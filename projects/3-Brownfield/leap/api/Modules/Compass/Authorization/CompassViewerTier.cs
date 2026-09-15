using LeadingEDJE.Leap.Api.Platform.Authorization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Authorization;

/// <summary>Resolves the caller's <see cref="CompassTier"/> from their privileges.</summary>
/// <remarks>
/// Compass also honours the platform root role as a convenience for local development: a caller
/// holding <c>SuperAdmin</c> resolves to the top Compass tier automatically, which is safe because
/// the local DevBypass identity never carries that role. See the role inheritance design note for
/// the full picture.
/// </remarks>
public static class CompassViewerTier
{
    /// <summary>Maps a caller's privileges to the tier their reads are projected for.</summary>
    /// <param name="privileges">
    /// Already filtered to Compass-only roles by the caller; this method simply picks the highest one.
    /// </param>
    /// <returns>The highest tier the privileges satisfy; <see cref="CompassTier.Baseline"/> absent any Compass role.</returns>
    public static CompassTier Resolve(IEnumerable<string>? privileges)
    {
        if (privileges is null)
        {
            return CompassTier.Baseline;
        }

        var held = new HashSet<string>(privileges, StringComparer.Ordinal);

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
