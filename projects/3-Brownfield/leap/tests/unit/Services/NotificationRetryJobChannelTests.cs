using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Jobs;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using LeadingEDJE.Leap.Api.Platform.Services.Slack;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Covers the two branches of <see cref="NotificationRetryJob"/> that no test reached: the Slack
/// re-dispatch, and the failure path that records a retry as failed rather than sent.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NotificationRetryJobTests"/> covers the email channel and the <c>RetryCount &lt; 1</c>
/// filter. Left uncovered were the Slack send and the whole <c>catch</c> — and the catch is the more
/// interesting of the two, because it is what decides whether a notification is marked <c>Failed</c>
/// with a reason or silently recorded as <c>Sent</c>. A retry that throws and is then logged as sent
/// would make the retry ledger lie, and nothing in the suite would have noticed.
/// </para>
/// <para>
/// <c>MockEmailSender</c> cannot be made to fail — the observation
/// <see cref="CoachNotifierTests"/> already records — so the failure path needs a throwing double.
/// </para>
/// </remarks>
public class NotificationRetryJobChannelTests
{
    private const string Employee = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private sealed class ThrowingEmailSender(string message) : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string body, EmailBodyFormat format = EmailBodyFormat.Html,
        EmailFrom? from = null) =>
            throw new InvalidOperationException(message);
    }

    /// <summary>
    /// A context whose <c>SaveChangesAsync</c> always throws, standing in for the PostgreSQL errors
    /// the job's persistence boundary can raise.
    /// </summary>
    /// <remarks>
    /// <c>22001</c> on an over-long column, a lost connection, a deadlock.
    /// The job resolves <see cref="LeapDbContext"/> concretely, so a subclass registered under that
    /// service type is the seam. Only the cancellation-token overload is overridden because that is the
    /// one the job calls (a bare <c>SaveChangesAsync()</c> binds to it with <c>default</c>).
    /// </remarks>
    private sealed class SaveFailingDbContext(DbContextOptions<LeapDbContext> options, string message)
        : LeapDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }

    private sealed class TestServiceScopeFactory(IServiceProvider provider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(provider);

        private sealed class Scope(IServiceProvider provider) : IServiceScope
        {
            public IServiceProvider ServiceProvider => provider;
            public void Dispose() { }
        }
    }

    /// <summary>A job wired to the given doubles, plus the log repository it will read and write.</summary>
    /// <remarks>
    /// The <see cref="LeapDbContext"/> is present only because the job resolves one to close its
    /// persistence boundary (#591). It is an empty InMemory context and the log repository above is a
    /// <c>List&lt;&gt;</c>, so saving through it proves nothing — which is exactly why the persistence
    /// itself is guarded by <c>NotificationRetryJobPersistenceTests</c> against real PostgreSQL. These
    /// tests cover channel selection and the failure branch; do not extend them to persistence.
    /// </remarks>
    private static (NotificationRetryJob Job, InMemoryNotificationLogRepository Logs) Build(
        IEmailSender? emailSender = null,
        MockSlackClient? slackClient = null,
        string? saveFailsWith = null)
    {
        var logs = new InMemoryNotificationLogRepository();

        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton<INotificationLogRepository>(logs);
        services.AddSingleton(emailSender ?? new MockEmailSender());
        services.AddSingleton<ISlackClient>(slackClient ?? new MockSlackClient());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(saveFailsWith is null
            ? new LeapDbContext(options)
            : new SaveFailingDbContext(options, saveFailsWith));

        return (
            new NotificationRetryJob(new TestServiceScopeFactory(services.BuildServiceProvider())),
            logs);
    }

    private static NotificationLog Failed(
        string channel, string? recipientEmail = null, string? slackUserId = null) =>
        new()
        {
            EmployeeId = Employee,
            NotificationType = "late_reminder_Fri5pm",
            PeriodWeekStart = new DateOnly(2026, 3, 16),
            Channel = channel,
            Status = "Failed",
            RecipientEmail = recipientEmail,
            SlackUserId = slackUserId,
            RetryCount = 0,
            // Inside the 30-minute window the job filters on.
            CreatedAt = DateTime.UtcNow.AddMinutes(-3),
        };

    private static IJobExecutionContext Context() =>
        new TestJobExecutionContext("RetryEvery5min");

    [Fact]
    public async Task ASlackChannelFailure_IsRetriedOverSlack()
    {
        // Arrange
        var slack = new MockSlackClient();
        var (job, logs) = Build(slackClient: slack);
        await logs.AddAsync(Failed("slack", slackUserId: "U12345"));

        // Act
        await job.Execute(Context());

        // Assert
        var sent = slack.SentMessages.ShouldHaveSingleItem();
        sent.SlackUserId.ShouldBe("U12345");
        sent.Payload.Subject.ShouldBe("Retry: late_reminder_Fri5pm");
        sent.Payload.Body.ShouldContain("2026-03-16");
    }

    [Fact]
    public async Task ABothChannelFailure_IsRetriedOverEmailAndSlack()
    {
        // Arrange -- "both" is the one value that satisfies each channel test independently, so it is
        // the case where a mis-written condition sends only half the retry.
        var slack = new MockSlackClient();
        var email = new MockEmailSender();
        var (job, logs) = Build(email, slack);
        await logs.AddAsync(Failed("both", "coach@leadingedje.com", "U12345"));

        // Act
        await job.Execute(Context());

        // Assert
        email.SentEmails.ShouldHaveSingleItem().To.ShouldBe("coach@leadingedje.com");
        slack.SentMessages.ShouldHaveSingleItem().SlackUserId.ShouldBe("U12345");
    }

    [Fact]
    public async Task ASlackChannelFailureWithNoSlackUserId_SendsNothing()
    {
        // Arrange -- the guard beside the channel test. Without a Slack id there is nobody to send to,
        // and the job must not fall back to email for a Slack-only notification.
        var slack = new MockSlackClient();
        var email = new MockEmailSender();
        var (job, logs) = Build(email, slack);
        await logs.AddAsync(Failed("slack", recipientEmail: "coach@leadingedje.com"));

        // Act
        await job.Execute(Context());

        // Assert
        slack.SentMessages.ShouldBeEmpty();
        email.SentEmails.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASuccessfulRetry_IsRecordedAsSent()
    {
        // Arrange
        var (job, logs) = Build();
        var log = await logs.AddAsync(Failed("email", "coach@leadingedje.com"));

        // Act
        await job.Execute(Context());

        // Assert
        var stored = await logs.GetByIdAsync(log.Id);
        stored.ShouldNotBeNull();
        stored.Status.ShouldBe("Sent");
        stored.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task ARetryThatThrows_IsRecordedAsFailedWithTheReason()
    {
        // Arrange -- the ledger has to stay truthful. Recording this as Sent would claim a delivery
        // that never happened, and the row is the only evidence anyone has.
        var (job, logs) = Build(new ThrowingEmailSender("smtp refused the connection"));
        var log = await logs.AddAsync(Failed("email", "coach@leadingedje.com"));

        // Act
        await job.Execute(Context());

        // Assert
        var stored = await logs.GetByIdAsync(log.Id);
        stored.ShouldNotBeNull();
        stored.Status.ShouldBe("Failed");
        stored.ErrorMessage.ShouldBe("smtp refused the connection");
    }

    [Fact]
    public async Task ARetryThatThrowsAnOverLongReason_TruncatesItToTheColumnBound()
    {
        // Arrange -- error_message is varchar(2000). Writing a longer reason makes PostgreSQL raise 22001,
        // which fails the SaveChanges, aborts the pass, and leaves retry_count at 0 -- reproducing the very
        // duplicate storm #591 exists to stop. The full text is not lost: LogError already carries the
        // whole exception.
        var (job, logs) = Build(new ThrowingEmailSender(new string('x', 5_000)));
        var log = await logs.AddAsync(Failed("email", "coach@leadingedje.com"));

        // Act
        await job.Execute(Context());

        // Assert
        var stored = await logs.GetByIdAsync(log.Id);
        stored.ShouldNotBeNull();
        stored.Status.ShouldBe("Failed");
        stored.ErrorMessage.ShouldNotBeNull();
        // 2000 and the marker are pinned verbatim here: the bound must match
        // NotificationLogConfiguration (guarded by NotificationErrorMessageTests), and a change to
        // the marker is a change to what an operator reads in the log viewer.
        stored.ErrorMessage.Length.ShouldBeLessThanOrEqualTo(2000);
        stored.ErrorMessage.ShouldStartWith("xxxxx");
        stored.ErrorMessage.ShouldEndWith(
            "… [truncated]",
            customMessage: "a cut message must announce itself, or the reader trusts a half-sentence");
    }

    [Fact]
    public async Task ARetryWithNoUsableRecipient_IsRecordedAsSkippedNotSent()
    {
        // Arrange -- the reachable shape from #552: a Slack-preferring EDJEr whose id never resolved, so
        // NotificationService fell back to email and the send failed. The row is Channel="slack",
        // SlackUserId=null. Both branches below are skipped and nothing throws, so the pass used to fall
        // through to "Sent" -- stamping sent_at and ERASING the original reason for a notice that was
        // never delivered to anyone.
        var slack = new MockSlackClient();
        var email = new MockEmailSender();
        var (job, logs) = Build(email, slack);
        var row = Failed("slack", recipientEmail: "coach@leadingedje.com");
        row.ErrorMessage = "the original failure";
        var log = await logs.AddAsync(row);

        // Act
        await job.Execute(Context());

        // Assert
        slack.SentMessages.ShouldBeEmpty();
        email.SentEmails.ShouldBeEmpty();

        var stored = await logs.GetByIdAsync(log.Id);
        stored.ShouldNotBeNull();
        stored.Status.ShouldBe("Skipped", "nothing was attempted, so the ledger must not claim a delivery");
        stored.SentAt.ShouldBeNull();
        stored.ErrorMessage.ShouldNotBeNullOrEmpty();
        stored.ErrorMessage.ShouldContain(
            "the original failure",
            customMessage: "the reason the notice first failed is the only diagnostic there is");
        stored.RetryCount.ShouldBe(1, "the row must leave the retry set rather than be re-examined forever");
    }

    [Fact]
    public async Task ARetryOfARowWithAnUnrecognisedChannel_SaysSoInsteadOfBlamingTheRecipient()
    {
        // Arrange -- the OTHER way a row reaches Skipped: not "the address is missing" but "this is not a
        // channel anything here can deliver on". Both land in the same branch, and one reason string for
        // the two misdirects whoever reads it -- "no usable recipient on this row" sends them looking for
        // a missing slack_user_id on a row whose problem is the word 'teams'.
        var slack = new MockSlackClient();
        var email = new MockEmailSender();
        var (job, logs) = Build(email, slack);
        var log = await logs.AddAsync(Failed("teams", "coach@leadingedje.com", "U12345"));

        // Act
        await job.Execute(Context());

        // Assert
        email.SentEmails.ShouldBeEmpty();
        slack.SentMessages.ShouldBeEmpty();

        var stored = await logs.GetByIdAsync(log.Id);
        stored.ShouldNotBeNull();
        stored.Status.ShouldBe("Skipped");
        stored.ErrorMessage.ShouldNotBeNull();
        stored.ErrorMessage.ShouldContain("teams");
        stored.ErrorMessage.ShouldNotContain(
            "no usable recipient",
            customMessage: "this row HAS both a recipient_email and a slack_user_id; the channel is what "
                + "cannot be delivered on");
    }

    [Fact]
    public async Task ABothChannelRetryDeliveredOnOneHalf_IsSentButRecordsTheSkippedHalf()
    {
        // Arrange -- Channel="both" with no Slack id. The email really is delivered, so this is NOT a
        // skip and must NOT be retried (that would duplicate the email). It is also not a clean success:
        // half the notice never went. Recording it as Sent with the shortfall in error_message is the
        // deliberate middle, and it is what stops the ledger claiming full delivery.
        var slack = new MockSlackClient();
        var email = new MockEmailSender();
        var (job, logs) = Build(email, slack);
        var log = await logs.AddAsync(Failed("both", "coach@leadingedje.com"));

        // Act
        await job.Execute(Context());

        // Assert
        email.SentEmails.ShouldHaveSingleItem();
        slack.SentMessages.ShouldBeEmpty();

        var stored = await logs.GetByIdAsync(log.Id);
        stored.ShouldNotBeNull();
        stored.Status.ShouldBe("Sent");
        stored.ErrorMessage.ShouldNotBeNullOrEmpty("a half-delivered notice recorded as cleanly sent is a lie");
        stored.ErrorMessage.ShouldContain("Slack");
    }

    [Fact]
    public async Task ABothChannelRetryWithNoRecipientEmail_IsSentButRecordsTheMissingEmail()
    {
        // Arrange -- the mirror of the case above, and the one that is easy to leave unwritten. Slack
        // carried the notice; the email half had no address. Same ruling, opposite half.
        var slack = new MockSlackClient();
        var email = new MockEmailSender();
        var (job, logs) = Build(email, slack);
        var log = await logs.AddAsync(Failed("both", slackUserId: "U12345"));

        // Act
        await job.Execute(Context());

        // Assert
        slack.SentMessages.ShouldHaveSingleItem();
        email.SentEmails.ShouldBeEmpty();

        var stored = await logs.GetByIdAsync(log.Id);
        stored.ShouldNotBeNull();
        stored.Status.ShouldBe("Sent");
        stored.ErrorMessage.ShouldNotBeNullOrEmpty();
        stored.ErrorMessage.ShouldContain("recipient_email");
    }

    [Fact]
    public async Task ASaveFailure_AbortsThePassAndLeavesTheLaterRowsUnattempted()
    {
        // Arrange -- the branch every other test walks past. When the persistence boundary itself fails,
        // the row's changes stay pending in the scoped change tracker, so every later save in this loop
        // would re-attempt and fail identically. The job rethrows rather than continuing; this pins that
        // choice and the fact that the second row is left for the next pass.
        var email = new MockEmailSender();
        var (job, logs) = Build(email, saveFailsWith: "23505: duplicate key value violates unique constraint");
        await logs.AddAsync(Failed("email", "one@leadingedje.com"));
        await logs.AddAsync(Failed("email", "two@leadingedje.com"));

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => job.Execute(Context()));

        // Assert
        thrown.Message.ShouldContain("23505");
        email.SentEmails.Count.ShouldBe(
            1,
            "the pass aborts on the first row whose outcome cannot be persisted; the rest wait for the "
                + "next five-minute pass rather than being silently dropped");
    }

    [Fact]
    public async Task ASaveFailure_ReleasesTheClaimSoTheRowRetriesRatherThanStranding()
    {
        // Arrange -- the claim commits in its own statement before the send, decoupled from the
        // outcome save. If that save throws, retry_count = 1 is already durable while status never
        // caught up, so RetryCount < 1 would exclude the row forever with a stale status. The pass must
        // release the claim so the next pass picks the row up again.
        var email = new MockEmailSender();
        var (job, logs) = Build(email, saveFailsWith: "23505: duplicate key value violates unique constraint");
        var seeded = await logs.AddAsync(Failed("email", "one@leadingedje.com"));

        // Act
        await Should.ThrowAsync<InvalidOperationException>(() => job.Execute(Context()));

        // Assert
        var row = await logs.GetByIdAsync(seeded.Id);
        row.ShouldNotBeNull();
        row.RetryCount.ShouldBe(
            0,
            "a save failure must release the claim so the row is retried, not stranded at retry_count = 1");
    }

    [Fact]
    public async Task WhenTheClaimReleaseAlsoFails_TheOriginalSaveErrorStillAbortsThePass()
    {
        // Arrange -- the save fails and the best-effort release then fails too. The pass must still
        // abort on the ORIGINAL persist error, not the release error, and swallow the release failure.
        var email = new MockEmailSender();
        var inner = new InMemoryNotificationLogRepository();
        var repository = new ReleaseThrowingRepository(inner);
        var seeded = await inner.AddAsync(Failed("email", "one@leadingedje.com"));

        var services = new ServiceCollection();
        services.AddSingleton<INotificationLogRepository>(repository);
        services.AddSingleton<IEmailSender>(email);
        services.AddSingleton<ISlackClient>(new MockSlackClient());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<LeapDbContext>(new SaveFailingDbContext(
            new DbContextOptionsBuilder<LeapDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            "23505: duplicate key value violates unique constraint"));

        var job = new NotificationRetryJob(new TestServiceScopeFactory(services.BuildServiceProvider()));

        // Act
        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => job.Execute(Context()));

        // Assert
        // the pass aborts on the persist error, not the release error
        thrown.Message.ShouldContain("23505");
        var row = await inner.GetByIdAsync(seeded.Id);
        row!.RetryCount.ShouldBe(1, "when the release fails the row stays claimed -- logged, not crashed");
    }

    /// <summary>Delegates to a real in-memory repository, but every claim release throws.</summary>
    private sealed class ReleaseThrowingRepository(InMemoryNotificationLogRepository inner) : INotificationLogRepository
    {
        public Task ReleaseRetryClaimAsync(long id) =>
            throw new InvalidOperationException("the release could not reach the database either");

        public Task<bool> TryClaimForRetryAsync(long id) => inner.TryClaimForRetryAsync(id);

        public Task<NotificationLog> AddAsync(NotificationLog log) => inner.AddAsync(log);

        public Task<NotificationLog?> GetByIdAsync(long id) => inner.GetByIdAsync(id);

        public Task<NotificationLog?> GetByIdempotencyKeyAsync(
            string employeeId, string notificationType, DateOnly periodWeekStart) =>
            inner.GetByIdempotencyKeyAsync(employeeId, notificationType, periodWeekStart);

        public Task<IReadOnlyList<NotificationLog>> GetRecentAsync(int count, int offset) =>
            inner.GetRecentAsync(count, offset);

        public Task UpdateStatusAsync(long id, string status, string? errorMessage) =>
            inner.UpdateStatusAsync(id, status, errorMessage);

        public Task<IReadOnlyList<NotificationLog>> GetFailedForRetryAsync() =>
            inner.GetFailedForRetryAsync();
    }

    [Fact]
    public async Task ARetryThatThrows_DoesNotAbortTheRemainingRetries()
    {
        // Arrange -- one poisoned row must not strand the rest of the batch. The try/catch sits INSIDE
        // the loop, and this is the assertion that keeps it there.
        var (job, logs) = Build(new ThrowingEmailSender("smtp refused the connection"));
        var first = await logs.AddAsync(Failed("email", "one@leadingedje.com"));
        var second = await logs.AddAsync(Failed("email", "two@leadingedje.com"));

        // Act
        await job.Execute(Context());

        // Assert
        (await logs.GetByIdAsync(first.Id))!.Status.ShouldBe("Failed");
        (await logs.GetByIdAsync(second.Id))!.Status.ShouldBe("Failed");
    }

    [Fact]
    public async Task AFailureOlderThanThirtyMinutes_IsNotRetried()
    {
        // Arrange -- the other half of the filter NotificationRetryJobTests covers by RetryCount. The
        // window matters because the job runs every five minutes: without it, an old failure would be
        // re-sent long after the reason for it passed.
        var email = new MockEmailSender();
        var (job, logs) = Build(email);
        var stale = Failed("email", "coach@leadingedje.com");
        stale.CreatedAt = DateTime.UtcNow.AddMinutes(-31);
        await logs.AddAsync(stale);

        // Act
        await job.Execute(Context());

        // Assert
        email.SentEmails.ShouldBeEmpty();
    }

    [Fact]
    public async Task ARetryWithAStoredMessage_ReSendsTheOriginalSubjectBodyAndSender()
    {
        // Arrange -- #531/#532: a failed Compass coach notice carries its rendered message and its
        // no-reply@ sender on the log row, so the retry re-sends the real AC-43 notice, not a placeholder
        // from the Timesheet address. This is the case CP2 finding 3 flagged.
        var email = new MockEmailSender();
        var (job, logs) = Build(email);
        var row = Failed("email", "coach.mentor@example.test");
        row.NotificationType = "compass.assignment-ended.42";
        row.Subject = "Ada Lovelace Assignment Change";
        row.Body = "Ada Lovelace's assignment at Buckeye Mutual is coming to an end on 09/30/2025";
        row.FromAddress = "no-reply@leadingedje.com";
        row.FromName = "";
        await logs.AddAsync(row);

        // Act
        await job.Execute(Context());

        // Assert -- the stored message, verbatim, from the stored sender; NOT the generic placeholder.
        var sent = email.SentEmails.ShouldHaveSingleItem();
        sent.To.ShouldBe("coach.mentor@example.test");
        sent.Subject.ShouldBe("Ada Lovelace Assignment Change");
        sent.Body.ShouldBe("Ada Lovelace's assignment at Buckeye Mutual is coming to an end on 09/30/2025");
        sent.From.ShouldNotBeNull().Address.ShouldBe("no-reply@leadingedje.com");
    }

    [Fact]
    public async Task ARetryWithNoStoredMessage_FallsBackToTheGenericPlaceholderAndDefaultSender()
    {
        // Arrange -- rows written before the message columns existed have null Subject/Body/FromAddress.
        // The retry must still work: generic content, default (config) sender. This pins backward-compat
        // so the expand migration is safe against a mid-rollout mix of old and new rows.
        var email = new MockEmailSender();
        var (job, logs) = Build(email);
        await logs.AddAsync(Failed("email", "coach@leadingedje.com")); // Subject/Body/FromAddress all null

        // Act
        await job.Execute(Context());

        // Assert
        var sent = email.SentEmails.ShouldHaveSingleItem();
        sent.Subject.ShouldBe("Notification Retry: late_reminder_Fri5pm");
        sent.Body.ShouldContain("Retrying notification type");
        sent.From.ShouldBeNull("a null FromAddress means the configured default sender");
    }

    [Fact]
    public async Task AClaimLostToAnotherReplica_SendsNothingAndLeavesTheRow()
    {
        // Arrange -- the row passes the RetryCount < 1 filter, but another replica wins the claim
        // between this pass's read and its claim (#599). The job must skip: no send, no status write.
        var inner = new InMemoryNotificationLogRepository();
        var seeded = await inner.AddAsync(Failed("email", recipientEmail: "coach@leadingedje.com"));
        var repository = new ClaimLostRepository(inner);
        var emailSender = new MockEmailSender();

        var services = new ServiceCollection();
        services.AddSingleton<INotificationLogRepository>(repository);
        services.AddSingleton<IEmailSender>(emailSender);
        services.AddSingleton<ISlackClient>(new MockSlackClient());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(new LeapDbContext(
            new DbContextOptionsBuilder<LeapDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));

        var job = new NotificationRetryJob(new TestServiceScopeFactory(services.BuildServiceProvider()));

        // Act
        await job.Execute(Context());

        // Assert
        emailSender.SentEmails.ShouldBeEmpty("a pass that lost the claim must not send");

        var row = await inner.GetByIdAsync(seeded.Id);
        row.ShouldNotBeNull();
        row.Status.ShouldBe("Failed", "the losing pass must leave the row for the winner to resolve");
    }

    /// <summary>Delegates to a real in-memory repository but never wins the retry claim.</summary>
    private sealed class ClaimLostRepository(InMemoryNotificationLogRepository inner) : INotificationLogRepository
    {
        public Task<bool> TryClaimForRetryAsync(long id) => Task.FromResult(false);

        public Task ReleaseRetryClaimAsync(long id) => inner.ReleaseRetryClaimAsync(id);

        public Task<NotificationLog> AddAsync(NotificationLog log) => inner.AddAsync(log);

        public Task<NotificationLog?> GetByIdAsync(long id) => inner.GetByIdAsync(id);

        public Task<NotificationLog?> GetByIdempotencyKeyAsync(
            string employeeId, string notificationType, DateOnly periodWeekStart) =>
            inner.GetByIdempotencyKeyAsync(employeeId, notificationType, periodWeekStart);

        public Task<IReadOnlyList<NotificationLog>> GetRecentAsync(int count, int offset) =>
            inner.GetRecentAsync(count, offset);

        public Task UpdateStatusAsync(long id, string status, string? errorMessage) =>
            inner.UpdateStatusAsync(id, status, errorMessage);

        public Task<IReadOnlyList<NotificationLog>> GetFailedForRetryAsync() =>
            inner.GetFailedForRetryAsync();
    }
}
