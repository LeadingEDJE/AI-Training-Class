using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>Data access for <see cref="ClientAssignment"/>.</summary>
public interface ICompassAssignmentRepository
{
    /// <summary>Every assignment, with its EDJEr, client and SOWs loaded for projection.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ClientAssignment>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The tracked assignment with this id, with its EDJEr, client, invoice-frequency type and SOWs
    /// loaded for projection, or null. Tracked because the service mutates what it returns and saves.
    /// </summary>
    /// <remarks>
    /// The <c>Client</c> navigation is loaded lazily here; callers needing the internal-client flag
    /// should query it separately.
    /// </remarks>
    /// <param name="id">The assignment's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClientAssignment?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Stages a new assignment for insertion. Does not persist.</summary>
    /// <param name="assignment">The assignment to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(ClientAssignment assignment, CancellationToken cancellationToken);

    /// <summary>
    /// Stages a TRACKED assignment for permanent removal. Does not persist — the caller commits
    /// through <c>IAuditService.LogAsync</c>, as every Compass write does.
    /// </summary>
    /// <param name="assignment">
    /// The tracked assignment to remove, as returned by <see cref="GetByIdAsync"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAsync(ClientAssignment assignment, CancellationToken cancellationToken);

    /// <summary>
    /// This EDJEr's open assignments — the ones with no end date. The condition is the absence of an
    /// end date, not a date comparison, so it does not trip the currency-derivation gate.
    /// </summary>
    /// <remarks>
    /// This is the method backing the AC-19 deactivation guard's check.
    /// </remarks>
    /// <param name="employeeId">The EDJEr being considered for deactivation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ClientAssignment>> GetOpenAssignmentsByEmployeeAsync(
        int employeeId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The EDJEr behind FR-003's active-EDJEr precondition, or null if the id does not exist.
    /// </summary>
    /// <param name="employeeId">The EDJEr's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Employee?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether an invoice frequency type exists and is currently selectable (FR-038, AC-26).
    /// </summary>
    /// <param name="invoiceFrequencyTypeId">The cadence to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the type exists and is active.</returns>
    Task<bool> ActiveInvoiceFrequencyTypeExistsAsync(
        int invoiceFrequencyTypeId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The client behind <c>CreateAsync</c>'s existence precondition, or null. Unlike
    /// <see cref="GetEmployeeAsync"/> there is no active gate: an inactive client is a valid target.
    /// </summary>
    /// <param name="clientId">The client's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Client?> GetClientAsync(int clientId, CancellationToken cancellationToken);

    /// <summary>
    /// `GET /pickers/clients` — every client, status derived set-wise, never filtered: a client with
    /// zero assignments derives Inactive and must still appear (O6, FR-009-FR-012, BR-11, AC-NFR-4).
    /// </summary>
    /// <param name="today">The business date the derivation evaluates against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ClientPickerRowDto>> GetClientPickerRowsAsync(
        DateOnly today,
        CancellationToken cancellationToken);

    /// <summary>`GET /pickers/edjers` — active EDJErs only (FR-003).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<EdjerPickerRowDto>> GetActiveEdjerPickerRowsAsync(
        CancellationToken cancellationToken);
}
