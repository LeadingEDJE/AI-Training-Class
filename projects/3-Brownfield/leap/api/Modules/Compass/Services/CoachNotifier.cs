using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Microsoft.Extensions.Logging;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <inheritdoc cref="ICoachNotifier" />
public class CoachNotifier(
    ICoachNotificationRepository notifications,
    INotificationLogRepository notificationLogs,
    ICompassUnitOfWork unitOfWork,
    IEmailSender emailSender,
    ILogger<CoachNotifier> logger
) : ICoachNotifier
{
    /// <summary>The notification-type prefix, so a Compass row is identifiable in a shared table.</summary>
    /// <remarks>
    /// Obligation O-1: these rows are load-bearing for a business rule. Deleting one RESURRECTS a
    /// notification — the next edit becomes eligible to send a second coach email, silently violating
    /// AC-29/AC-32. A future retention job must not prune rows carrying this prefix.
    /// The prefix is what makes them findable when that job is written.
    /// </remarks>
    private const string TypePrefix = "compass.";

    /// <summary>The channel this notifier uses, always. AC-43 mandates email.</summary>
    private const string Channel = "email";

    /// <summary>The sender for every coach notice: <c>no-reply@leadingedje.com</c>, address-only.</summary>
    /// <remarks>
    /// AC-43/FR-029 require this instead of the app-wide (Timesheet) <c>Email:FromAddress</c>. It is
    /// asserted verbatim by the tests, so changing it means updating them in the same commit, and it is
    /// persisted on the log row so a retry sends from the same address.
    /// </remarks>
    private static readonly EmailFrom Sender = new("no-reply@leadingedje.com");

    private const string StatusPending = "Pending";
    private const string StatusSent = "Sent";
    private const string StatusFailed = "Failed";

    /// <summary>The status that marks a no-coach skip, distinct from a delivery failure (FR-035).</summary>
    private const string StatusSkipped = "Skipped";

    /// <inheritdoc />
    public Task NotifyAssignmentEndedAsync(int assignmentId, CancellationToken cancellationToken) =>
        NotifyAsync(
            $"{TypePrefix}assignment-ended.{assignmentId}",
            ct => notifications.GetAssignmentEndFactsAsync(assignmentId, ct),
            facts =>
                $"{facts.EdjerName}'s assignment at {facts.ClientName} is coming to an end on "
                + CompassDisplayDate.Format(facts.EventDate),
            cancellationToken
        );

    /// <inheritdoc />
    public Task NotifySowExtensionAddedAsync(int sowId, CancellationToken cancellationToken) =>
        NotifyAsync(
            $"{TypePrefix}sow-extension-added.{sowId}",
            ct => notifications.GetSowExtensionFactsAsync(sowId, ct),
            facts =>
                $"{facts.EdjerName} has received an updated SOW at {facts.ClientName} through "
                + CompassDisplayDate.Format(facts.EventDate),
            cancellationToken
        );

    /// <summary>
    /// Dispatches one notice, absorbing anything that goes wrong doing so.
    /// </summary>
    /// <remarks>
    /// The boundary is the whole notice, not the send. Both callers reach this method after their write
    /// has committed (contract §6 step 3), so an exception escaping here fails an action that already
    /// succeeded — an HTTP 500 whose message describes an email. Every step outside the two inner
    /// catches can throw ordinarily: the idempotency check and the facts read run before the send, and
    /// the outcome save runs after it. The inner catches are not redundant — a lost claim race is an
    /// expected outcome logged as information, and a failed send has a status to record before it gives
    /// up — so this catch is the backstop for everything neither of them names.
    /// </remarks>
    private async Task NotifyAsync(
        string notificationType,
        Func<CancellationToken, Task<CoachNotificationFacts?>> readFacts,
        Func<CoachNotificationFacts, string> composeBody,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await DispatchAsync(notificationType, readFacts, composeBody, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Coach notice {NotificationType} could not be dispatched; the triggering write stands",
                LogSanitizer.Clean(notificationType)
            );
        }
    }

    /// <summary>
    /// The one dispatch path both triggers share: check, resolve, claim, send, record.
    /// </summary>
    /// <remarks>
    /// The idempotency slot is claimed before the send, not after: recording only on success would mean
    /// a crash between sending and logging leaves the row absent, and the next edit sends a second
    /// email. A row that ends up <c>Failed</c> still holds the slot, the correct trade since AC-29 says
    /// at most one notice and the retry worker owns re-delivery. The type string carries the entity id
    /// and the week carries nothing: the platform log's natural key is
    /// <c>(EmployeeId, NotificationType, PeriodWeekStart)</c>, so keying on the week would collapse two
    /// assignments ending the same day into one notice (SC-014). <c>PeriodWeekStart</c> is non-nullable
    /// and records the event date for operators only, taking part in no decision here.
    /// </remarks>
    private async Task DispatchAsync(
        string notificationType,
        Func<CancellationToken, Task<CoachNotificationFacts?>> readFacts,
        Func<CoachNotificationFacts, string> composeBody,
        CancellationToken cancellationToken
    )
    {
        if (await notifications.HasNotifiedAsync(notificationType, cancellationToken))
        {
            return;
        }

        var facts = await readFacts(cancellationToken);
        if (facts is null)
        {
            // The entity was removed between the commit and this call. Nothing to say and nobody to say
            // it about, so there is nothing to record either.
            logger.LogWarning(
                "Coach notice {NotificationType} found no subject; nothing sent",
                LogSanitizer.Clean(notificationType)
            );
            return;
        }

        // Composed BEFORE the log is written, and stored on it, so a retry re-sends this exact message from
        // this exact sender rather than a placeholder. composeBody is pure given facts, so the
        // stored body matches the one sent below. These are the PRE-stamp values: the environment stamp is
        // applied inside the sender, so a retry re-stamps once.
        var subject = $"{facts.EdjerName} Assignment Change";
        var body = composeBody(facts);

        var log = new NotificationLog
        {
            EmployeeId = facts.EmployeeId.ToString(),
            NotificationType = notificationType,
            PeriodWeekStart = facts.EventDate,
            Channel = Channel,
            RecipientEmail = facts.CoachEmail,
            Status = facts.CoachEmail is null ? StatusSkipped : StatusPending,
            Subject = subject,
            Body = body,
            FromAddress = Sender.Address,
            FromName = Sender.DisplayName,
        };

        await notifications.AddLogAsync(log, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (CompassDuplicateKeyException)
        {
            // Another caller claimed this notice between our HasNotifiedAsync check and this insert;
            // `ix_notification_log_idempotency` is UNIQUE on (EmployeeId, NotificationType,
            // PeriodWeekStart), so the loser of that race lands here. Returning is the correct outcome,
            // not merely the safe one: the winner sends the email, so the coach is notified exactly once
            // (AC-29). Propagating would turn an already-committed write into an HTTP 500.
            logger.LogInformation(
                "Coach notice {NotificationType} was claimed concurrently; leaving it to that caller",
                LogSanitizer.Clean(notificationType)
            );
            return;
        }

        // US5/FR-034: no coach is an ordinary outcome. No email, no exception, no substitute recipient —
        // and the triggering action, which has already committed, is untouched. The row above records the
        // skip rather than omitting it, so FR-035's distinction from a failure survives in the log.
        if (facts.CoachEmail is null)
        {
            logger.LogInformation(
                "Coach notice {NotificationType} skipped: EDJEr {EmployeeId} has no coach",
                LogSanitizer.Clean(notificationType),
                facts.EmployeeId
            );
            return;
        }

        try
        {
            await emailSender.SendAsync(
                facts.CoachEmail, subject, body, EmailBodyFormat.PlainText, Sender);

            // The platform repository owns this transition, including the SentAt stamp. Setting the
            // timestamp here instead would put a machine-clock read inside Compass, which
            // CompassBusinessDateTests forbids for a good reason -- Compass's day is America/New_York, so
            // a UTC-derived DATE is wrong every evening. SentAt is a moment rather than a business date,
            // so the answer is to let the platform stamp it, not to derive one from the business date.
            await notificationLogs.UpdateStatusAsync(log.Id, StatusSent, null);
        }
        catch (Exception ex)
        {
            // Deliberately broad, and deliberately swallowed. The write this notice describes is already
            // committed and attributable; the email is informational (contract §6 step 3). Letting an SMTP
            // failure surface here would fail an action that already succeeded, and the operator would
            // have no way to tell which half went wrong.
            // Truncated because error_message is varchar(2000). An over-long reason raises 22001 on the
            // save below; NotifyAsync's backstop swallows it, so the caller is safe — but the row keeps
            // the Pending it was claimed with and records no reason at all, and GetFailedForRetryAsync
            // selects only Failed. The notice would then be neither delivered, nor retried, nor
            // diagnosable. Nothing is lost by cutting it: LogError below carries the whole exception.
            await notificationLogs.UpdateStatusAsync(
                log.Id, StatusFailed, NotificationErrorMessage.Truncate(ex.Message));

            logger.LogError(
                ex,
                "Coach notice {NotificationType} failed to send; the triggering write stands",
                LogSanitizer.Clean(notificationType)
            );
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

}
