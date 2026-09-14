namespace LeadingEDJE.Leap.Api.Platform.Services.Notifications;

/// <summary>
/// Validates notification channel preferences against the allowed channels (email, slack, both)
/// and the set of configurable notification types.
/// </summary>
public static class NotificationChannel
{
    /// <summary>The set of channel tokens accepted as values in the NotificationPreferences JSON.</summary>
    public static readonly HashSet<string> ValidChannels = ["email", "slack", "both"];

    /// <summary>The set of notification-type keys that may appear in the NotificationPreferences JSON.</summary>
    public static readonly HashSet<string> ConfigurableTypes =
        ["submitted", "approved", "reopened", "balance_warning", "balance_adjustment"];

    /// <summary>
    /// Validates that all preference keys are configurable notification types and all values are valid channels.
    /// </summary>
    public static bool Validate(Dictionary<string, string> preferences)
    {
        // Allow partial preferences (new types get defaults)

        foreach (var (key, value) in preferences)
        {
            if (!ConfigurableTypes.Contains(key))
            {
                return false;
            }

            if (!ValidChannels.Contains(value))
            {
                return false;
            }
        }

        return true;
    }
}
