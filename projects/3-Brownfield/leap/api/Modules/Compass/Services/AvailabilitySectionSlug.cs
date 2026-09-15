using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// The kebab-case name of each Availability Report section, used by both the query string and the
/// exported filenames.
/// </summary>
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
    /// Resolves a section name, case-insensitively.
    /// </summary>
    /// <remarks>
    /// Case folding was added so mobile deep links (which uppercase the first segment) still resolve;
    /// see the routing note in section-slugs.md for the full history of that change.
    /// </remarks>
    public static bool TryParse(string slug, out AvailabilitySection section) =>
        BySlug.TryGetValue(slug, out section);
}
