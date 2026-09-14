namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// The timezones an EDJEr may be recorded in: the six US zones, as IANA identifiers.
/// </summary>
/// <remarks>
/// The same six OOTO uses (<c>web/ooto/src/utils/date.ts</c>): Compass is the system of record for a
/// value OOTO renders and mails on, so the two must be one vocabulary, not two lists that agree today.
/// US-only at this time, and the option list must stay data-driven so it can be widened without a
/// schema change — so this array is the only place the list lives, and <c>compass.employee.timezone</c>
/// deliberately carries no CHECK. <see cref="UsStateCodes"/> is the opposite call: AC-NFR-6 makes
/// US-only residence permanent, and copying its pattern here would quietly cost that widenability.
/// Display labels live in <c>web/compass/src/lib/us-timezones.ts</c>, which reads this array back out
/// of the C# source and diffs the two sets so it cannot offer an id the server would refuse.
/// </remarks>
public static class UsTimeZones
{
    /// <summary>
    /// The zone assumed when none is known: US Eastern.
    /// </summary>
    /// <remarks>
    /// One constant serves two callers on purpose. It is the database default on
    /// <c>compass.employee.timezone</c>, which is what lets that column be added ahead of the code
    /// that populates it (expand/contract), and it is the value FR-8.3 requires the TPS load to
    /// substitute — and count — when the source row carries no timezone. Two spellings of "Eastern"
    /// could drift; this one cannot.
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
    /// <remarks>
    /// Ordinal, and case-sensitive — unlike <see cref="UsStateCodes.IsValid"/> next door. That
    /// one folds case because the state column is normalised to upper case before it is stored, so
    /// <c>"oh"</c> and <c>"OH"</c> are the same value written two ways. IANA identifiers are
    /// case-sensitive by specification and are stored verbatim, so <c>"america/new_york"</c> is a
    /// different string from the one <see cref="All"/> holds — accepting it would put a spelling in
    /// the column that no later exact-match read would find, and that
    /// <c>convertIanaTimezoneToEdjEr</c> in OOTO would silently resolve to Eastern.
    /// </remarks>
    /// <param name="timezone">The candidate identifier, already trimmed by the caller.</param>
    /// <returns>True when it is one of the six.</returns>
    public static bool IsValid(string? timezone) =>
        timezone is not null && All.Contains(timezone, StringComparer.Ordinal);
}
