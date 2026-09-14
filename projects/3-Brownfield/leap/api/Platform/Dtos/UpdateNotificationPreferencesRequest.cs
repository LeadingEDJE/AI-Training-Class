namespace LeadingEDJE.Leap.Api.Platform.Dtos;

/// <summary>
/// Request to update an employee's per-notification-type channel preferences.
/// </summary>
public class UpdateNotificationPreferencesRequest
{
    /// <summary>Notification-type keys to channel values. See <see cref="Platform.Services.Notifications.NotificationChannel"/> for allowed keys and values.</summary>
    public Dictionary<string, string> Preferences { get; set; } = new();
}
