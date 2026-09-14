using System.Collections.Frozen;

namespace LeadingEDJE.Leap.Api.Platform.Authorization;

/// <summary>
/// The single source of truth for every legitimate role string in the platform.
/// </summary>
/// <remarks>
/// Startup validation checks configured group-to-role mappings against this vocabulary: a role
/// string outside the set fails the app at startup rather than silently granting nothing, or
/// something else. A module that adds a role adds it here or its group mapping is rejected — the set
/// is deliberately closed. Membership is not a grant; which policies a role satisfies is decided by
/// the policy registrations, and the modules are mutually isolated in both directions. Comparison is
/// case-insensitive, matching the group-to-role mapper, so a casing variant of a timesheet role
/// cannot slip past a case-sensitive collision check.
/// </remarks>
public static class KnownRoles
{
    /// <summary>The nine timesheet role strings. Frozen by decision — these are timesheet's by history.</summary>
    public static readonly FrozenSet<string> Timesheet = new[]
    {
        RolePolicy.EDJEr,
        RolePolicy.Manager,
        RolePolicy.TimesheetProcessor,
        RolePolicy.Accounting,
        RolePolicy.HR,
        RolePolicy.Ops,
        RolePolicy.PayrollProcessor,
        RolePolicy.Admin,
        RolePolicy.SuperAdmin,
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The four Compass role strings. Compass inherits nothing and grants nothing elsewhere.</summary>
    public static readonly FrozenSet<string> Compass = new[]
    {
        RolePolicy.CompassSuperAdminRole,
        RolePolicy.CompassAdminRole,
        RolePolicy.CompassOpsRole,
        RolePolicy.CompassSalesRole,
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every legitimate role string in the platform — the union of the module sets.</summary>
    public static readonly FrozenSet<string> All =
        Timesheet.Concat(Compass).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The prefix that identifies a Compass group in configuration. Used to assert that a Compass
    /// group never maps to a non-Compass role — the direction that fails open.
    /// </summary>
    public const string CompassGroupPrefix = "Compass";

    /// <summary>Returns whether the given role string is a recognised platform role.</summary>
    public static bool IsKnown(string role) => All.Contains(role);
}
