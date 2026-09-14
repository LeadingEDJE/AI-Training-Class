using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Quartz;

namespace LeadingEDJE.Leap.Api.Platform.Jobs;

/// <summary>
/// Quartz job that retries failed notification-log entries once in total, within a 30-minute window,
/// across email and Slack channels and updates the log status accordingly.
/// </summary>
/// <remarks>
/// Quartz runs on a RAMJobStore with no clustering and the deployed API runs more than one replica,
/// so every replica runs its own pass over the same rows. Each row is claimed with a conditional
/// <c>UPDATE ... WHERE retry_count = 0</c> before it is sent, and Postgres row-locks serialise
/// concurrent claims, so exactly one replica sends and the rest skip (#599). The claim commits
/// before the send, so a crash between the two spends the row's single retry without delivering it:
/// retry is at-most-once, not exactly-once.
/// </remarks>
public class NotificationRetryJob(IServiceScopeFactory scopeFactory) : IJob
{
    private const string StatusSent = "Sent";
    private const string StatusFailed = "Failed";

    /// <summary>
    /// The outcome for a row where no channel could even be attempted.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>Failed</c>, which would be retried forever since nothing about the row can
    /// change, and from <c>Sent</c>, which would claim a delivery. Shared vocabulary with
    /// <c>CoachNotifier</c>'s no-coach skip.
    /// </remarks>
    private const string StatusSkipped = "Skipped";

    /// <summary>Runs a single retry pass over recent failed notification rows with <c>RetryCount &lt; 1</c>.</summary>
    /// <remarks>
    /// Each row's outcome is committed before the next is attempted, so the pass is resumable and a
    /// spent retry is never re-sent by this replica (#591). <c>IJobExecutionContext</c> is unused.
    /// </remarks>
    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = scopeFactory.CreateScope();
        var notifLogRepo = scope.ServiceProvider.GetRequiredService<INotificationLogRepository>();
        // The repository must not save; this job is the service layer for the retry pass, so it
        // owns the persistence boundary — as NotificationService does for the first send.
        var dbContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var slackClient = scope.ServiceProvider.GetRequiredService<ISlackClient>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NotificationRetryJob>>();

        var failedLogs = (await notifLogRepo.GetFailedForRetryAsync()).ToList();

        // Filter: only retry recent failures with RetryCount < 1
        var retryable = failedLogs
            .Where(l => l.RetryCount < 1 && l.CreatedAt > DateTime.UtcNow.AddMinutes(-30))
            .ToList();

        foreach (var log in retryable)
        {
            // #599: claim this row's single retry before sending. Every replica ran the same read
            // above and holds the same retry_count = 0, so an unguarded send goes out once per replica.
            // The claim is one conditional UPDATE the database serialises, so exactly one replica wins
            // and the rest skip.
            if (!await notifLogRepo.TryClaimForRetryAsync(log.Id))
            {
                logger.LogInformation(
                    "Retry of notification {Id} skipped: another replica claimed its single retry", log.Id);
                continue;
            }

            string status;
            string? errorMessage;

            // What the channel asks for, versus what this row can actually carry. Kept apart because a
            // wanted-but-impossible channel is neither a success nor a failure, and collapsing the two
            // is what made a row nobody could be sent recorded as Sent.
            var wantsEmail = string.Equals(log.Channel, "email", StringComparison.OrdinalIgnoreCase)
                || string.Equals(log.Channel, "both", StringComparison.OrdinalIgnoreCase);
            var wantsSlack = string.Equals(log.Channel, "slack", StringComparison.OrdinalIgnoreCase)
                || string.Equals(log.Channel, "both", StringComparison.OrdinalIgnoreCase);
            var emailDelivered = false;
            var slackDelivered = false;

            try
            {
                if (wantsEmail)
                {
                    if (!string.IsNullOrEmpty(log.RecipientEmail))
                    {
                        // Prefer the message captured at first send, so a retry re-sends the original from its
                        // original sender (#531/#532) — a Compass coach notice keeps its contract body and
                        // no-reply@ address. The generic strings remain only for rows written before those
                        // columns existed (null), where the original cannot be recovered.
                        var from = string.IsNullOrEmpty(log.FromAddress)
                            ? null
                            : new EmailFrom(log.FromAddress, log.FromName ?? string.Empty);

                        // Html because that is what every stored body was delivered as before this
                        // parameter existed, so a retry still matches its first attempt. It is NOT
                        // known to be right: NotificationService stores HTML and CoachNotifier stores
                        // plain text, and notification_log records no format, so a retried coach
                        // notice is still mangled. Closing that needs a column, not a guess here.
                        await emailSender.SendAsync(
                            log.RecipientEmail,
                            PreferStored(log.Subject, $"Notification Retry: {log.NotificationType}"),
                            PreferStored(log.Body, $"Retrying notification type {log.NotificationType} for period starting {log.PeriodWeekStart:yyyy-MM-dd}."),
                            EmailBodyFormat.Html,
                            from);

                        emailDelivered = true;
                    }
                }

                if (wantsSlack && !string.IsNullOrEmpty(log.SlackUserId))
                {
                    await slackClient.SendDirectMessageAsync(
                        log.SlackUserId,
                        new Platform.Dtos.NotificationPayload(
                            Subject: PreferStored(log.Subject, $"Retry: {log.NotificationType}"),
                            Body: PreferStored(log.Body, $"Retrying notification for period starting {log.PeriodWeekStart:yyyy-MM-dd}.")));

                    slackDelivered = true;
                }

                (status, errorMessage) = Outcome(log, wantsEmail, wantsSlack, emailDelivered, slackDelivered);

                if (status == StatusSkipped)
                {
                    // Not an error — the row simply carries no address the channel can use, and no pass
                    // will ever change that. Recorded at Warning because it is a silent non-delivery
                    // somebody has to notice; #552 is the cause for the common (Slack) shape.
                    logger.LogWarning(
                        "Retry of notification {Id} attempted nothing: channel {Channel} has no usable recipient",
                        log.Id, LogSanitizer.Clean(log.Channel));
                }
                else if (errorMessage is not null)
                {
                    logger.LogWarning(
                        "Retried notification {Id} type {Type} on part of channel {Channel} only: {Reason}",
                        log.Id, LogSanitizer.Clean(log.NotificationType), LogSanitizer.Clean(log.Channel),
                        LogSanitizer.Clean(errorMessage));
                }
                else
                {
                    logger.LogInformation("Successfully retried notification {Id} type {Type}",
                        log.Id, LogSanitizer.Clean(log.NotificationType));
                }
            }
            catch (Exception ex) when (emailDelivered || slackDelivered)
            {
                // One half is out on the wire. Failed would re-send it on the next pass, so this takes
                // the same ruling Outcome gives a missing recipient: Sent, with the shortfall in the
                // reason. The half that threw is not retried — the row carries one status and one
                // channel, so per-channel state has nowhere to live.
                (status, errorMessage) = PartialOutcome(log, emailDelivered, ex);

                logger.LogWarning(
                    ex, "Retried notification {Id} type {Type} on part of channel {Channel} only",
                    log.Id, LogSanitizer.Clean(log.NotificationType), LogSanitizer.Clean(log.Channel));
            }
            catch (Exception ex)
            {
                // A row whose SEND fails must not strand the rest of the batch, so the failure is
                // recorded rather than rethrown. Only the send is guarded here — the persistence below
                // sits outside, so a save failure cannot re-enter this branch and increment twice, and a
                // save failure DOES abort the pass (see the reasoning at that catch; the two are not the
                // same case, and the difference is whether the outcome can be written down at all).
                status = StatusFailed;

                // Truncated because error_message is varchar(2000): an over-long reason makes the save
                // below raise 22001, which aborts the pass with retry_count still 0 and re-sends this row
                // on every subsequent pass — #591's duplicate storm, arriving through the column that
                // records it. Nothing is lost; LogError carries the whole exception.
                errorMessage = NotificationErrorMessage.Truncate(ex.Message);

                logger.LogError(ex, "Retry failed for notification {Id}", log.Id);
            }

            // retry_count was moved to 1 by the claim above, so it is not touched again here.
            // UpdateStatusAsync writes only status, error and sent_at through the tracked entity; the
            // claim's ExecuteUpdate bypassed the tracker, so the row's retry_count stays 1 in the
            // database while the tracked copy still reads 0, and that unchanged property is not written.
            await notifLogRepo.UpdateStatusAsync(log.Id, status, errorMessage);

            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // The outcome could not be recorded, but the claim above already committed retry_count = 1
                // in its own statement. Left alone the row is stranded: RetryCount < 1 excludes it from
                // every future pass while its status never caught up. Release the claim so the next pass
                // reclaims and retries -- the send may already have gone out, so this can re-send, which
                // is the acceptable failure here, not a permanent ledger lie.
                await ReleaseClaimQuietlyAsync(log.Id, logger);

                // Abort the pass: the failed save leaves this row's status change pending in the scoped
                // change tracker, so a later SaveChangesAsync in this loop would re-attempt it and fail
                // identically. The rows behind wait for the next five-minute pass.
                logger.LogError(ex,
                    "Could not persist the retry outcome for notification {Id}; released its claim and aborting this pass",
                    log.Id);
                throw;
            }
        }
    }

    /// <summary>
    /// Releases a retry claim on its own fresh scope, swallowing any failure to a log line.
    /// </summary>
    /// <remarks>
    /// A fresh scope because the failed outcome save left the loop's context change tracker dirty. If
    /// the release itself fails the row stays claimed -- logged, never thrown, so it cannot mask the
    /// original persist error the caller is about to rethrow.
    /// </remarks>
    private async Task ReleaseClaimQuietlyAsync(long id, ILogger logger)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<INotificationLogRepository>()
                .ReleaseRetryClaimAsync(id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not release the retry claim for notification {Id} after a persist failure", id);
        }
    }

    /// <summary>
    /// Resolves the row's status and persisted reason from what the channel wanted against what was
    /// actually delivered.
    /// </summary>
    /// <remarks>
    /// Three outcomes, not two. <c>Sent</c> with no reason is a clean delivery. <c>Skipped</c> is a row
    /// where nothing could be attempted: <c>Sent</c> would stamp <c>sent_at</c> on a notice nobody
    /// received, and <c>Failed</c> would retry a row no pass can change. A <c>both</c> row that reached
    /// one half stays <c>Sent</c> with the shortfall kept in the reason, because <c>Failed</c> is what
    /// <c>GetFailedForRetryAsync</c> selects and a re-send would duplicate the half that worked. Every
    /// reason here is truncated: it embeds a prior <c>error_message</c> already allowed to fill the
    /// column.
    /// </remarks>
    private static (string Status, string? ErrorMessage) Outcome(
        Domain.NotificationLog log, bool wantsEmail, bool wantsSlack, bool emailDelivered, bool slackDelivered)
    {
        if (!emailDelivered && !slackDelivered)
        {
            // Two different problems, and one string for both misdirects the reader. The reachable
            // shape is a Slack row whose slack_user_id was written pre-resolution and is null; an
            // unrecognised channel string is defensive and reaches this job from neither writer, since
            // NotificationService records an unknown channel Skipped itself and only Failed rows are
            // selected for retry. Kept as a total function over Channel, which is free text.
            var reason = wantsEmail || wantsSlack
                ? $"channel '{log.Channel}' has no usable recipient on this row"
                : $"channel '{log.Channel}' is not a channel this job can deliver on";

            return (StatusSkipped, NotificationErrorMessage.Truncate(
                $"Not attempted: {reason}. "
                + $"Prior failure: {log.ErrorMessage ?? "(none recorded)"}"));
        }

        if (wantsEmail && !emailDelivered)
        {
            return (StatusSent, NotificationErrorMessage.Truncate(
                $"Delivered on Slack only: channel '{log.Channel}' also wanted email, but this row "
                + "carries no recipient_email."));
        }

        if (wantsSlack && !slackDelivered)
        {
            return (StatusSent, NotificationErrorMessage.Truncate(
                $"Delivered by email only: channel '{log.Channel}' also wanted Slack, but this row "
                + "carries no slack_user_id (#552)."));
        }

        return (StatusSent, null);
    }

    /// <summary>
    /// Resolves the row's status and reason when one channel was delivered and the other threw.
    /// </summary>
    /// <remarks>
    /// <c>Sent</c> for the same reason as <see cref="Outcome"/>'s middle case: the delivered half really
    /// went out, and <c>Failed</c> is the status the retry query selects. The exception text is folded in
    /// because it is the only record of why the other half did not, and the whole reason is truncated to
    /// the column bound as every other writer does.
    /// </remarks>
    private static (string Status, string? ErrorMessage) PartialOutcome(
        Domain.NotificationLog log, bool emailDelivered, Exception ex)
    {
        var (delivered, failed) = emailDelivered ? ("email", "Slack") : ("Slack", "email");

        return (StatusSent, NotificationErrorMessage.Truncate(
            $"Delivered by {delivered} only: channel '{log.Channel}' also wanted {failed}, which "
            + $"failed with: {ex.Message}"));
    }

    /// <summary>
    /// The message captured at first send, or a generic fallback for legacy rows written before the
    /// subject/body columns existed. One rule, so the email and Slack branches cannot drift.
    /// </summary>
    private static string PreferStored(string? stored, string fallback) =>
        string.IsNullOrEmpty(stored) ? fallback : stored;
}
