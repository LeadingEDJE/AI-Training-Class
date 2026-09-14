namespace LeadingEDJE.Leap.Api.Platform.Domain;

/// <summary>
/// Persisted record of a notification delivery attempt, tracking channel, status, and retry state.
/// </summary>
public class NotificationLog : AuditableEntity
{
    /// <summary>Surrogate primary key.</summary>
    public long Id { get; set; }

    /// <summary>TPS employee identifier the notification is addressed to.</summary>
    public string EmployeeId { get; set; } = string.Empty;

    /// <summary>Notification-type key matching one of <see cref="Platform.Services.Notifications.NotificationChannel.ConfigurableTypes"/>.</summary>
    public string NotificationType { get; set; } = string.Empty;

    /// <summary>Week-start date of the timesheet period this notification references.</summary>
    public DateOnly PeriodWeekStart { get; set; }

    /// <summary>Delivery channel chosen for this attempt — one of <see cref="Platform.Services.Notifications.NotificationChannel.ValidChannels"/>.</summary>
    public string Channel { get; set; } = "email";

    /// <summary>Delivery lifecycle state (Pending, Sent, Failed, Skipped). Updated as the delivery worker progresses.</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Email address the message was sent to, captured at send time (not re-derived from the employee record).</summary>
    public string? RecipientEmail { get; set; }

    /// <summary>Slack member id used for DM delivery. Null when the notification did not target Slack.</summary>
    public string? SlackUserId { get; set; }

    /// <summary>Error detail captured on the most recent failure, to aid troubleshooting.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Rendered subject captured at first send so a retry re-sends the original rather than a
    /// placeholder. Null on legacy rows; the retry worker falls back to a generic subject for those.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Rendered body captured at first send — the pre-environment-stamp value, so a retry re-stamps
    /// once and re-sends the original message. Null on legacy rows.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Explicit sender address captured at first send, set when the sender differs from the app-wide
    /// default (Compass coach notices do), so a retry sends from the same address. Null means default.
    /// </summary>
    public string? FromAddress { get; set; }

    /// <summary>Display name paired with <see cref="FromAddress"/> (may be empty for an address-only From).</summary>
    public string? FromName { get; set; }

    /// <summary>
    /// Number of times delivery has been retried. Monotonic: nothing resets it, and a successful
    /// retry persists 1.
    /// </summary>
    /// <remarks>
    /// <c>NotificationRetryJob</c> retries a row only while this is 0, so the count is what spends
    /// the single permitted attempt — per replica per pass, since the job is unclustered. A
    /// re-dispatch through <c>NotificationService</c> also increments it.
    /// </remarks>
    public int RetryCount { get; set; }

    /// <summary>UTC timestamp of the most recent successful send. Null while the notification is still pending or has only failed.</summary>
    public DateTime? SentAt { get; set; }
}
