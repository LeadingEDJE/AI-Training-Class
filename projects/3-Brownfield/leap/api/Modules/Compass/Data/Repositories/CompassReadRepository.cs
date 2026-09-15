using System.Linq.Expressions;
using LeadingEDJE.Leap.Api.Modules.Compass.Authorization;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>EF Core reads backing the Compass application read surface (ADR-014).</summary>
public class CompassReadRepository(LeapDbContext context, IClientStatusDerivation statusDerivation)
    : ICompassReadRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TeamDirectoryRowDto>> GetTeamDirectoryAsync(
        TeamDirectoryQuery query,
        CompassTier tier,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var isCurrent = statusDerivation.IsCurrent(today);
        var hasStarted = statusDerivation.HasStarted(today);
        var showsStatus = tier.SeesInactiveEdjers();

        IQueryable<Employee> employees = context
            .Set<Employee>()
            .AsNoTracking()
            .Where(EdjerVisibility.For(tier));

        employees = query.Status switch
        {
            DirectoryStatusFilter.Active => employees.Where(e => e.IsActive),
            DirectoryStatusFilter.Inactive => employees.Where(e => !e.IsActive),
            _ => employees,
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Matches against first and last name.
            var search = query.Search.Trim().ToLower();
            employees = employees.Where(e => e.LastName.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.EmployeeType))
        {
            var employeeType = query.EmployeeType.Trim();
            employees = employees.Where(e => e.EmployeeType != null && e.EmployeeType.TypeName == employeeType);
        }

        if (!string.IsNullOrWhiteSpace(query.State))
        {
            var state = query.State.Trim();
            employees = employees.Where(e => e.StateOfResidence == state);
        }

        if (query.CoachId is not null)
        {
            employees = employees.Where(e => e.CoachEmployeeId == query.CoachId);
        }

        employees = Sort(employees, query.Sort, query.Descending, isCurrent, hasStarted);

        return await employees
            .Select(e => new TeamDirectoryRowDto
            {
                Id = e.Id,
                FirstName = e.FirstName,
                LastName = e.LastName,
                HireDate = e.HireDate,
                Email = e.Email,
                EmployeeType = e.EmployeeType != null ? e.EmployeeType.TypeName : string.Empty,

                CoachId = e.Coach != null ? e.Coach.Id : (int?)null,
                Coach = e.Coach != null ? e.Coach.FirstName + " " + e.Coach.LastName : null,
                State = e.StateOfResidence,
                CurrentAssignments = e.ClientAssignments
                    .AsQueryable()
                    .Where(isCurrent)
                    .Where(hasStarted)
                    .Select(a => new TeamDirectoryAssignmentDto
                    {
                        ClientId = a.ClientId,
                        ClientName = a.Client != null ? a.Client.ClientName : string.Empty,
                    })
                    .ToList(),

                IsActive = showsStatus ? e.IsActive : null,
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmployeeDetailDto?> GetEmployeeDetailAsync(
        int employeeId,
        CompassTier tier,
        string? viewerEmail,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var seesElevated = tier.SeesOthersSowsAndNotes();
        var seesTimeTracking = tier.SeesTimeTrackingSettings();

        var ownsRecord = !seesElevated
            && await context
                .Set<Employee>()
                .AsNoTracking()
                .Where(e => e.Id == employeeId)
                .Where(OwnRecordMatch.IsOwnedBy(viewerEmail))
                .AnyAsync(cancellationToken);

        // Entitlement is enforced after loading, in the endpoint layer, not in this query.
        var detail = await context
            .Set<Employee>()
            .AsNoTracking()
            .Where(EdjerVisibility.For(tier))
            .Where(e => e.Id == employeeId)
            .Select(e => new EmployeeDetailDto
            {
                Id = e.Id,
                FirstName = e.FirstName,
                LastName = e.LastName,
                HireDate = e.HireDate,
                Email = e.Email,
                EmployeeType = e.EmployeeType != null ? e.EmployeeType.TypeName : string.Empty,
                CoachId = e.Coach != null ? e.Coach.Id : (int?)null,
                Coach = e.Coach != null ? e.Coach.FirstName + " " + e.Coach.LastName : null,
                State = e.StateOfResidence,

                IsDeliveryTeam = e.IsDeliveryTeam,
                IsActive = seesElevated ? e.IsActive : null,
                DirectReports = e.Coachees
                    .AsQueryable()
                    .Where(EdjerVisibility.For(tier))
                    .OrderBy(c => c.HireDate)
                    .ThenBy(c => c.LastName)
                    .Select(c => new DirectReportDto
                    {
                        Id = c.Id,
                        FirstName = c.FirstName,
                        LastName = c.LastName,

                        IsActive = seesElevated ? c.IsActive : (bool?)null,
                    })
                    .ToList(),
                TimeTrackingSettings = seesTimeTracking
                    ? new TimeTrackingSettingsDto
                    {
                        TimesheetRequired = e.TimesheetRequired,
                        CanSubmitUnder40 = e.CanSubmitUnder40,
                        IncludeInPayroll = e.IncludeInPayroll,
                    }
                    : null,
                // Oldest first; AssignmentId ties off equal start dates.
                AssignmentHistory = e.ClientAssignments
                    .OrderByDescending(a => a.StartDate)
                    .ThenByDescending(a => a.Id)
                    .Select(a => new EmployeeAssignmentDto
                    {
                        AssignmentId = a.Id,
                        ClientId = a.ClientId,
                        ClientName = a.Client != null ? a.Client.ClientName : string.Empty,

                        IsInternal = a.Client != null && a.Client.IsInternal,
                        StartDate = a.StartDate,
                        EndDate = a.EndDate,

                        Note = seesElevated ? a.Note : null,

                        Sows = seesElevated || ownsRecord
                            ? a.Sows
                                .Select(sow => new EmployeeSowDto
                                {
                                    SowType = sow.SowType.ToString(),
                                    StartDate = sow.SowStartDate,
                                    EndDate = sow.SowEndDate,
                                    RateIncrease = seesElevated ? sow.RateIncrease : null,
                                    Note = seesElevated ? sow.Note : null,
                                })
                                .ToList()
                            : null,

                        CanViewAssignment = seesElevated ? true : null,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (detail is null)
        {
            return null;
        }

        var currentAssignmentIds = await context
            .Set<ClientAssignment>()
            .AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var current = currentAssignmentIds.ToHashSet();

        foreach (var assignment in detail.AssignmentHistory)
        {
            assignment.Status = statusDerivation
                .StatusOfAssignment(current.Contains(assignment.AssignmentId))
                .ToString();
        }

        return detail;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientDirectoryRowDto>> GetClientDirectoryAsync(
        ClientDirectoryQuery query,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        IQueryable<Client> clients = context.Set<Client>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            clients = clients.Where(c => c.ClientName.ToLower().Contains(search));
        }

        var activeIds = await clients
            .Where(statusDerivation.IsActive(today))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var active = activeIds.ToHashSet();

        var everAssignedIds = await clients
            .Where(statusDerivation.HasEverBeenAssigned())
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var everAssigned = everAssignedIds.ToHashSet();

        var rows = await clients
            .Select(c => new { c.Id, c.ClientName })
            .ToListAsync(cancellationToken);

        var projected = rows.Select(c => new ClientDirectoryRowDto
        {
            Id = c.Id,
            ClientName = c.ClientName,

            Status = statusDerivation
                .StatusOfClient(active.Contains(c.Id), everAssigned.Contains(c.Id))
                .ToString(),
        });

        var ordered = query.Sort?.Trim().ToLowerInvariant() switch
        {
            "status" => projected.OrderBy(r => r.Status, StringComparer.Ordinal)
                                 .ThenBy(r => r.ClientName, StringComparer.Ordinal),
            _ => projected.OrderBy(r => r.ClientName, StringComparer.Ordinal).ThenBy(r => r.Id),
        };

        return query.Descending ? [.. ordered.Reverse()] : [.. ordered];
    }

    /// <inheritdoc />
    public async Task<ClientViewDto?> GetClientViewAsync(
        int clientId,
        CompassTier tier,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var seesElevated = tier.SeesOthersSowsAndNotes();
        var seesConfiguration = tier.SeesTimeTrackingSettings();
        var seesInactive = tier.SeesInactiveEdjers();

        var isActive = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(c => c.Id == clientId)
            .AnyAsync(statusDerivation.IsActive(today), cancellationToken);

        var hasEverBeenAssigned = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(c => c.Id == clientId)
            .AnyAsync(statusDerivation.HasEverBeenAssigned(), cancellationToken);

        var view = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(c => new ClientViewDto
            {
                Id = c.Id,

                ClientName = c.ClientName,
                Status = statusDerivation.StatusOfClient(isActive, hasEverBeenAssigned).ToString(),

                IsInternal = c.IsInternal,
                ClientDetails = seesConfiguration
                    ? new ClientDetailsDto
                    {
                        MsaSignedDate = c.MsaSignedDate,
                        NdaSignedDate = c.NdaSignedDate,
                        IsInternal = c.IsInternal,
                        InvoiceFrequency = c.InvoiceFrequencyType != null
                            ? c.InvoiceFrequencyType.TypeName
                            : null,
                        BillableTimeCategories = c.BillableTimeCategories
                            .Select(b => b.CategoryName)
                            .ToList(),
                    }
                    : null,

                AssignmentHistory = c.ClientAssignments
                    .Where(a => seesInactive || (a.Employee != null && a.Employee.IsActive))
                    .OrderByDescending(a => a.StartDate)
                    .ThenByDescending(a => a.Id)
                    .Select(a => new ClientAssignmentHistoryDto
                    {
                        AssignmentId = a.Id,
                        EmployeeId = a.EmployeeId,
                        EmployeeName = a.Employee != null
                            ? a.Employee.FirstName + " " + a.Employee.LastName
                            : string.Empty,
                        StartDate = a.StartDate,
                        EndDate = a.EndDate,
                        EmployeeIsActive = seesInactive && a.Employee != null ? a.Employee.IsActive : null,

                        CanViewSow = seesElevated && !c.IsInternal ? true : null,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (view is null)
        {
            return null;
        }

        var currentAssignmentIds = await context
            .Set<ClientAssignment>()
            .AsNoTracking()
            .Where(a => a.ClientId == clientId)
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var current = currentAssignmentIds.ToHashSet();

        foreach (var assignment in view.AssignmentHistory)
        {
            assignment.Status = statusDerivation
                .StatusOfAssignment(current.Contains(assignment.AssignmentId))
                .ToString();
        }

        return view;
    }

    /// <summary>Applies the requested column sort, defaulting to hire date.</summary>
    private static IQueryable<Employee> Sort(
        IQueryable<Employee> employees,
        string? sort,
        bool descending,
        Expression<Func<ClientAssignment, bool>> isCurrent,
        Expression<Func<ClientAssignment, bool>> hasStarted)
    {
        var ascending = sort?.Trim().ToLowerInvariant() switch
        {
            "lastname" => employees.OrderBy(e => e.LastName).ThenBy(e => e.FirstName),
            "firstname" => employees.OrderBy(e => e.FirstName).ThenBy(e => e.LastName),
            "email" => employees.OrderBy(e => e.Email),
            "employeetype" => employees.OrderBy(e => e.EmployeeType!.TypeName).ThenBy(e => e.LastName),
            "state" => employees.OrderBy(e => e.StateOfResidence).ThenBy(e => e.LastName),
            "coach" => employees.OrderBy(e => e.Coach!.LastName).ThenBy(e => e.LastName),

            "currentclients" => employees
                .OrderBy(e => e.ClientAssignments
                    .AsQueryable()
                    .Where(isCurrent)
                    .Where(hasStarted)
                    .Select(a => a.Client!.ClientName)
                    .OrderBy(name => name)
                    .FirstOrDefault())
                .ThenBy(e => e.LastName),
            _ => employees.OrderBy(e => e.HireDate).ThenBy(e => e.LastName),
        };

        return descending ? Reverse(ascending) : ascending;
    }

    /// <summary>
    /// Reverses an ordered query in memory, since <c>IOrderedQueryable</c> exposes no direction flip
    /// and <c>Queryable.Reverse()</c> does not translate under Npgsql.
    /// </summary>
    private static IQueryable<Employee> Reverse(IQueryable<Employee> ordered) =>
        ordered.Reverse();
}
