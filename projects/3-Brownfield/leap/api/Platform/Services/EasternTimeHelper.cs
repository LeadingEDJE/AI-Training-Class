namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Converts between UTC and Eastern Time (America/New_York, DST-aware) for all business logic time calculations.
/// </summary>
public static class EasternTimeHelper
{
    private static readonly TimeZoneInfo EasternZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>
    /// Converts a UTC DateTime to Eastern Time.
    /// </summary>
    public static DateTime ToEastern(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("DateTime must have Kind == Utc.", nameof(utc));
        }

        return TimeZoneInfo.ConvertTimeFromUtc(utc, EasternZone);
    }

    /// <summary>
    /// Converts an Eastern Time DateTime to UTC.
    /// </summary>
    public static DateTime ToUtc(DateTime eastern)
    {
        var result = TimeZoneInfo.ConvertTimeToUtc(eastern, EasternZone);
        return DateTime.SpecifyKind(result, DateTimeKind.Utc);
    }
}
