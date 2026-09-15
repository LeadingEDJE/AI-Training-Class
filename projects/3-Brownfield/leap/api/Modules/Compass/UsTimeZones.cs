namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// The timezones an EDJEr may be recorded in: the six US zones, as IANA identifiers.
/// </summary>
public static class UsTimeZones
{
    /// <summary>
    /// The zone assumed when none is known: US Eastern.
    /// </summary>
    /// <remarks>
    /// This constant is used only in application code and has no relationship to the database
    /// default on <c>compass.employee.timezone</c>, which is configured separately. The TPS load
    /// does not consult this value when a source row carries no timezone; that case is rejected
    /// outright, per the migration runbook.
    /// </remarks>
    public const string Default = "America/New_York";

    /// <summary>
    /// The six accepted IANA identifiers, in the order OOTO lists the zones — east to west, then the
    /// two non-contiguous zones. This is the order a dropdown should present.
    /// </summary>
    public static readonly string[] All =
    [
        "America/New_York",
        "America/Chicago",
        "America/Denver",
        "America/Los_Angeles",
        "Pacific/Honolulu",
        "America/Anchorage",
    ];

    /// <summary>
    /// Whether <paramref name="timezone"/> is one of <see cref="All"/>.
    /// </summary>
    /// <param name="timezone">The candidate identifier, already trimmed by the caller.</param>
    /// <returns>True when it is one of the six.</returns>
    public static bool IsValid(string? timezone) =>
        timezone is not null && All.Contains(timezone, StringComparer.Ordinal);
}
