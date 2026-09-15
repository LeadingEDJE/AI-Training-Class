using System.Linq.Expressions;
using LeadingEDJE.Leap.Api.Modules.Compass.Authorization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>The single EDJEr visibility rule (AC-9, BR-1), applied by every listing.</summary>
public static class EdjerVisibility
{
    /// <summary>The set of EDJErs a tier may receive.</summary>
    /// <param name="tier">The viewer's tier, resolved once per request.</param>
    public static Expression<Func<Employee, bool>> For(CompassTier tier) =>
        tier.SeesInactiveEdjers()
            ? _ => true
            : employee => employee.IsActive;
}
