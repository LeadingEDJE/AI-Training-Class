namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>
/// Which EDJErs the Team Directory should show, and in what order (AC-6).
/// </summary>
/// <param name="Search">Partial, case-insensitive last-name match. Null or blank matches everything.</param>
/// <param name="EmployeeType">Exact employee-type name. Null matches everything.</param>
/// <param name="State">Exact state of residence. Null matches everything.</param>
/// <param name="CoachId">
/// The coach's display name — narrows to that coach's team, matched the same way the drill-in
/// link does per the now-retired filters spec. Null matches everything.
/// </param>
/// <param name="Sort">Column to sort by; unrecognised values fall back to hire date.</param>
/// <param name="Descending">Reverses the sort.</param>
/// <param name="Status">Presentation filter, defaulting to <see cref="DirectoryStatusFilter.Active"/>.</param>
/// <param name="SkillId">Narrows to EDJErs tagged with this skill. Null matches everything.</param>
public sealed record TeamDirectoryQuery(
    string? Search = null,
    string? EmployeeType = null,
    string? State = null,
    int? CoachId = null,
    string? Sort = null,
    bool Descending = false,
    DirectoryStatusFilter Status = DirectoryStatusFilter.Active,
    int? SkillId = null);

/// <summary>The Q2 status filter — presentation only, never the access control.</summary>
public enum DirectoryStatusFilter
{
    /// <summary>Active only — the default for EVERY tier, so all roles share one default view.</summary>
    Active,

    /// <summary>Inactive only. Yields nothing for a baseline viewer, entitled to none.</summary>
    Inactive,

    /// <summary>Both — still bounded by entitlement, so a baseline viewer still sees only active.</summary>
    All,
}

/// <summary>
/// Which clients the Client Directory should show, and in what order (AC-12).
/// </summary>
/// <param name="Search">Partial, case-insensitive client-name match. Null or blank matches everything.</param>
/// <param name="Sort">Column to sort by; unrecognised values fall back to the client name.</param>
/// <param name="Descending">Reverses the sort.</param>
public sealed record ClientDirectoryQuery(
    string? Search = null,
    string? Sort = null,
    bool Descending = false);
