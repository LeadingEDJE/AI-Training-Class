namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Data access for EDJEr configuration.
/// </summary>
/// <remarks>
/// Lives inside the module per Principle III's new-module rule; only the entity-agnostic machinery
/// stays in <c>api/Platform/</c>, and Timesheet's placement there is grandfathered and must not be
/// extended. It deliberately does not derive from <c>IRepository&lt;T&gt;</c>: that contract carries
/// <c>DeleteAsync</c>, and an EDJEr is deactivated rather than removed — other records reference them
/// by id, and email uniqueness spans inactive rows precisely because they are never deleted
/// (Principle VIII, BR-9). The same reasoning applies to
/// <see cref="ICompassLookupRepository{TLookup}"/>. No method persists; that boundary is the service's
/// (<see cref="ICompassUnitOfWork"/>, Principle III).
/// </remarks>
public interface ICompassEmployeeRepository
{
    /// <summary>
    /// Every EDJEr, active and inactive, with their classification's name resolved.
    /// </summary>
    /// <remarks>
    /// Inactive EDJErs are included: a Compass Admin and above see both (BR-1), and an administrator who
    /// cannot see a deactivated EDJEr cannot reactivate one. Ordered by family then given name, which is
    /// how both the list and the coach picker present them.
    /// <para>
    /// The classification name is resolved in this one query rather than per row — the p95 target
    /// (AC-NFR-4) is what rules out the N+1.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every EDJEr paired with their employee type's display name.</returns>
    Task<IReadOnlyList<(Employee Employee, string EmployeeTypeName)>> GetAllWithTypeNameAsync(
        CancellationToken cancellationToken
    );

    /// <summary>One EDJEr by id, tracked so the service can mutate and save.</summary>
    /// <param name="id">The EDJEr's identity key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The EDJEr, or null when no such row exists.</returns>
    Task<Employee?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Whether any other EDJEr already holds <paramref name="email"/>, compared case-insensitively and
    /// trimmed.
    /// </summary>
    /// <remarks>
    /// Spans active and inactive rows (BR-9). This is a pre-check so the rejection can name the
    /// conflicting field (FR-013); it is not the guard — <c>ux_employee_email_ci</c> is, because a
    /// concurrent writer can commit between this call and the write.
    /// </remarks>
    /// <param name="email">The address to test.</param>
    /// <param name="excludingId">The row being edited, so it does not collide with itself.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the address is already in use.</returns>
    Task<bool> EmailExistsAsync(string email, int? excludingId, CancellationToken cancellationToken);

    /// <summary>Whether an employee type exists and is currently selectable.</summary>
    /// <remarks>
    /// One question, not two, because the server's answer is the same either way — a 400. The selection
    /// list omitting inactive types is convenience; this is the enforcement (FR-005).
    /// </remarks>
    /// <param name="employeeTypeId">The classification to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the type exists and is active.</returns>
    Task<bool> ActiveEmployeeTypeExistsAsync(
        int employeeTypeId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Whether an employee type exists at all, active or retired.
    /// </summary>
    /// <param name="employeeTypeId">The classification to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when a row with that identifier exists, whatever its active flag.</returns>
    /// <remarks>
    /// Used only by the migration write path, which may record a retired classification because it is
    /// reporting what the legacy directory said rather than choosing one now. It still has to exist —
    /// dropping the existence check along with the active check would turn a bad identifier into a
    /// foreign-key violation at <c>SaveChangesAsync</c> instead of a named validation error.
    /// </remarks>
    Task<bool> EmployeeTypeExistsAsync(int employeeTypeId, CancellationToken cancellationToken);

    /// <summary>Whether an EDJEr exists, for validating a nominated coach.</summary>
    /// <param name="employeeId">The EDJEr to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the EDJEr exists.</returns>
    Task<bool> ExistsAsync(int employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether an EDJEr exists and is active, for validating a newly nominated coach. Separate from
    /// <see cref="ExistsAsync"/>: an already-assigned coach since deactivated stays acceptable.
    /// </summary>
    /// <param name="employeeId">The EDJEr to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the EDJEr exists and is active.</returns>
    Task<bool> ActiveEmployeeExistsAsync(int employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// The EDJEr's assignments that have no end date, with their client's name.
    /// </summary>
    /// <remarks>
    /// The input to the AC-19 deactivation guard. A null <c>end_date</c> means open-ended, so these are
    /// exactly the engagements that must be end-dated before the EDJEr can be deactivated. This stream
    /// reads assignments; it adds no management surface for them (spec A-6).
    /// </remarks>
    /// <param name="employeeId">The EDJEr whose engagements to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open assignments, oldest first.</returns>
    Task<IReadOnlyList<Dtos.BlockingAssignmentDto>> GetOpenAssignmentsAsync(
        int employeeId,
        CancellationToken cancellationToken
    );

    /// <summary>Stages a new EDJEr. Does not persist.</summary>
    /// <param name="employee">The EDJEr to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(Employee employee, CancellationToken cancellationToken);
}
