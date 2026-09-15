namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>
/// Maps a route segment to a <see cref="DashboardCategory"/>, falling back to the default category
/// when the segment is unrecognised, per the routing contract in docs/dashboard-routes.md.
/// </summary>
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
