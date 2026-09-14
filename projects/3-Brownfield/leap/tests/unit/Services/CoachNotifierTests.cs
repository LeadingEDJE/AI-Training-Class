using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// US5/#69 T105 — a delivery FAILURE must not fail the action that triggered it.
/// </summary>
/// <remarks>
/// <para>
/// This is the one case the integration suite cannot reach. Everything else about the notifier —
/// the message literals, the once-ever rule, the no-coach skip — is asserted against real PostgreSQL and
/// a real outbox in <c>tests/integration/Compass/CoachNotificationTests</c>, because those are claims
/// about data. A send that THROWS is not: <c>MockEmailSender</c> cannot be made to fail, and the failure
/// path is precisely the one that must not propagate.
/// </para>
/// <para>
/// Contract §6 step 3: the write the notice describes is already committed and attributable; the email is
/// informational. An SMTP outage must not retroactively fail an assignment that was ended successfully.
/// </para>
/// </remarks>
public class CoachNotifierTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- the doubles

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body, EmailFrom? From)> Sent { get; } = [];

        public Task SendAsync(string toEmail, string subject, string body, EmailBodyFormat format = EmailBodyFormat.Html,
        EmailFrom? from = null)
        {
            Sent.Add((toEmail, subject, body, from));
            return Task.CompletedTask;
        }
    }

    /// <param name="message">
    /// The exception text, which becomes the row's <c>error_message</c>. Parameterised so one test can
    /// hand over a message longer than the column, which is the shape that raises PostgreSQL
    /// <c>22001</c> at save time.
    /// </param>
    private sealed class ThrowingEmailSender(string message = "SMTP is down") : IEmailSender
    {
        public int Attempts { get; private set; }

        public Task SendAsync(string toEmail, string subject, string body, EmailBodyFormat format = EmailBodyFormat.Html,
        EmailFrom? from = null)
        {
            Attempts++;
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>Which step of the notice a transient database failure lands on.</summary>
    private enum FailingStep
    {
        /// <summary>The idempotency check, before anything has been read or written.</summary>
        IdempotencyCheck,

        /// <summary>Reading the facts, after the check said this notice is still owed.</summary>
        ReadingTheFacts,
    }

    /// <summary>
    /// Fails one step of the notifier's READ path the way an unreachable database does.
    /// </summary>
    /// <remarks>
    /// Neither step is a lost race, so neither surfaces as <c>CompassDuplicateKeyException</c> — they
    /// are the ordinary transient failures the guarded paths already assume can happen elsewhere.
    /// </remarks>
    private sealed class FailingCoachNotificationRepository(FailingStep step)
        : ICoachNotificationRepository
    {
        public List<NotificationLog> Logs { get; } = [];

        public Task<bool> HasNotifiedAsync(string notificationType, CancellationToken cancellationToken) =>
            step == FailingStep.IdempotencyCheck
                ? throw new InvalidOperationException("the database is unreachable")
                : Task.FromResult(false);

        public Task<CoachNotificationFacts?> GetAssignmentEndFactsAsync(
            int assignmentId,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("the database is unreachable");

        public Task<CoachNotificationFacts?> GetSowExtensionFactsAsync(
            int sowId,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("the database is unreachable");

        public Task AddLogAsync(NotificationLog log, CancellationToken cancellationToken)
        {
            log.Id = Logs.Count + 1;
            Logs.Add(log);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCoachNotificationRepository(CoachNotificationFacts? facts)
        : ICoachNotificationRepository
    {
        public List<NotificationLog> Logs { get; } = [];

        public Task<CoachNotificationFacts?> GetAssignmentEndFactsAsync(
            int assignmentId,
            CancellationToken cancellationToken
        ) => Task.FromResult(facts);

        public Task<CoachNotificationFacts?> GetSowExtensionFactsAsync(
            int sowId,
            CancellationToken cancellationToken
        ) => Task.FromResult(facts);

        public Task<bool> HasNotifiedAsync(
            string notificationType,
            CancellationToken cancellationToken
        ) => Task.FromResult(Logs.Any(log => log.NotificationType == notificationType));

        public Task AddLogAsync(NotificationLog log, CancellationToken cancellationToken)
        {
            log.Id = Logs.Count + 1;
            Logs.Add(log);
            return Task.CompletedTask;
        }
    }

    /// <summary>Applies the status transition the way the platform repository does, in memory.</summary>
    private sealed class FakeNotificationLogRepository(FakeCoachNotificationRepository store)
        : INotificationLogRepository
    {
        public Task UpdateStatusAsync(long id, string status, string? errorMessage)
        {
            var row = store.Logs.SingleOrDefault(log => log.Id == id);
            if (row is not null)
            {
                row.Status = status;
                row.ErrorMessage = errorMessage;
            }

            return Task.CompletedTask;
        }

        // ---- Not reached by the notifier.
        public Task<NotificationLog> AddAsync(NotificationLog log) => throw new NotSupportedException();

        public Task<NotificationLog?> GetByIdAsync(long id) => throw new NotSupportedException();

        public Task<NotificationLog?> GetByIdempotencyKeyAsync(
            string employeeId,
            string notificationType,
            DateOnly periodWeekStart
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<NotificationLog>> GetRecentAsync(int count, int offset) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<NotificationLog>> GetFailedForRetryAsync() =>
            throw new NotSupportedException();

        public Task<bool> TryClaimForRetryAsync(long id) => throw new NotSupportedException();

        public Task ReleaseRetryClaimAsync(long id) => throw new NotSupportedException();
    }

    private sealed class CountingUnitOfWork : ICompassUnitOfWork
    {
        /// <summary>When set, the FIRST save throws the way a lost unique-index race does.</summary>
        public bool ThrowDuplicateOnFirstSave { get; init; }

        /// <summary>
        /// When set, the LAST save — the one recording the delivery outcome — fails transiently.
        /// </summary>
        /// <remarks>
        /// A different failure from the claim race: nothing about this one is expected, and it lands
        /// after the email has already gone out, so there is nothing left to protect but the caller.
        /// </remarks>
        public bool ThrowTransientOnSecondSave { get; init; }

        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;

            if (ThrowTransientOnSecondSave && SaveCount == 2)
            {
                throw new InvalidOperationException("the database is unreachable");
            }

            if (ThrowDuplicateOnFirstSave && SaveCount == 1)
            {
                throw new CompassDuplicateKeyException(
                    "A concurrent write already committed a value with this name.",
                    new InvalidOperationException("23505")
                );
            }

            return Task.FromResult(0);
        }

        /// <summary>Not reached: the notifier runs AFTER its caller's atomic unit has committed.</summary>
        public Task<T> ExecuteAtomicallyAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            Func<T, bool> shouldCommit,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private static readonly CoachNotificationFacts Coached = new(
        EmployeeId: 7,
        EdjerName: "Ada Lovelace",
        ClientName: "Buckeye Mutual",
        EventDate: new DateOnly(2025, 9, 30),
        CoachEmail: "coach.mentor@example.test"
    );

    private static (
        CoachNotifier Notifier,
        ThrowingEmailSender Sender,
        FakeCoachNotificationRepository Store
    ) BuildWithFailingSender(CoachNotificationFacts? facts, string? senderMessage = null)
    {
        var store = new FakeCoachNotificationRepository(facts);
        var sender = senderMessage is null
            ? new ThrowingEmailSender()
            : new ThrowingEmailSender(senderMessage);
        var notifier = new CoachNotifier(
            store,
            new FakeNotificationLogRepository(store),
            new CountingUnitOfWork(),
            sender,
            NullLogger<CoachNotifier>.Instance
        );

        return (notifier, sender, store);
    }

    private static (CoachNotifier Notifier, ThrowingEmailSender Sender) BuildLosingTheClaimRace()
    {
        var store = new FakeCoachNotificationRepository(Coached);
        var sender = new ThrowingEmailSender();
        var notifier = new CoachNotifier(
            store,
            new FakeNotificationLogRepository(store),
            new CountingUnitOfWork { ThrowDuplicateOnFirstSave = true },
            sender,
            NullLogger<CoachNotifier>.Instance
        );

        return (notifier, sender);
    }

    // ---------------------------------------------------------------- the extension trigger

    [Fact]
    public async Task NotifySowExtensionAdded_ComposesTheExtensionBody_NotTheAssignmentEndOne()
    {
        // Arrange — the two triggers share one dispatch path and differ only in their body, so a test
        // that only ever exercises the assignment-end lambda leaves the SOW one unproven. Both bodies
        // are asserted verbatim against a real outbox in the integration suite; this proves the SOW
        // entry point reaches its own lambda rather than the other one.
        var store = new FakeCoachNotificationRepository(Coached);
        var sender = new CapturingEmailSender();
        var notifier = new CoachNotifier(
            store,
            new FakeNotificationLogRepository(store),
            new CountingUnitOfWork(),
            sender,
            NullLogger<CoachNotifier>.Instance
        );

        // Act
        await notifier.NotifySowExtensionAddedAsync(7, Token);

        // Assert
        var sent = sender.Sent.ShouldHaveSingleItem();
        sent.Subject.ShouldBe("Ada Lovelace Assignment Change");
        sent.Body.ShouldBe(
            "Ada Lovelace has received an updated SOW at Buckeye Mutual through 09/30/2025"
        );
        sent.From.ShouldNotBeNull().Address.ShouldBe("no-reply@leadingedje.com", "AC-43: the SOW trigger too");
        store.Logs.ShouldHaveSingleItem()
            .NotificationType.ShouldBe(
                "compass.sow-extension-added.7",
                "the entity id travels in the type, and the two triggers must not share a namespace"
            );
    }

    // ---------------------------------------------------------------- AC-43 sender (#531) + persistence (#532)

    [Fact]
    public async Task TheAssignmentEndNotice_SendsFromNoReplyAddressOnly()
    {
        // Arrange — AC-43/FR-029: coach notices send from no-reply@leadingedje.com, NOT the app-wide
        // Timesheet Email:FromAddress. Address-only: no display name leaks the Timesheet sender's name.
        var store = new FakeCoachNotificationRepository(Coached);
        var sender = new CapturingEmailSender();
        var notifier = new CoachNotifier(
            store,
            new FakeNotificationLogRepository(store),
            new CountingUnitOfWork(),
            sender,
            NullLogger<CoachNotifier>.Instance
        );

        // Act
        await notifier.NotifyAssignmentEndedAsync(7, Token);

        // Assert
        var sent = sender.Sent.ShouldHaveSingleItem();
        var from = sent.From.ShouldNotBeNull();
        from.Address.ShouldBe("no-reply@leadingedje.com");
        from.DisplayName.ShouldBe("", "address-only; the Timesheet display name must not leak in");
    }

    [Fact]
    public async Task TheLogRow_StoresTheRenderedMessageAndSender_SoARetryReSendsTheOriginal()
    {
        // Arrange — #532: the retry worker re-sends from the log row, so the row must carry the exact
        // message and sender rather than forcing a placeholder.
        var store = new FakeCoachNotificationRepository(Coached);
        var sender = new CapturingEmailSender();
        var notifier = new CoachNotifier(
            store,
            new FakeNotificationLogRepository(store),
            new CountingUnitOfWork(),
            sender,
            NullLogger<CoachNotifier>.Instance
        );

        // Act
        await notifier.NotifyAssignmentEndedAsync(7, Token);

        // Assert — the stored row matches what was sent, character for character.
        var sent = sender.Sent.ShouldHaveSingleItem();
        var log = store.Logs.ShouldHaveSingleItem();
        log.Subject.ShouldBe(sent.Subject);
        log.Body.ShouldBe(sent.Body);
        log.FromAddress.ShouldBe("no-reply@leadingedje.com");
        log.FromName.ShouldBe("");
    }

    // ---------------------------------------------------------------- the subject went away

    [Fact]
    public async Task WhenTheSubjectNoLongerExists_NothingIsSentAndNothingIsRecorded()
    {
        // Arrange — the entity was removed between the commit and this call. There is nobody to notify
        // and nothing true to say, so there is nothing to record either: writing a log row here would
        // claim an idempotency slot for a notice that never had a subject.
        var store = new FakeCoachNotificationRepository(null);
        var sender = new CapturingEmailSender();
        var notifier = new CoachNotifier(
            store,
            new FakeNotificationLogRepository(store),
            new CountingUnitOfWork(),
            sender,
            NullLogger<CoachNotifier>.Instance
        );

        // Act
        await Should.NotThrowAsync(() => notifier.NotifyAssignmentEndedAsync(404, Token));

        // Assert
        sender.Sent.ShouldBeEmpty();
        store.Logs.ShouldBeEmpty();
    }

    // ------------------------------- the read path fails transiently (PR #310 review finding)

    [Fact]
    public async Task WhenTheIdempotencyCheckFails_TheAlreadyCommittedWriteIsNotAffected()
    {
        // Arrange -- contract §6 step 3 is a claim about the WHOLE notice, not about the send alone.
        // Both callers invoke the notifier after their write has committed, so anything that escapes
        // here turns a successful assignment-end into an HTTP 500. The guarded send was never the only
        // thing that can fail: this read runs first, and a transient database error is ordinary.
        var store = new FailingCoachNotificationRepository(FailingStep.IdempotencyCheck);
        var sender = new CapturingEmailSender();
        var notifier = new CoachNotifier(
            store,
            new FakeNotificationLogRepository(new FakeCoachNotificationRepository(Coached)),
            new CountingUnitOfWork(),
            sender,
            NullLogger<CoachNotifier>.Instance
        );

        // Act
        await Should.NotThrowAsync(() => notifier.NotifyAssignmentEndedAsync(7, Token));

        // Assert -- and nothing was claimed, so the notice is still owed and a later edit can send it.
        sender.Sent.ShouldBeEmpty();
        store.Logs.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenReadingTheFactsFails_TheAlreadyCommittedWriteIsNotAffected()
    {
        // Arrange -- the second unguarded read. Distinct from "the subject no longer exists", which is
        // an ordinary null answer: this is the query itself failing.
        var store = new FailingCoachNotificationRepository(FailingStep.ReadingTheFacts);
        var sender = new CapturingEmailSender();
        var notifier = new CoachNotifier(
            store,
            new FakeNotificationLogRepository(new FakeCoachNotificationRepository(Coached)),
            new CountingUnitOfWork(),
            sender,
            NullLogger<CoachNotifier>.Instance
        );

        // Act
        await Should.NotThrowAsync(() => notifier.NotifySowExtensionAddedAsync(7, Token));

        // Assert
        sender.Sent.ShouldBeEmpty();
        store.Logs.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRecordingTheDeliveryOutcomeFails_TheAlreadyCommittedWriteIsNotAffected()
    {
        // Arrange -- the far end of the same path. The email has GONE OUT by the time this save runs,
        // so failing the caller here would report a failure for two things that both succeeded.
        var store = new FakeCoachNotificationRepository(Coached);
        var sender = new CapturingEmailSender();
        var notifier = new CoachNotifier(
            store,
            new FakeNotificationLogRepository(store),
            new CountingUnitOfWork { ThrowTransientOnSecondSave = true },
            sender,
            NullLogger<CoachNotifier>.Instance
        );

        // Act
        await Should.NotThrowAsync(() => notifier.NotifyAssignmentEndedAsync(7, Token));

        // Assert -- the send happened, and the claim row still holds the slot.
        sender.Sent.ShouldHaveSingleItem();
        store.Logs.ShouldHaveSingleItem();
    }

    // ------------------------------------------- the concurrent claim (PR #304 review finding)

    [Fact]
    public async Task LosingTheRaceToClaimTheNotice_DoesNotPropagateToTheCaller()
    {
        // Arrange — ix_notification_log_idempotency is UNIQUE on
        // (EmployeeId, NotificationType, PeriodWeekStart), so two callers passing HasNotifiedAsync
        // before either inserts means the loser's SaveChanges throws. The write this notice describes
        // has ALREADY COMMITTED by then, so letting that propagate turns a successful assignment-end
        // into an HTTP 500 — the same failure the send's try/catch exists to prevent, one step earlier.
        var (notifier, _) = BuildLosingTheClaimRace();

        // Act / Assert
        await Should.NotThrowAsync(() => notifier.NotifyAssignmentEndedAsync(42, Token));
    }

    [Fact]
    public async Task LosingTheRaceToClaimTheNotice_SendsNothing_SoTheCoachHearsOnceNotTwice()
    {
        // Arrange — the winner of the race sends. If the loser sent too, the unique index would have
        // prevented a duplicate ROW while still producing a duplicate EMAIL, which is the outcome
        // AC-29 actually cares about.
        var (notifier, sender) = BuildLosingTheClaimRace();

        // Act
        await notifier.NotifyAssignmentEndedAsync(42, Token);

        // Assert
        sender.Attempts.ShouldBe(0);
    }

    // ---------------------------------------------------------------- contract §6 step 3

    [Fact]
    public async Task ASendFailure_DoesNotPropagateToTheCaller()
    {
        // Arrange — the triggering write has already committed by the time the notifier runs, so throwing
        // here would fail an action that already succeeded, and the operator would have no way to tell
        // which half went wrong.
        var (notifier, sender, _) = BuildWithFailingSender(Coached);

        // Act / Assert
        await Should.NotThrowAsync(() => notifier.NotifyAssignmentEndedAsync(42, Token));
        sender.Attempts.ShouldBe(1, "the send was genuinely attempted, not skipped");
    }

    [Fact]
    public async Task ASendFailure_IsRecordedAsFailedWithItsError_NotAsSkipped()
    {
        // Arrange — FR-035's distinction seen from the other side. A skip is a normal outcome; a delivery
        // failure is not, and an operator has to be able to tell them apart in the log.
        var (notifier, _, store) = BuildWithFailingSender(Coached);

        // Act
        await notifier.NotifyAssignmentEndedAsync(42, Token);

        // Assert
        var log = store.Logs.ShouldHaveSingleItem();
        log.Status.ShouldBe("Failed");
        log.ErrorMessage.ShouldBe("SMTP is down");
        log.RecipientEmail.ShouldBe("coach.mentor@example.test", "there WAS someone to address it to");
    }

    [Fact]
    public async Task ASendFailureWithAnOverLongMessage_IsCutToTheColumnBoundBeforeItIsRecorded()
    {
        // Arrange — #600. `notification_logs.error_message` is varchar(2000) and this writer hands it the
        // exception's own text. An SMTP or template failure whose message runs long makes PostgreSQL
        // raise 22001 on the save below, which escapes NotifyAsync's guard through the SAME
        // SaveChangesAsync the guard does not cover — failing a Compass write that already committed, and
        // leaving the row Pending with no reason recorded at all. Every writer of this column truncates
        // to NotificationErrorMessage.MaxLength; this one is the second of the three.
        var (notifier, _, store) = BuildWithFailingSender(Coached, new string('x', 5_000));

        // Act
        await notifier.NotifyAssignmentEndedAsync(42, Token);

        // Assert
        var log = store.Logs.ShouldHaveSingleItem();
        log.Status.ShouldBe("Failed");
        log.ErrorMessage.ShouldNotBeNull();
        log.ErrorMessage.Length.ShouldBe(NotificationErrorMessage.MaxLength);
        log.ErrorMessage.ShouldStartWith("xxxxx");
        log.ErrorMessage.ShouldEndWith(
            NotificationErrorMessage.TruncationMarker,
            customMessage: "a cut message must announce itself, or the reader trusts a half-sentence");
    }

    [Fact]
    public async Task AFailedSend_StillClaimsTheIdempotencySlot()
    {
        // Arrange — the slot is claimed before the send, so a failure does not leave the door open for a
        // second notice on the next edit. AC-29 says at most one; re-delivery is the retry worker's job,
        // not a side effect of editing the assignment again.
        var (notifier, sender, store) = BuildWithFailingSender(Coached);

        // Act
        await notifier.NotifyAssignmentEndedAsync(42, Token);
        await notifier.NotifyAssignmentEndedAsync(42, Token);

        // Assert
        store.Logs.Count.ShouldBe(1, "the second call found the first's record");
        sender.Attempts.ShouldBe(1, "and did not try to send again");
    }

    [Fact]
    public async Task AnEdjerWithNoCoach_IsNeverHandedToTheSenderAtAll()
    {
        // Arrange — US5/FR-034 at the unit level: the guard is BEFORE the send, not a caught exception
        // from attempting to mail a null address. A throwing sender proves the difference — if the
        // notifier reached it, Attempts would be 1 and the row would read Failed rather than Skipped.
        var (notifier, sender, store) = BuildWithFailingSender(Coached with { CoachEmail = null });

        // Act
        await Should.NotThrowAsync(() => notifier.NotifyAssignmentEndedAsync(42, Token));

        // Assert
        sender.Attempts.ShouldBe(0, "no coach means no send attempt, not a failed one");
        store.Logs.ShouldHaveSingleItem().Status.ShouldBe("Skipped");
    }
}
