using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>Report-only Compass queries, aggregated entirely in memory after a bulk load.</summary>
public class CompassReportRepository(LeapDbContext context, IClientStatusDerivation statusDerivation)
    : ICompassReportRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AssignmentDurationRowDto>> GetAssignmentDurationAsync(
        DateOnly today, CancellationToken cancellationToken)
    {
        var activePairs = context.Set<ClientAssignment>()
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Where(a => !a.Client!.IsInternal);

        // Assignments that have not yet started are floored at zero days here.
        var contributing = context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(statusDerivation.HasStarted(today))
            .Where(a => activePairs.Any(
                active => active.EmployeeId == a.EmployeeId && active.ClientId == a.ClientId));

        var gross = await contributing
            .GroupBy(a => new
            {
                a.EmployeeId,
                EmployeeName = a.Employee!.FirstName + " " + a.Employee.LastName,
                a.ClientId,
                ClientName = a.Client!.ClientName,
                CoachName = a.Employee.Coach != null
                    ? a.Employee.Coach.FirstName + " " + a.Employee.Coach.LastName
                    : null,
                EmployeeType = a.Employee.EmployeeType != null
                    ? a.Employee.EmployeeType.TypeName
                    : null,
            })
            .Select(g => new
            {
                g.Key.EmployeeId,
                g.Key.EmployeeName,
                g.Key.ClientId,
                g.Key.ClientName,
                g.Key.CoachName,
                g.Key.EmployeeType,
                Days = g.Sum(a =>
                    ((a.EndDate ?? today).DayNumber - a.StartDate.DayNumber) + 1),
            })
            .ToListAsync(cancellationToken);

        var unserved = await contributing
            .Where(statusDerivation.IsFutureDated(today))
            .GroupBy(a => new { a.EmployeeId, a.ClientId })
            .Select(g => new
            {
                g.Key.EmployeeId,
                g.Key.ClientId,
                Days = g.Sum(a => a.EndDate!.Value.DayNumber - today.DayNumber),
            })
            .ToListAsync(cancellationToken);

        var unservedByPair = unserved.ToDictionary(x => (x.EmployeeId, x.ClientId), x => x.Days);

        return
        [
            .. gross
                .Select(row =>
                {
                    var totalDays = row.Days
                        - unservedByPair.GetValueOrDefault((row.EmployeeId, row.ClientId));

                    return new AssignmentDurationRowDto
                    {
                        EmployeeId = row.EmployeeId,
                        EmployeeName = row.EmployeeName,
                        EmployeeType = row.EmployeeType,
                        ClientId = row.ClientId,
                        ClientName = row.ClientName,
                        CoachName = row.CoachName,
                        TotalDays = totalDays,
                        DurationDisplay = AssignmentDurationFormat.Describe(totalDays),
                    };
                })
                .OrderByDescending(row => row.TotalDays)
                .ThenBy(row => row.EmployeeName, StringComparer.Ordinal)
                .ThenBy(row => row.ClientName, StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc />
    /// <remarks>Exclusive of the upper bound (<c>&lt; to</c>), sorted earliest-first.</remarks>
    public async Task<IReadOnlyList<AssignmentStartRowDto>> GetAssignmentStartsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var rows = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(a => a.StartDate >= from && a.StartDate <= to)
            .Select(a => new
            {
                EmployeeName = a.Employee!.FirstName + " " + a.Employee.LastName,
                EmployeeType = a.Employee.EmployeeType != null
                    ? a.Employee.EmployeeType.TypeName
                    : null,
                ClientName = a.Client!.ClientName,
                a.StartDate,
            })
            .OrderBy(row => row.StartDate)
            .ThenBy(row => row.EmployeeName)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new AssignmentStartRowDto
        {
            EmployeeName = row.EmployeeName,
            EmployeeType = row.EmployeeType,
            ClientName = row.ClientName,
            StartDate = row.StartDate,
        })];
    }

    /// <inheritdoc />
    /// <remarks>Inclusive at both ends, sorted oldest extension start date first.</remarks>
    public async Task<IReadOnlyList<SowExtensionRowDto>> GetSowExtensionsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var rows = await context.Set<Sow>()
            .AsNoTracking()
            .Where(s => s.SowType == SowType.SowExtension)
            .Where(s => s.SowStartDate >= from && s.SowStartDate <= to)
            .Select(s => new
            {
                EmployeeName = s.ClientAssignment!.Employee!.FirstName + " " + s.ClientAssignment.Employee.LastName,
                EmployeeType = s.ClientAssignment.Employee.EmployeeType != null
                    ? s.ClientAssignment.Employee.EmployeeType.TypeName
                    : null,
                ClientName = s.ClientAssignment.Client!.ClientName,
                s.SowStartDate,
            })
            .OrderBy(row => row.SowStartDate)
            .ThenBy(row => row.EmployeeName)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new SowExtensionRowDto
        {
            EmployeeName = row.EmployeeName,
            EmployeeType = row.EmployeeType,
            ClientName = row.ClientName,
            ExtensionStartDate = row.SowStartDate,
        })];
    }
}
