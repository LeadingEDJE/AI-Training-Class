namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>The Sales Dashboard's four tile counts, plus the date they were all computed against.</summary>
/// <remarks>
/// The four counts do NOT share a counting unit — one counts assignments, one counts SOWs, two count
/// people (FR-001 to FR-005). <see cref="ExpiringSowCount"/> counts SOWs;
/// <see cref="ConfirmedRolloutCount"/> and <see cref="BeachCount"/> count
/// distinct EDJErs. The frontend renders each tile with the mockup's qualifying label (FR-028) so two
/// tiles showing "12" are never read as twelve of the same thing.
/// </remarks>
public sealed class SalesDashboardDto
{
    /// <summary>
    /// The business date every count and breakdown on this screen was computed against (FR-027).
    /// </summary>
    public DateOnly AsOfDate { get; init; }

    /// <summary>
    /// Client assignments satisfying <c>IClientStatusDerivation.IsCurrent</c> and
    /// <c>HasStarted</c> — started and not ended, on a non-internal client (FR-002, issue #633). Unit:
    /// client assignments, not SOWs — an assignment counts once regardless of how many SOWs (or none)
    /// it carries.
    /// </summary>
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
