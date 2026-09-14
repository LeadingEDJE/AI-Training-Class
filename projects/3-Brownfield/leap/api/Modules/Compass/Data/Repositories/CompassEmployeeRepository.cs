using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>
/// EDJEr data access over the single application context.
/// </summary>
/// <remarks>
/// Reaches data through <c>context.Set&lt;T&gt;()</c> rather than a <c>DbSet</c> property, because
/// Compass declares none on the context — Option 2. No
/// method persists; that boundary is the service's (Principle III), reached through <see
/// cref="ICompassUnitOfWork"/>.
///
/// It does not extend <c>Repository&lt;T&gt;</c>, for the same reason <see
/// cref="CompassLookupRepository{TLookup}"/> does not: the base class's members match none of these
/// signatures, so inheritance buys no reuse while bringing a public <c>DeleteAsync</c> that an
/// EDJEr must never have (Principle VIII, BR-9).
/// </remarks>
public class CompassEmployeeRepository(LeapDbContext context) : ICompassEmployeeRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// The classification's name is resolved by a join in this one query, not per row. Ninety
    /// EDJErs each triggering their own lookup is the N+1 the p95 target (AC-NFR-4) is measured against.
    /// </remarks>
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
    /// Tracked deliberately — the service mutates what this returns and then saves. An
    /// <c>AsNoTracking</c> read here would make every update silently do nothing.
    /// </remarks>
    public async Task<Employee?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await context.Set<Employee>().FirstOrDefaultAsync(employee => employee.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Normalised with <c>Trim().ToLower()</c> on both sides so this pre-check asks the same question
    /// <c>ux_employee_email_ci</c> answers. <c>ToLower()</c> rather than <c>ILike</c>: Npgsql translates it
    /// to SQL <c>lower()</c> and the in-memory provider evaluates it, so the same comparison holds
    /// under the integration suite and the endpoint tests. The trim is applied to the candidate only —
    /// stored values are normalised on write, so <c>btrim</c> on the column would be redundant work the
    /// index expression already covers.
    /// </remarks>
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
    /// <remarks>
    /// A NULL <c>end_date</c> means open-ended, so these are exactly the engagements AC-19 requires to be
    /// end-dated before the EDJEr can be deactivated. The client's name is joined in rather than looked up
    /// afterwards, because the refusal has to be actionable by a human and an id alone is not.
    /// </remarks>
    public async Task<IReadOnlyList<BlockingAssignmentDto>> GetOpenAssignmentsAsync(
        int employeeId,
        CancellationToken cancellationToken
    )
    {
        // Ordered and projected in two steps, deliberately. Constructing the DTO inside the join and then
        // ordering by its members does not translate — Npgsql raises "The LINQ expression could not be
        // translated" and the route answers 500. The in-memory provider evaluates the same expression
        // happily in .NET, so this shape passed every unit test and failed the first integration run.
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
