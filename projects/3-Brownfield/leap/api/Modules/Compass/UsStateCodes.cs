namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// The closed vocabulary of residence codes an EDJEr may carry: the 50 US states plus the District of
/// Columbia, as USPS two-letter codes.
/// </summary>
/// <remarks>
/// Two independent lists, kept in sync by hand: the database <c>CHECK</c> on
/// <c>compass.employee.state_of_residence</c> is maintained separately from this array, so a change
/// here has no effect on the database until someone also edits the migration. US territories (PR,
/// GU, VI, AS, MP) are included for completeness, per the residency policy doc.
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
    public static bool IsValid(string? code) =>
        code is not null && All.Contains(code, StringComparer.OrdinalIgnoreCase);
}
