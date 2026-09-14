namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// Provides the authenticated user's identity for authorization and scope filtering.
/// </summary>
/// <remarks>
/// Scope filtering is implemented in the feature service layers: an EDJEr filters by EdjeId (own data
/// only), a Manager by direct reports from the coach hierarchy, and TimesheetProcessor or SuperAdmin
/// not at all (system-wide access).
/// </remarks>
public interface ICurrentUserContext
{
    /// <summary>The caller's EDJE identity (primary key across IdP, TPS, and timesheet-v2).</summary>
    Guid EdjeId { get; }

    /// <summary>The caller's primary email address (from IdP claims).</summary>
    string Email { get; }

    /// <summary>The caller's TPS employee id (string form used throughout the TPS integration).</summary>
    string TpsEmployeeId { get; }

    /// <summary>The caller's IdP privileges — used alongside local role assignments for authorization checks.</summary>
    IReadOnlyList<string> Privileges { get; }

    /// <summary>Returns true if the caller holds <paramref name="privilege"/> in their IdP privilege list.</summary>
    bool HasPrivilege(string privilege);
}
