using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>Resolves the business date and asks the repository for the Sales Dashboard's projection.</summary>
public class CompassDashboardReadService(
    ICompassDashboardRepository repository,
    ICompassBusinessDate businessDate) : ICompassDashboardReadService
{
    /// <inheritdoc />
    public Task<SalesDashboardDto> GetDashboardAsync(CancellationToken cancellationToken) =>
        repository.GetCountsAsync(businessDate.Today(), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<DashboardBreakdownRowDto>> GetBreakdownAsync(
        DashboardCategory category,
        CancellationToken cancellationToken) =>
        repository.GetBreakdownAsync(category, businessDate.Today(), cancellationToken);
}
