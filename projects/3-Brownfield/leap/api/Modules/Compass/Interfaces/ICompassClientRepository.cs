namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Data access for client configuration, including the client's billable time categories.
/// </summary>
public interface ICompassClientRepository
{
    /// <summary>
    /// Every client, with its invoicing cadence's name resolved.
    /// </summary>
    /// <param name="today">The business date status is derived against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Every client, its cadence name (or null when it has no default), and the two derived facts its
    /// status is named from.
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
    /// Loads the client's full assignment history into memory and evaluates the two facts there,
    /// per the approach documented in the client-status spike write-up.
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
    /// Returns false only when the id is missing entirely; a retired cadence still passes this check
    /// and is caught later by the selection list filter instead.
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
