using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Client configuration — AC-21, AC-22, AC-23.
/// </summary>
public interface ICompassClientService
{
    /// <summary>Every client, as the administration list renders them.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every client, ordered by name.</returns>
    Task<IReadOnlyList<ClientSummaryDto>> GetClientsAsync(CancellationToken cancellationToken);

    /// <summary>One client's full configuration record, including its billable time categories.</summary>
    /// <param name="id">The client's identity key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record, or null when no such client exists.</returns>
    Task<ClientDto?> GetClientAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a client, which is immediately available for assignment even at zero assignments (FR-022).
    /// </summary>
    /// <param name="request">The client's configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The created record, or a rejection: a conflict when the name is already in use, a validation
    /// failure when the name is missing or the invoice frequency is unknown or inactive.
    /// </returns>
    Task<CompassWrite<ClientDto>> CreateClientAsync(
        CompassClientRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>Updates a client's details, internal flag, and invoice-frequency default.</summary>
    /// <param name="id">The client to update.</param>
    /// <param name="request">The new configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated record, or a rejection. Never refused on account of derived status (FR-037).</returns>
    Task<CompassWrite<ClientDto>> UpdateClientAsync(
        int id,
        CompassClientRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>Adds a billable time category to a client.</summary>
    /// <param name="clientId">The owning client.</param>
    /// <param name="request">The category to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The created category, a not-found when the client does not exist, or a conflict when this client
    /// already offers that name — the same name on a different client is accepted (FR-025, FR-006).
    /// </returns>
    Task<CompassWrite<BillableTimeCategoryDto>> AddCategoryAsync(
        int clientId,
        CreateBillableTimeCategoryRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>Renames a billable time category and/or retires it.</summary>
    /// <param name="clientId">The owning client. A category belonging to another resolves to not-found.</param>
    /// <param name="categoryId">The category to update.</param>
    /// <param name="request">The new name and offered state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated category, or a rejection.</returns>
    Task<CompassWrite<BillableTimeCategoryDto>> UpdateCategoryAsync(
        int clientId,
        int categoryId,
        UpdateBillableTimeCategoryRequest request,
        CancellationToken cancellationToken
    );
}
