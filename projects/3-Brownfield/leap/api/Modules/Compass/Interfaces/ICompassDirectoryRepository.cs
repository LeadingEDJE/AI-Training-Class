namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Read-only data access for the Compass directory boundary.
/// </summary>
public interface ICompassDirectoryRepository
{
    /// <summary>
    /// Returns the Compass employee with the given identifier, or <c>null</c> when no such employee
    /// exists.
    /// </summary>
    Task<Employee?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the Compass employee whose email matches <paramref name="email"/>, or <c>null</c>
    /// when none does.
    /// </summary>
    /// <remarks>
    /// Matched with a simple case-sensitive equality; a blank or whitespace-only email is passed
    /// through to the query like any other value.
    /// </remarks>
    /// <param name="email">The candidate address. Neither trimmed nor cased by the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Employee?> GetEmployeeByEmailAsync(string? email, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the Compass employee for each of <paramref name="emails"/> that matches one, in a
    /// single query. Emails matching nothing are simply absent from the result.
    /// </summary>
    /// <param name="emails">The candidate addresses. Neither trimmed nor cased by the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Employee>> GetEmployeesByEmailAsync(
        IReadOnlyCollection<string?> emails,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every ACTIVE Compass employee, ordered by family then given name, in a single query.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Employee>> GetActiveEmployeesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns every active invoice frequency type, ordered by <c>TypeName</c> (FR-010). Inactive
    /// types are filtered out in the query itself, not by the caller.
    /// </summary>
    Task<IReadOnlyList<InvoiceFrequencyType>> GetInvoiceFrequenciesAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the Compass client with the given identifier, together with its derived status, or
    /// <c>null</c> when no such client exists (FR-005).
    /// </summary>
    /// <remarks>
    /// The status string is read directly off a stored column on the client row, updated whenever
    /// an assignment changes.
    /// </remarks>
    Task<(Client Client, string Status)?> GetClientAsync(
        int clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every Compass client with its derived status, ordered by client name.
    /// </summary>
    Task<IReadOnlyList<(Client Client, string Status)>> GetClientsAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Every billable time category for the given client, active and inactive alike (FR-009); no
    /// <c>IsActive</c> filter here — see <see cref="Contracts.CompassBillableCategoryDto"/>'s remarks.
    /// </summary>
    /// <param name="clientId">The owning client's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<BillableTimeCategory>> GetBillableCategoriesAsync(
        int clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the assignment with the given identifier, or <c>null</c> when no such assignment
    /// exists (FR-006).
    /// </summary>
    Task<ClientAssignment?> GetAssignmentAsync(int assignmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every assignment held by the given employee, ordered by <c>StartDate</c> (FR-006). No
    /// not-found case: an unknown or assignment-less employee simply has zero assignments.
    /// </summary>
    Task<IReadOnlyList<ClientAssignment>> GetAssignmentsByEmployeeAsync(
        int employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every assignment at the given client, ordered by <c>StartDate</c> (FR-006). No
    /// not-found case: an unknown or assignment-less client simply has zero assignments.
    /// </summary>
    Task<IReadOnlyList<ClientAssignment>> GetAssignmentsByClientAsync(
        int clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every SOW under the given assignment, ordered by <c>SowStartDate</c> (FR-007). No
    /// not-found case: an unknown or SOW-less assignment simply has zero SOWs.
    /// </summary>
    /// <param name="clientAssignmentId">The owning assignment's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Sow>> GetSowsByAssignmentAsync(
        int clientAssignmentId, CancellationToken cancellationToken);
}
