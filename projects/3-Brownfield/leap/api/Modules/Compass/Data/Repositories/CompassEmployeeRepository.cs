using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>EDJEr data access, extending the shared repository base for delete support.</summary>
public class CompassEmployeeRepository(LeapDbContext context) : ICompassEmployeeRepository
{
    /// <inheritdoc />
    public async Task<
        IReadOnlyList<(Employee Employee, string EmployeeTypeName)>
    > GetAllWithTypeNameAsync(CancellationToken cancellationToken)
    {
        var rows = await context
            .Set<Employee>()
            .AsNoTracking()
            .Join(
                context.Set<EmployeeType>().AsNoTracking(),
                employee => employee.EmployeeTypeId,
                employeeType => employeeType.Id,
                (employee, employeeType) =>
                    new { Employee = employee, EmployeeTypeName = employeeType.TypeName }
            )
            .OrderBy(row => row.Employee.LastName)
            .ThenBy(row => row.Employee.FirstName)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => (row.Employee, row.EmployeeTypeName))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Tracked, not <c>AsNoTracking</c> — the service mutates and saves the same instance. Includes
    /// <see cref="Employee.EmployeeSkills"/> so the service can diff the tagged set against a request
    /// instead of reloading it separately.
    /// </remarks>
    public async Task<Employee?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await context
            .Set<Employee>()
            .Include(employee => employee.EmployeeSkills)
            .FirstOrDefaultAsync(employee => employee.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> EmailExistsAsync(
        string email,
        int? excludingId,
        CancellationToken cancellationToken
    )
    {
        var candidate = email.Trim().ToLowerInvariant();
        return await context
            .Set<Employee>()
            .AsNoTracking()
            .AnyAsync(
                employee => employee.Id != excludingId && employee.Email.ToLower() == candidate,
                cancellationToken
            );
    }

    /// <inheritdoc />
    public async Task<bool> ActiveEmployeeTypeExistsAsync(
        int employeeTypeId,
        CancellationToken cancellationToken
    ) =>
        await context
            .Set<EmployeeType>()
            .AsNoTracking()
            .AnyAsync(
                employeeType => employeeType.Id == employeeTypeId && employeeType.IsActive,
                cancellationToken
            );

    /// <inheritdoc />
    public async Task<bool> EmployeeTypeExistsAsync(
        int employeeTypeId,
        CancellationToken cancellationToken
    ) =>
        await context
            .Set<EmployeeType>()
            .AsNoTracking()
            .AnyAsync(employeeType => employeeType.Id == employeeTypeId, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(int employeeId, CancellationToken cancellationToken) =>
        await context
            .Set<Employee>()
            .AsNoTracking()
            .AnyAsync(employee => employee.Id == employeeId, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ActiveEmployeeExistsAsync(
        int employeeId,
        CancellationToken cancellationToken
    ) =>
        await context
            .Set<Employee>()
            .AsNoTracking()
            .AnyAsync(
                employee => employee.Id == employeeId && employee.IsActive,
                cancellationToken
            );

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlockingAssignmentDto>> GetOpenAssignmentsAsync(
        int employeeId,
        CancellationToken cancellationToken
    )
    {
        var rows = await context
            .Set<ClientAssignment>()
            .AsNoTracking()
            .Where(assignment => assignment.EmployeeId == employeeId && assignment.EndDate == null)
            .Join(
                context.Set<Client>().AsNoTracking(),
                assignment => assignment.ClientId,
                client => client.Id,
                (assignment, client) =>
                    new
                    {
                        AssignmentId = assignment.Id,
                        ClientId = client.Id,
                        client.ClientName,
                        assignment.StartDate,
                    }
            )
            .OrderBy(row => row.StartDate)
            .ThenBy(row => row.AssignmentId)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new BlockingAssignmentDto(
                row.AssignmentId,
                row.ClientId,
                row.ClientName,
                row.StartDate
            )),
        ];
    }

    /// <inheritdoc />
    public async Task AddAsync(Employee employee, CancellationToken cancellationToken) =>
        await context.Set<Employee>().AddAsync(employee, cancellationToken);
}
