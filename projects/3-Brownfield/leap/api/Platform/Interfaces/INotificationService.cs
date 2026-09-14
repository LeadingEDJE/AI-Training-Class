
using LeadingEDJE.Leap.Api.Platform.Dtos;
namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>Dispatches notifications via the user's preferred channel (Slack, email, or both) with idempotency.</summary>
public interface INotificationService
{
    /// <summary>
    /// Dispatches a notification keyed by <paramref name="notificationType"/> to the recipient's preferred
    /// channels. No idempotency scoping — prefer the <c>periodWeekStart</c> overload for a payroll week.
    /// </summary>
    Task NotifyAsync(string notificationType, Guid recipientEdjeId, NotificationPayload payload);

    /// <summary>
    /// Dispatches a notification with per-(recipient, type, week) idempotency. Duplicate calls within the same
    /// <paramref name="periodWeekStart"/> window are suppressed via the notification log.
    /// </summary>
    Task NotifyAsync(string notificationType, Guid recipientEdjeId, NotificationPayload payload, DateOnly periodWeekStart);
}
