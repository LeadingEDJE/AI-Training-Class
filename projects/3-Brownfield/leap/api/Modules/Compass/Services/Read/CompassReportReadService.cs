using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>Projects the Compass reports from the dashboard's populations.</summary>
/// <remarks>
/// The three sections are the dashboard's three breakdowns re-shaped, not re-derived: §1 is
/// <see cref="DashboardCategory.Beach"/> (FR-005, FR-010), §2
/// <see cref="DashboardCategory.ConfirmedRollouts"/> (FR-004, FR-011), §3
/// <see cref="DashboardCategory.ExpiringSows"/> (FR-003, FR-012) — so SC-003's tile-to-section
/// equalities hold by construction. Re-deriving them in a repository of their own is the second
/// definition BR-11 forbids, and would need its own tie folding, its own vacuous-truth guard for
/// rollouts (research D-2) and its own command-count test. The business date resolves here once, or
/// sections could disagree; no data context (<c>CompassBoundaryTests.RuleTwo</c>); §1 drops the client.
/// </remarks>
public class CompassReportReadService(
    ICompassDashboardRepository repository,
    ICompassReportRepository reportRepository,
    ICompassBusinessDate businessDate) : ICompassReportReadService
{
    /// <inheritdoc />
    /// <remarks>
    /// Delegation, deliberately. The grain, the FR-032 qualifying test and FR-015's span rule
    /// are all set-based SQL and belong in the repository — <c>CompassBoundaryTests.RuleTwo</c> forbids
    /// a data context here in any case. This layer owns exactly one thing: resolving the business date
    /// once, from <see cref="ICompassBusinessDate"/>, so the report cannot be computed against a
    /// different day than the dashboard above it.
    /// </remarks>
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

            // Order is NOT re-applied here. The repository orders in SQL on the source date column;
            // a second sort in this layer is a second place for FR-009's direction to drift.
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

            // Date and day count are passed through as-is, NOT coalesced. An expiring SOW is selected
            // BY its end date so neither should ever be absent — but this section is read for triage,
            // and defaulting a missing date to today would render the most urgent row the screen can
            // show for data it does not have. Nulls render as empty cells instead.
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
    /// A straight delegation, and deliberately not more than one. Unlike the availability report, this
    /// lookup resolves no business date — the range comes from the caller, so there is nothing here to
    /// anchor and no opportunity for two sections to disagree about what day it is. The query itself
    /// cannot live in this layer: <c>CompassBoundaryTests.RuleTwo</c> forbids the Compass services from
    /// referencing a data context at all.
    /// </remarks>
    public Task<IReadOnlyList<AssignmentStartRowDto>> GetAssignmentStartsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken) =>
        reportRepository.GetAssignmentStartsAsync(from, to, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// A straight delegation, for the same reason as <see cref="GetAssignmentStartsAsync"/>: the range
    /// comes from the caller, so there is no business date to anchor here, and the query cannot live in
    /// this layer — <c>CompassBoundaryTests.RuleTwo</c> forbids a data context in a Compass service.
    /// </remarks>
    public Task<IReadOnlyList<SowExtensionRowDto>> GetSowExtensionsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken) =>
        reportRepository.GetSowExtensionsAsync(from, to, cancellationToken);
}
