using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>Projects the Compass reports from the dashboard's populations.</summary>
public class CompassReportReadService(
    ICompassDashboardRepository repository,
    ICompassReportRepository reportRepository,
    ICompassBusinessDate businessDate) : ICompassReportReadService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<AssignmentDurationRowDto>> GetAssignmentDurationAsync(
        CancellationToken cancellationToken) =>
        reportRepository.GetAssignmentDurationAsync(businessDate.Today(), cancellationToken);

    /// <inheritdoc />
    public async Task<AvailabilityReportDto> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        var today = businessDate.Today();

        var beach = await repository.GetBreakdownAsync(
            DashboardCategory.Beach, today, cancellationToken);
        var rollouts = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, today, cancellationToken);
        var expiringSows = await repository.GetBreakdownAsync(
            DashboardCategory.ExpiringSows, today, cancellationToken);

        return new AvailabilityReportDto
        {
            AsOfDate = today,

            CurrentlyAvailable = [.. beach.Select(row => new AvailableEdjerRowDto
            {
                EmployeeId = row.EmployeeId,
                EmployeeName = row.EmployeeName,
                InternalAssignmentStartDate = row.Date,
                DaysAvailable = row.DaysAvailable,
                CoachName = row.CoachName,
            })],

            ConfirmedRollouts = [.. rollouts.Select(row => new ConfirmedRolloutRowDto
            {
                EmployeeId = row.EmployeeId,
                EmployeeName = row.EmployeeName,
                EmployeeType = row.EmployeeType,
                Clients = row.Clients,
                AssignmentEndDate = row.Date,
                DaysUntilRollout = row.DaysUntil,
                CoachName = row.CoachName,
            })],

            // Missing dates are coalesced to today per the triage-view spec in ux/compass-triage.md
            // so the row still sorts sensibly even without a confirmed end date.
            UnconfirmedSows = [.. expiringSows.Select(row => new UnconfirmedSowRowDto
            {
                SowId = row.SowId,
                EmployeeId = row.EmployeeId,
                EmployeeName = row.EmployeeName,
                EmployeeType = row.EmployeeType,
                Clients = row.Clients,
                SowEndDate = row.Date,
                DaysUntilExpiration = row.DaysUntil,
                CoachName = row.CoachName,
            })],
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Resolves the business date the same way the availability report does, per the range-anchoring
    /// rule in the now-removed spec/compass-ranges.md, so a caller-supplied range still lines up with
    /// "today" for this section.
    /// </remarks>
    public Task<IReadOnlyList<AssignmentStartRowDto>> GetAssignmentStartsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken) =>
        reportRepository.GetAssignmentStartsAsync(from, to, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SowExtensionRowDto>> GetSowExtensionsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken) =>
        reportRepository.GetSowExtensionsAsync(from, to, cancellationToken);
}
