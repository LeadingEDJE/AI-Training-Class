using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>EF Core reads backing the Sales Dashboard (AC-36, AC-37).</summary>
public class CompassDashboardRepository(LeapDbContext context, IClientStatusDerivation statusDerivation)
    : ICompassDashboardRepository
{
    /// <inheritdoc />
    public async Task<SalesDashboardDto> GetCountsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var isCurrent = statusDerivation.IsCurrent(today);

        var hasStarted = statusDerivation.HasStarted(today);
        var isExpiring = statusDerivation.IsExpiringWithin(today, 90);
        var hasNoFollowOn = statusDerivation.HasNoFollowOn();

        // Issue #291: the tile counts active SOWs directly, one per row.
        var activeSowCount = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(isCurrent)
            .Where(hasStarted)
            .Where(a => !a.Client!.IsInternal)
            .CountAsync(cancellationToken);

        var expiringSowCount = await context.Set<Sow>()
            .AsNoTracking()
            .Where(isExpiring)
            .Where(hasNoFollowOn)
            .Where(statusDerivation.SowAssignmentIsOpenEndedAndActive(today))
            .CountAsync(cancellationToken);

        var confirmedRolloutCount = await RolloutEmployeeIds(today).CountAsync(cancellationToken);

        var beachCount = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(isCurrent)
            .Where(hasStarted)
            .Where(a => a.Client!.IsInternal)
            .Select(a => a.EmployeeId)
            .Distinct()
            .CountAsync(cancellationToken);

        return new SalesDashboardDto
        {
            AsOfDate = today,
            ActiveSowCount = activeSowCount,
            ExpiringSowCount = expiringSowCount,
            ConfirmedRolloutCount = confirmedRolloutCount,
            BeachCount = beachCount,
        };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DashboardBreakdownRowDto>> GetBreakdownAsync(
        DashboardCategory category,
        DateOnly today,
        CancellationToken cancellationToken) => category switch
        {
            DashboardCategory.ActiveSows => ActiveSowsBreakdown(today, cancellationToken),
            DashboardCategory.ExpiringSows => ExpiringSowsBreakdown(today, cancellationToken),
            DashboardCategory.ConfirmedRollouts => ConfirmedRolloutsBreakdown(today, cancellationToken),
            DashboardCategory.Beach => BeachBreakdown(today, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(category), category, "unrecognised dashboard category"),
        };

    /// <summary>One row per active SOW, matching the tile's own count exactly.</summary>
    private async Task<IReadOnlyList<DashboardBreakdownRowDto>> ActiveSowsBreakdown(
        DateOnly today, CancellationToken cancellationToken)
    {
        var rows = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Where(a => !a.Client!.IsInternal)
            .Select(a => new
            {
                AssignmentId = a.Id,
                EmployeeId = a.EmployeeId,
                EmployeeName = a.Employee!.FirstName + " " + a.Employee.LastName,
                EmployeeType = a.Employee.EmployeeType != null
                    ? a.Employee.EmployeeType.TypeName
                    : string.Empty,
                ClientId = a.ClientId,
                ClientName = a.Client!.ClientName,
                a.StartDate,
                MaxSowEndDate = context.Set<Sow>()
                    .Where(s => s.ClientAssignmentId == a.Id)
                    .Select(s => (DateOnly?)s.SowEndDate)
                    .Max(),
                CoachName = a.Employee.Coach != null
                    ? a.Employee.Coach.FirstName + " " + a.Employee.Coach.LastName
                    : null,
                CoachId = a.Employee.Coach != null ? a.Employee.Coach.Id : (int?)null,
            })
            .ToListAsync(cancellationToken);

        return [.. rows
            .OrderBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.MaxSowEndDate)
            .ThenBy(r => r.AssignmentId)
            .Select(r => new DashboardBreakdownRowDto
            {
                EmployeeId = r.EmployeeId,
                EmployeeName = r.EmployeeName,
                EmployeeType = r.EmployeeType,
                Clients = [new DashboardBreakdownClientDto { Id = r.ClientId, Name = r.ClientName }],
                Date = r.MaxSowEndDate,
                DaysUntil = null,
                CoachName = r.CoachName,
                CoachId = r.CoachId,
                SowId = null,
                StartDate = r.StartDate,
            })];
    }

    private async Task<IReadOnlyList<DashboardBreakdownRowDto>> ExpiringSowsBreakdown(
        DateOnly today, CancellationToken cancellationToken)
    {
        var rows = await context.Set<Sow>()
            .AsNoTracking()
            .Where(statusDerivation.IsExpiringWithin(today, 90))
            .Where(statusDerivation.HasNoFollowOn())
            .Where(statusDerivation.SowAssignmentIsOpenEndedAndActive(today))
            .OrderBy(s => s.SowEndDate)
            .Select(s => new
            {
                SowId = s.Id,
                EmployeeId = s.ClientAssignment!.EmployeeId,
                EmployeeName = s.ClientAssignment.Employee!.FirstName + " "
                    + s.ClientAssignment.Employee.LastName,
                EmployeeType = s.ClientAssignment.Employee.EmployeeType != null
                    ? s.ClientAssignment.Employee.EmployeeType.TypeName
                    : string.Empty,
                ClientId = s.ClientAssignment.ClientId,
                ClientName = s.ClientAssignment.Client!.ClientName,
                s.SowEndDate,
                CoachName = s.ClientAssignment.Employee.Coach != null
                    ? s.ClientAssignment.Employee.Coach.FirstName + " "
                        + s.ClientAssignment.Employee.Coach.LastName
                    : null,
                CoachId = s.ClientAssignment.Employee.Coach != null
                    ? s.ClientAssignment.Employee.Coach.Id
                    : (int?)null,
            })
            .ToListAsync(cancellationToken);

        // Ties on the same end date break on EDJEr name, then client name.
        return [.. rows
            .OrderBy(r => r.SowEndDate)
            .ThenBy(r => r.ClientName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.SowId)
            .Select(r => new DashboardBreakdownRowDto
            {
                EmployeeId = r.EmployeeId,
                EmployeeName = r.EmployeeName,
                EmployeeType = r.EmployeeType,
                Clients = [new DashboardBreakdownClientDto { Id = r.ClientId, Name = r.ClientName }],
                Date = r.SowEndDate,
                DaysUntil = r.SowEndDate.DayNumber - today.DayNumber,
                CoachName = r.CoachName,
                CoachId = r.CoachId,
                SowId = r.SowId,
            })];
    }

    /// <summary>Employee ids for whom any current non-internal-client assignment carries an end date.</summary>
    private IQueryable<int> RolloutEmployeeIds(DateOnly today) =>
        context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Where(a => !a.Client!.IsInternal)
            .GroupBy(a => a.EmployeeId)
            .Where(g => g.Count() == g.Count(a => a.EndDate != null))
            .Select(g => g.Key);

    private async Task<IReadOnlyList<DashboardBreakdownRowDto>> ConfirmedRolloutsBreakdown(
        DateOnly today, CancellationToken cancellationToken)
    {
        var rolloutEmployeeIds = RolloutEmployeeIds(today);

        var rows = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Where(a => !a.Client!.IsInternal)
            .Where(a => rolloutEmployeeIds.Contains(a.EmployeeId))
            .Where(a => a.EndDate == context.Set<ClientAssignment>()
                .Where(statusDerivation.HasStarted(today))
                .Where(statusDerivation.IsCurrent(today))
                .Where(b => b.EmployeeId == a.EmployeeId && !b.Client!.IsInternal)
                .Max(b => b.EndDate))
            .OrderBy(a => a.EndDate)
            .Select(a => new
            {
                a.EmployeeId,
                EmployeeName = a.Employee!.FirstName + " " + a.Employee.LastName,
                EmployeeType = a.Employee.EmployeeType != null ? a.Employee.EmployeeType.TypeName : string.Empty,
                a.ClientId,
                ClientName = a.Client!.ClientName,
                a.EndDate,
                CoachName = a.Employee.Coach != null
                    ? a.Employee.Coach.FirstName + " " + a.Employee.Coach.LastName
                    : null,
                CoachId = a.Employee.Coach != null ? a.Employee.Coach.Id : (int?)null,
            })
            .ToListAsync(cancellationToken);

        return [.. GroupToOneRowPerEmployee(
                rows.Select(r => (
                    r.EmployeeId,
                    r.EmployeeName,
                    r.EmployeeType,
                    Client: new DashboardBreakdownClientDto { Id = r.ClientId, Name = r.ClientName },
                    Date: (DateOnly?)r.EndDate,
                    DaysUntil: r.EndDate is { } end ? (int?)(end.DayNumber - today.DayNumber) : null,
                    DaysAvailable: (int?)null,
                    r.CoachName,
                    r.CoachId)))
            .OrderBy(r => r.DaysUntil)
            .ThenBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeId)];
    }

    private async Task<IReadOnlyList<DashboardBreakdownRowDto>> BeachBreakdown(
        DateOnly today, CancellationToken cancellationToken)
    {
        // One row per EDJEr, keyed to their LATEST current internal-assignment start date.
        var rows = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Where(a => a.Client!.IsInternal)
            .Where(a => a.StartDate == context.Set<ClientAssignment>()
                .Where(statusDerivation.HasStarted(today))
                .Where(statusDerivation.IsCurrent(today))
                .Where(b => b.EmployeeId == a.EmployeeId && b.Client!.IsInternal)
                .Min(b => b.StartDate))
            .Select(a => new
            {
                a.EmployeeId,
                EmployeeName = a.Employee!.FirstName + " " + a.Employee.LastName,
                EmployeeType = a.Employee.EmployeeType != null ? a.Employee.EmployeeType.TypeName : string.Empty,
                a.ClientId,
                ClientName = a.Client!.ClientName,
                a.StartDate,
                CoachName = a.Employee.Coach != null
                    ? a.Employee.Coach.FirstName + " " + a.Employee.Coach.LastName
                    : null,
                CoachId = a.Employee.Coach != null ? a.Employee.Coach.Id : (int?)null,
            })
            .ToListAsync(cancellationToken);

        return [.. GroupToOneRowPerEmployee(
                rows.Select(r => (
                    r.EmployeeId,
                    r.EmployeeName,
                    r.EmployeeType,
                    Client: new DashboardBreakdownClientDto { Id = r.ClientId, Name = r.ClientName },
                    Date: (DateOnly?)r.StartDate,
                    DaysUntil: (int?)null,
                    DaysAvailable: (int?)(today.DayNumber - r.StartDate.DayNumber),
                    r.CoachName,
                    r.CoachId)))
            .OrderBy(r => r.Date)
            .ThenBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeId)];
    }

    /// <summary>Folds the rows of a per-EDJEr breakdown into one row per client tied on the category date.</summary>
    private static IEnumerable<DashboardBreakdownRowDto> GroupToOneRowPerEmployee(
        IEnumerable<(int EmployeeId, string EmployeeName, string EmployeeType,
            DashboardBreakdownClientDto Client, DateOnly? Date, int? DaysUntil, int? DaysAvailable,
            string? CoachName, int? CoachId)> rows) =>
        rows
            .GroupBy(r => r.EmployeeId)
            .Select(group => new DashboardBreakdownRowDto
            {
                EmployeeId = group.Key,
                EmployeeName = group.First().EmployeeName,
                EmployeeType = group.First().EmployeeType,
                Clients = [.. group
                    .Select(r => r.Client)
                    .OrderBy(client => client.Name, StringComparer.Ordinal)
                    .ThenBy(client => client.Id)],
                Date = group.First().Date,
                DaysUntil = group.First().DaysUntil,
                DaysAvailable = group.First().DaysAvailable,
                CoachName = group.First().CoachName,
                CoachId = group.First().CoachId,
            });
}
