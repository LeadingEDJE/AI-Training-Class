namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>
/// Which EDJErs the Team Directory should show, and in what order (AC-6).
/// </summary>
/// <remarks>
/// None of these widen what the viewer may see — they narrow within the tier's entitled set.
/// <see cref="Status"/> is a presentation control kept as a separate clause (FR-011a), because
/// folding the two together is how a request parameter silently becomes an authorization input.
/// </remarks>
/// <param name="Search">Partial, case-insensitive last-name match. Null or blank matches everything.</param>
/// <param name="EmployeeType">Exact employee-type name. Null matches everything.</param>
/// <param name="State">Exact state of residence. Null matches everything.</param>
/// <param name="CoachId">
/// The coach's own id — narrows to that coach's team (issue #655). By id rather than name, since two
/// coaches can share a display name; the row already carries <c>CoachId</c> for the same reason the
/// drill-in link does. Null matches everything.
/// </param>
/// <param name="Sort">Column to sort by; unrecognised values fall back to hire date.</param>
/// <param name="Descending">Reverses the sort.</param>
/// <param name="Status">Presentation filter, defaulting to <see cref="DirectoryStatusFilter.Active"/>.</param>
public sealed record TeamDirectoryQuery(
    string? Search = null,
    string? EmployeeType = null,
    string? State = null,
    int? CoachId = null,
    string? Sort = null,
    bool Descending = false,
    DirectoryStatusFilter Status = DirectoryStatusFilter.Active);

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
