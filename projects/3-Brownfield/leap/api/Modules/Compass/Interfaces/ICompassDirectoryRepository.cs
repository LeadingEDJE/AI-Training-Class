namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Read-only data access for the Compass directory boundary.
/// </summary>
/// <remarks>
/// The bare name <c>Employee</c> here is Compass's own entity, <c>compass.employee</c>, built from the
/// Compass ERD — not the Timesheet module's directory type. This is the repository layer the OOTO
/// module never got: its services inject the database context and query it directly, which is exactly
/// the habit a module boundary exists to prevent. Compass gets the complete vertical slice so the
/// playbook documents one coherent pattern rather than a compromise. Do not use the OOTO module as a
/// structural template.
/// </remarks>
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
    /// Matched on <c>lower(btrim(email))</c>, both sides — the rule <c>ux_employee_email_ci</c>
    /// enforces, so matching more strictly would make a record that exists read as missing. It does not
    /// use that index: Npgsql renders <c>.Trim()</c> as the two-argument <c>btrim</c>, which Postgres
    /// treats as a different expression. Deliberate at this table's size; see the implementation's
    /// remarks. Fails closed on a blank key — a null, empty or whitespace-only email returns
    /// <c>null</c> without querying, because the one shape that must never happen is a blank key
    /// behaving as a wildcard.
    /// </remarks>
    /// <param name="email">The candidate address. Neither trimmed nor cased by the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Employee?> GetEmployeeByEmailAsync(string? email, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the Compass employee for each of <paramref name="emails"/> that matches one, in a
    /// single query. Emails matching nothing are simply absent from the result.
    /// </summary>
    /// <remarks>
    /// This exists so a caller resolving a list is not an N+1: its consumer is OOTO's
    /// employee-directory read, which projects ~100 rows, and asking
    /// <see cref="GetEmployeeByEmailAsync"/> per row is ~100 round trips. The filter is applied before
    /// any projection, and that is not a style choice: an email predicate applied inside a projection
    /// compiles to a per-row delegate EF cannot translate, so it passes against the in-memory provider
    /// and throws against PostgreSQL — only a real-PostgreSQL test sees it.
    /// Blank entries are discarded and duplicates collapse,
    /// since the match key is <c>lower(btrim(...))</c>.
    /// </remarks>
    /// <param name="emails">The candidate addresses. Neither trimmed nor cased by the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Employee>> GetEmployeesByEmailAsync(
        IReadOnlyCollection<string?> emails,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every ACTIVE Compass employee, ordered by family then given name, in a single query.
    /// </summary>
    /// <remarks>
    /// The unpaged directory list OOTO's accessible-employee and report reads walk in-process. Active
    /// only, matching TPS's <c>Db.ActiveEmployees()</c>. Orders entity columns before projecting, never
    /// a projected member, so the ordering translates on PostgreSQL.
    /// </remarks>
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
    /// The status is computed here through <see cref="IClientStatusDerivation.IsActive"/> — a scoped
    /// <c>AnyAsync</c> against the client's assignments, exactly like
    /// <c>CompassReadRepository.GetClientViewAsync</c> — never by comparing an end date directly.
    /// <c>ClientStatusSingleDerivationTests</c> fails the build on any other comparison, and
    /// <c>ClientStatusNonGatingTests</c> fails it on a new member added to
    /// <see cref="IClientStatusDerivation"/> instead of composing the existing ones.
    /// </remarks>
    Task<(Client Client, string Status)?> GetClientAsync(
        int clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every Compass client with its derived status, ordered by client name.
    /// </summary>
    /// <remarks>
    /// Derives status set-wise, in a fixed number of queries — two <c>Where</c>-scoped id projections
    /// over <see cref="IClientStatusDerivation.IsActive"/> and
    /// <see cref="IClientStatusDerivation.HasEverBeenAssigned"/>, then one read of the rows, exactly as
    /// <c>CompassReadRepository.GetClientDirectoryAsync</c> does. Calling
    /// <see cref="IClientStatusDerivation.Of"/> per row instead would be the N+1 that interface's own
    /// remarks forbid, and three queries stay three however many clients exist. The ordering is where
    /// the two part company: this one sorts in SQL under the Postgres collation and that one sorts in
    /// memory with <c>StringComparer.Ordinal</c>.
    /// </remarks>
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
    /// <remarks>
    /// Joins <see cref="Client"/> for the client name, and both
    /// <see cref="ClientAssignment.InvoiceFrequencyType"/> and <see cref="Client.InvoiceFrequencyType"/>
    /// for the <c>EffectiveInvoiceFrequency</c> precedence (FR-012). One of the highest-risk shapes in
    /// this feature for the projected-member trap: the DTO is
    /// constructed only after the query materializes, never inside the LINQ expression tree itself.
    /// </remarks>
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
