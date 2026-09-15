using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>Data access for the Sales Dashboard (AC-36, AC-37).</summary>
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
