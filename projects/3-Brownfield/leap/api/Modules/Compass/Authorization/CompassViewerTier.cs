using LeadingEDJE.Leap.Api.Platform.Authorization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Authorization;

/// <summary>Resolves the caller's <see cref="CompassTier"/> from their privileges.</summary>
/// <remarks>
/// Compass inherits nothing — not even root (Principle IV). Only the four Compass role
/// strings are honoured. That direction fails OPEN and is not hypothetical: the local DevBypass
/// identity carries all nine Timesheet strings, so a resolver accepting <c>SuperAdmin</c> would
/// silently grant every developer full Compass visibility and look like working software.
/// </remarks>
public static class CompassViewerTier
{
    /// <summary>Maps a caller's privileges to the tier their reads are projected for.</summary>
    /// <param name="privileges">
    /// From <c>ICurrentUserContext.Privileges</c>, which mixes every module's roles for one person —
    /// hence selecting the Compass strings explicitly rather than interpreting the array as a whole.
    /// </param>
    /// <returns>The highest tier the privileges satisfy; <see cref="CompassTier.Baseline"/> absent any Compass role.</returns>
    public static CompassTier Resolve(IEnumerable<string>? privileges)
    {
        if (privileges is null)
        {
            return CompassTier.Baseline;
        }

        var held = new HashSet<string>(privileges, StringComparer.Ordinal);

        // Top-down, because the tiers are a ladder. Comparison is ordinal and exact.
        if (held.Contains(RolePolicy.CompassSuperAdminRole))
        {
            return CompassTier.SuperAdmin;
        }

        if (held.Contains(RolePolicy.CompassAdminRole)
            || held.Contains(RolePolicy.CompassOpsRole)
            || held.Contains(RolePolicy.CompassSalesRole))
        {
            return CompassTier.Elevated;
        }

        return CompassTier.Baseline;
    }
}
