using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>Data access for <see cref="ClientAssignment"/>.</summary>
/// <remarks>
/// Module-owned per the new-module repository rule (Option 2):
/// Compass owns its own repositories rather than extending the Platform-owned pattern the Timesheet
/// module is grandfathered into. Reaches the entity through
/// <c>context.Set&lt;ClientAssignment&gt;()</c>, never a <c>DbSet</c> property — <c>LeapDbContext</c>
/// declares none for any Compass entity. No method persists: <c>SaveChangesAsync</c> belongs to the
/// service layer, reached through <c>IAuditService.LogAsync</c> for an audited write (Principle III),
/// and <c>tests/unit/Data/CompassAssignmentRepositoryTests.cs</c> fails the build if this class
/// contains that token.
/// </remarks>
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
    /// The eager-loaded <c>Client</c> is part of the contract, not an incidental detail.
    /// <c>CompassSowService.CreateAsync</c> refuses a new contract period under an internal ("beach")
    /// client by reading <c>Client.IsInternal</c> off this navigation, and
    /// <c>CompassAssignmentService.ToDto</c> projects the same flag onto the assignment row. Dropping
    /// the <c>Include</c> would make both fall back to "not internal" — the guard would stop refusing,
    /// and no test over a hand-written double would notice, because a double populates whatever it
    /// likes.
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
    /// <remarks>
    /// Remove this assignment's SOWs first: the FK is <c>DeleteBehavior.Restrict</c>. See
    /// <c>CompassAssignmentService.DeleteAsync</c>.
    /// </remarks>
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
    /// This is not what the AC-19 deactivation guard reads. <see cref="IEdjerDeactivationGuard"/> is
    /// built on <see cref="ICompassEmployeeRepository.GetOpenAssignmentsAsync"/> instead: that one
    /// projects straight to <c>BlockingAssignmentDto</c> with the client name FR-041 requires, and it
    /// carries the two-step ordering fix for the projected-member translation failure.
    /// This method returns entities and has no production
    /// consumer. Do not wire a second deactivation derivation to it — one question, one answer, which
    /// is the entire reason the guard is a service. For "may this EDJEr be deactivated", call the
    /// guard.
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
    /// <remarks>
    /// One question, not two: unknown and retired get the same answer from the service, because the
    /// caller can act on neither differently. Mirrors
    /// <c>ICompassClientRepository.ActiveInvoiceFrequencyTypeExistsAsync</c>, which asks the same thing
    /// of the same table for the CLIENT-level default — duplicated rather than shared because a
    /// module-owned repository does not take a dependency on another aggregate's repository.
    /// </remarks>
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
