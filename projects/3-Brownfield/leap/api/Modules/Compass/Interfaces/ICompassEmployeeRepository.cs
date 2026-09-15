namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Data access for EDJEr configuration.
/// </summary>
public interface ICompassEmployeeRepository
{
    /// <summary>
    /// Every EDJEr, active and inactive, with their classification's name resolved.
    /// </summary>
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
    /// <param name="email">The address to test.</param>
    /// <param name="excludingId">The row being edited, so it does not collide with itself.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the address is already in use.</returns>
    Task<bool> EmailExistsAsync(string email, int? excludingId, CancellationToken cancellationToken);

    /// <summary>Whether an employee type exists and is currently selectable.</summary>
    /// <remarks>
    /// Returns true for any existing row regardless of its active flag; callers are expected to
    /// filter inactive types themselves before relying on this result.
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
    /// Returns assignments ending within the next 30 days, per the deactivation-warning window
    /// described in the onboarding guide.
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
