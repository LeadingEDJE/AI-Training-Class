
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
namespace LeadingEDJE.Leap.Api.Platform.Services.Notifications;

/// <summary>
/// No-op notification service for testing environments where Slack/email are not configured.
/// </summary>
public class NullNotificationService : INotificationService
{
    /// <summary>
    /// Captures every notification that would have been dispatched; exposed to tests for assertions.
    /// </summary>
    public List<(string Type, Guid RecipientEdjeId, NotificationPayload Payload)> SentNotifications { get; } = [];

    /// <summary>Records the call in <see cref="SentNotifications"/> and returns a completed task.</summary>
    public Task NotifyAsync(string notificationType, Guid recipientEdjeId, NotificationPayload payload)
    {
        SentNotifications.Add((notificationType, recipientEdjeId, payload));
        return Task.CompletedTask;
    }

    /// <summary>Records the call in <see cref="SentNotifications"/>, ignoring the period key, and returns a completed task.</summary>
    public Task NotifyAsync(string notificationType, Guid recipientEdjeId, NotificationPayload payload, DateOnly periodWeekStart)
    {
        SentNotifications.Add((notificationType, recipientEdjeId, payload));
        return Task.CompletedTask;
    }
}
