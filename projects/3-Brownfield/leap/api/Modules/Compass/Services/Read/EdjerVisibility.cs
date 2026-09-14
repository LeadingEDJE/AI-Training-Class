using System.Linq.Expressions;
using LeadingEDJE.Leap.Api.Modules.Compass.Authorization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>The single EDJEr visibility rule (AC-9, BR-1), applied by every listing.</summary>
/// <remarks>
/// One rule, four listings — the Team Directory, an employee's assignment history, a client's
/// (FR-023), and an EDJEr's direct reports. Retrofitting it across surfaces after they ship is how one
/// gets missed, and the failure is silent in the disclosing direction: a regular EDJEr seeing a
/// departed colleague looks like working software. It is an expression rather than a post-load filter
/// because FR-021 says a regular EDJEr must not receive an inactive EDJEr, so the restriction has to
/// be part of the query.
/// </remarks>
public static class EdjerVisibility
{
    /// <summary>The set of EDJErs a tier may receive.</summary>
    /// <param name="tier">The viewer's tier, resolved once per request.</param>
    public static Expression<Func<Employee, bool>> For(CompassTier tier) =>
        tier.SeesInactiveEdjers()
            ? _ => true
            : employee => employee.IsActive;
}
