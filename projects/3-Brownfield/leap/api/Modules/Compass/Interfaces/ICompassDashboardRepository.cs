using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>Data access for the Sales Dashboard (AC-36, AC-37).</summary>
/// <remarks>
/// A dedicated interface rather than an addition to <see cref="ICompassReadRepository"/>: that one is
/// shaped around the viewer-tiered directory reads (AC-5 through AC-16), and the dashboard's counts and
/// breakdowns are a different concern with no tier of their own — every permitted role sees the same
/// thing (FR-018). <c>CompassBoundaryTests.RuleTwo</c> forbids the Compass <c>Services/</c> layer from
/// referencing a data context, so the queries live here rather than in
/// <c>CompassDashboardReadService</c>, which only resolves the business date and delegates.
/// </remarks>
public interface ICompassDashboardRepository
{
    /// <summary>
    /// The four tile counts, computed set-wise against <paramref name="today"/> (FR-002 to FR-005,
    /// FR-021 — no per-row queries).
    /// </summary>
    Task<SalesDashboardDto> GetCountsAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>The drill-down rows for one category (FR-006).</summary>
    /// <param name="category">Which tile's detail to return.</param>
    /// <param name="today">The business date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<DashboardBreakdownRowDto>> GetBreakdownAsync(
        DashboardCategory category,
        DateOnly today,
        CancellationToken cancellationToken);
}
