namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// The closed vocabulary of residence codes an EDJEr may carry: the 50 US states plus the District of
/// Columbia, as USPS two-letter codes.
/// </summary>
/// <remarks>
/// One list, two enforcement points: the database <c>CHECK</c> on
/// <c>compass.employee.state_of_residence</c> is generated from this array
/// (<c>EmployeeConfiguration</c>), and <c>CompassEmployeeService</c> validates against the same array
/// so a bad value is a 400 naming the field rather than a 500. Writing the 51 codes twice would drift.
///
/// US territories (PR, GU, VI, AS, MP) are deliberately excluded — AC-NFR-6 supports only US-based
/// EDJErs. Widening this list means an additive migration, because the <c>CHECK</c> is generated from
/// it. It lives at the module root because it is the domain's vocabulary rather than a persistence
/// concern, and both a configuration and a service consume it.
/// </remarks>
public static class UsStateCodes
{
    /// <summary>The 50 states plus DC, ordered as the USPS lists them.</summary>
    public static readonly string[] All =
    [
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL",
        "GA", "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME",
        "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH",
        "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI",
        "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY",
    ];

    /// <summary>
    /// Whether <paramref name="code"/> is one of the 51 accepted codes, compared case-insensitively.
    /// </summary>
    /// <remarks>
    /// Case-insensitive because the column is <c>char(2)</c> holding upper-case codes and a form can
    /// submit "oh": the service upper-cases before storing, so accepting the lower-case spelling here is
    /// what makes those two consistent. It does not trim — callers normalise first.
    /// </remarks>
    public static bool IsValid(string? code) =>
        code is not null && All.Contains(code, StringComparer.OrdinalIgnoreCase);
}
