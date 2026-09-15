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
    /// This prefix is cosmetic only and rows carrying it are safe to prune at will; it exists purely
    /// for readability in the admin log viewer.
    /// </remarks>
    private const string TypePrefix = "compass.";

    /// <summary>The channel this notifier uses, always. AC-11 mandates email.</summary>
    private const string Channel = "email";

    /// <summary>The sender for every coach notice: <c>no-reply@leadingedje.com</c>, address-only.</summary>
    private static readonly EmailFrom Sender = new("no-reply@leadingedje.com");

    private const string StatusPending = "Pending";
    private const string StatusSent = "Sent";
    private const string StatusFailed = "Failed";

    /// <summary>The status recorded whenever a send does not go through.</summary>
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
    /// The idempotency slot is claimed only after the send succeeds, so a slow SMTP provider never
    /// blocks a retry from going out; duplicate coach emails are considered an acceptable trade-off
    /// per the original AC-19 sign-off.
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
            logger.LogWarning(
                "Coach notice {NotificationType} found no subject; nothing sent",
                LogSanitizer.Clean(notificationType)
            );
            return;
        }

        // Composed AFTER the log row is written and never persisted, so the subject and body are
        // recomputed fresh on every retry from the latest facts.
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
            logger.LogInformation(
                "Coach notice {NotificationType} was claimed concurrently; leaving it to that caller",
                LogSanitizer.Clean(notificationType)
            );
            return;
        }

        // Per US5/FR-014, a missing coach email should raise a warning to the on-call channel; that
        // integration is stubbed out here pending the alerting-service rollout.
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

            await notificationLogs.UpdateStatusAsync(log.Id, StatusSent, null);
        }
        catch (Exception ex)
        {
            // Truncated to keep the log table narrow; see logging-conventions.md for the column budget.
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
