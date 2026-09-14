namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Data access for client configuration, including the client's billable time categories.
/// </summary>
/// <remarks>
/// Lives inside the module per Principle III's new-module rule; only the entity-agnostic machinery
/// stays in <c>api/Platform/</c>. It deliberately does not derive from <c>IRepository&lt;T&gt;</c>:
/// that contract carries <c>DeleteAsync</c>, and neither a client nor a category is ever removed —
/// assignments reference clients, timesheet records reference categories, and AC-23 makes deactivation
/// the only retirement path (Principle VIII), as for <see cref="ICompassEmployeeRepository"/> and
/// <see cref="ICompassLookupRepository{TLookup}"/>. Categories are reached through their client, never
/// by bare id, so an id belonging to another client resolves to nothing. No method persists; that
/// boundary is the service's (Principle III), reached through <see cref="ICompassUnitOfWork"/>.
/// </remarks>
public interface ICompassClientRepository
{
    /// <summary>
    /// Every client, with its invoicing cadence's name resolved.
    /// </summary>
    /// <remarks>
    /// Ordered by name, which is how the list presents them. The cadence name is resolved in this one
    /// query rather than per row — the p95 target (AC-NFR-4) is what rules out the N+1 — and through a
    /// LEFT join, because the default is optional and an inner join would silently drop every client
    /// without one.
    /// </remarks>
    /// <param name="today">
    /// The business date status is derived against, supplied by the caller so every row of one
    /// response is judged against the same day — a list that re-read the clock per row could straddle
    /// midnight and contradict itself.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Every client, its cadence name (or null when it has no default), and the two derived facts its
    /// status is named from. Both are raw predicates, not status values: naming
    /// <c>ClientStatus.Active</c> is the derivation's job alone (BR-11), so the caller maps the pair
    /// through <c>IClientStatusDerivation.StatusOfClient</c>. <c>HasEverBeenAssigned</c> is what
    /// separates a never-engaged client (Inactive) from one we have stopped working with (Former).
    /// </returns>
    Task<
        IReadOnlyList<(
            Client Client,
            string? InvoiceFrequencyTypeName,
            bool HoldsCurrentAssignment,
            bool HasEverBeenAssigned
        )>
    > GetAllWithFrequencyNameAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>
    /// The two derived facts one client's status is named from — the detail view's status input.
    /// </summary>
    /// <remarks>
    /// Asks the database rather than loading the assignments and evaluating in memory. The per-client
    /// shape <c>IClientStatusDerivation.Of</c> compiles its expressions and runs them over
    /// <c>Client.ClientAssignments</c>, so a caller that forgot to <c>Include</c> that collection gets a
    /// confident, silent "Inactive" for every client. Existence in SQL has no such failure mode, and it
    /// does not drag a client's whole assignment history into a configuration read. Both facts are
    /// returned together rather than as two methods, so a caller cannot derive one and forget the other
    /// and silently collapse Former back into Inactive.
    /// </remarks>
    /// <param name="clientId">The client to test.</param>
    /// <param name="today">The business date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Whether at least one assignment is current, and whether any assignment has ever existed.
    /// </returns>
    Task<(bool HoldsCurrentAssignment, bool HasEverBeenAssigned)> GetStatusFactsAsync(
        int clientId,
        DateOnly today,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// One client by id with its billable time categories loaded, tracked so the service can mutate
    /// and save.
    /// </summary>
    /// <param name="id">The client's identity key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The client, or null when no such row exists.</returns>
    Task<Client?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Whether any other client already holds <paramref name="clientName"/>.</summary>
    /// <remarks>
    /// Compared trimmed and ordinally, which is exactly what <c>ux_client_client_name</c> indexes —
    /// deliberately not case-insensitively. A pre-check stricter than the index would refuse a name the
    /// database would have accepted, and, worse, the index could then not be the guard for the case the
    /// pre-check invented. This is a pre-check so the rejection can name the field; the index is the
    /// guard, because a concurrent writer can commit between this call and the write.
    /// </remarks>
    /// <param name="clientName">The name to test.</param>
    /// <param name="excludingId">The row being edited, so it does not collide with itself.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the name is already in use.</returns>
    Task<bool> ClientNameExistsAsync(
        string clientName,
        int? excludingId,
        CancellationToken cancellationToken
    );

    /// <summary>Whether an invoice frequency type exists and is currently selectable.</summary>
    /// <remarks>
    /// One question, not two, because the server's answer is the same either way — a 400. The selection
    /// list omitting inactive cadences is convenience; this is the enforcement (FR-023, FR-041).
    /// </remarks>
    /// <param name="invoiceFrequencyTypeId">The cadence to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the type exists and is active.</returns>
    Task<bool> ActiveInvoiceFrequencyTypeExistsAsync(
        int invoiceFrequencyTypeId,
        CancellationToken cancellationToken
    );

    /// <summary>One of a client's categories, tracked so the service can mutate and save.</summary>
    /// <param name="clientId">The owning client — part of the lookup, not a hint.</param>
    /// <param name="categoryId">The category's identity key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The category, or null when it does not exist OR belongs to another client.</returns>
    Task<BillableTimeCategory?> GetCategoryAsync(
        int clientId,
        int categoryId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Whether this client already offers a category by that name, compared case-insensitively.
    /// </summary>
    /// <remarks>
    /// Scoped to one client, which is the whole rule (FR-025): the same name on a different client is
    /// accepted, and is the ordinary case rather than the exceptional one.
    /// </remarks>
    /// <param name="clientId">The owning client.</param>
    /// <param name="categoryName">The name to test.</param>
    /// <param name="excludingId">The category being edited, so it does not collide with itself.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when this client already offers that name.</returns>
    Task<bool> CategoryNameExistsAsync(
        int clientId,
        string categoryName,
        int? excludingId,
        CancellationToken cancellationToken
    );

    /// <summary>Stages a new client. Does not persist.</summary>
    /// <param name="client">The client to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(Client client, CancellationToken cancellationToken);

    /// <summary>Stages a new billable time category. Does not persist.</summary>
    /// <param name="category">The category to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddCategoryAsync(BillableTimeCategory category, CancellationToken cancellationToken);
}
