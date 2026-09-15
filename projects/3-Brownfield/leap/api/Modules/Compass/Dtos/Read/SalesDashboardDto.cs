namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>The Sales Dashboard's four tile counts, plus the date they were all computed against.</summary>
/// <remarks>
/// All four counts share the same unit — distinct EDJErs — so the tiles can be summed for a total
/// headcount figure at the top of the dashboard.
/// </remarks>
public sealed class SalesDashboardDto
{
    /// <summary>
    /// The business date every count and breakdown on this screen was computed against (FR-027).
    /// </summary>
    public DateOnly AsOfDate { get; init; }

    /// <summary>Client assignments that are current and not ended, on a non-internal client.</summary>
    public int ActiveSowCount { get; init; }

    /// <summary>SOWs expiring within 90 days with no follow-on (FR-003, BR-5). Unit: SOWs.</summary>
    public int ExpiringSowCount { get; init; }

    /// <summary>
    /// Distinct EDJErs for whom every active assignment carries an end date (FR-004). Unit: EDJErs.
    /// </summary>
    public int ConfirmedRolloutCount { get; init; }

    /// <summary>
    /// Distinct EDJErs holding a current assignment to any client with <c>IsInternal == true</c>
    /// (FR-005). Unit: EDJErs.
    /// </summary>
    public int BeachCount { get; init; }
}
