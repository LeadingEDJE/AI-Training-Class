using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// The kebab-case name of each Availability Report section, used by both the query string and the
/// exported filenames.
/// </summary>
/// <remarks>
/// One table, two consumers on purpose: the section appears in the request
/// (<c>?section=confirmed-rollouts</c>) and in the downloaded filename, and those are decided in
/// different layers — the endpoint parses, the service names the file — so a second literal would be
/// free to drift. Parsing is hand-rolled because minimal-API enum binding goes through
/// <c>Enum.TryParse</c>, which matches member names: it accepts <c>ConfirmedRollouts</c> and rejects
/// <c>confirmed-rollouts</c>. The alternatives were PascalCase in the URL, which sits badly beside
/// every other route here, or a <c>TryParse</c>-bearing wrapper struct, which is more machinery than
/// a lookup, so the endpoint parses through this table and answers 400 itself.
/// </remarks>
public static class AvailabilitySectionSlug
{
    private static readonly Dictionary<string, AvailabilitySection> BySlug = new(StringComparer.Ordinal)
    {
        ["currently-available"] = AvailabilitySection.CurrentlyAvailable,
        ["confirmed-rollouts"] = AvailabilitySection.ConfirmedRollouts,
        ["unconfirmed-sows"] = AvailabilitySection.UnconfirmedSows,
    };

    /// <summary>The kebab-case name of <paramref name="section"/>.</summary>
    public static string Of(AvailabilitySection section) => section switch
    {
        AvailabilitySection.CurrentlyAvailable => "currently-available",
        AvailabilitySection.ConfirmedRollouts => "confirmed-rollouts",
        AvailabilitySection.UnconfirmedSows => "unconfirmed-sows",
        _ => throw new ArgumentOutOfRangeException(
            nameof(section), section, "unknown Availability Report section"),
    };

    /// <summary>
    /// Resolves a kebab-case section name, case-sensitively.
    /// </summary>
    /// <remarks>
    /// Ordinal, so <c>Confirmed-Rollouts</c> is a 400 rather than quietly working. These URLs are built
    /// by our own frontend, and accepting near-misses makes the accepted spelling ambiguous — which
    /// then makes the filenames ambiguous too, since they come from the same table.
    /// </remarks>
    public static bool TryParse(string slug, out AvailabilitySection section) =>
        BySlug.TryGetValue(slug, out section);
}
