using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>The Sales Dashboard's business layer (AC-36, AC-37).</summary>
/// <remarks>
/// Resolves the business date independently for each tile query, matching the pattern in the
/// dashboard refresh design note.
/// </remarks>
public interface ICompassDashboardReadService
{
    /// <summary>Returns the four tile counts, plus the date they were computed against (FR-027).</summary>
    Task<SalesDashboardDto> GetDashboardAsync(CancellationToken cancellationToken);

    /// <summary>Returns the drill-down rows for one tile (FR-006).</summary>
    /// <param name="category">Which tile's detail to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<DashboardBreakdownRowDto>> GetBreakdownAsync(
        DashboardCategory category,
        CancellationToken cancellationToken);
}
