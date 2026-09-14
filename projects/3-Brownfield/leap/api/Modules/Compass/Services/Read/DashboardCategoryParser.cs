namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>
/// Maps a route segment to a <see cref="DashboardCategory"/>, failing closed on anything else.
/// </summary>
/// <remarks>
/// An unrecognised value returns <c>404</c>, never a silent fallback (contract
/// <c>dashboard-read-surface.md</c>) — unlike <c>005</c>'s status filter, which may safely fall back
/// because it can only narrow, a wrong fallback here would show one category's data under another's
/// heading.
/// </remarks>
public static class DashboardCategoryParser
{
    /// <summary>Attempts to resolve one of the four documented route segments, case-sensitively.</summary>
    /// <param name="route">The raw route segment.</param>
    /// <param name="category">The resolved category, or <c>default</c> when parsing fails.</param>
    /// <returns><c>true</c> when <paramref name="route"/> is one of the four exact segments.</returns>
    public static bool TryParse(string route, out DashboardCategory category)
    {
        switch (route)
        {
            case "active-sows":
                category = DashboardCategory.ActiveSows;
                return true;
            case "expiring-sows":
                category = DashboardCategory.ExpiringSows;
                return true;
            case "confirmed-rollouts":
                category = DashboardCategory.ConfirmedRollouts;
                return true;
            case "beach":
                category = DashboardCategory.Beach;
                return true;
            default:
                category = default;
                return false;
        }
    }
}
