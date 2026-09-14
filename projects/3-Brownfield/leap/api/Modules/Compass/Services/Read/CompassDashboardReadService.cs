using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>Resolves the business date and asks the repository for the Sales Dashboard's projection.</summary>
/// <remarks>
/// The business date is resolved here, once per request — not in the handler and not in the
/// repository — so the four tile counts and every breakdown a caller might request in the same request
/// cannot disagree about what day it is. No writes, and no reference to a data context: the actual
/// queries live in <see cref="ICompassDashboardRepository"/>, because
/// <c>CompassBoundaryTests.RuleTwo</c> forbids one here.
/// </remarks>
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
