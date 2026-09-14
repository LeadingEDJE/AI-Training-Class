using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Platform.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Recording, adjusting and ending an EDJEr's engagement at a client. Compass's first audited write
/// path.
/// </summary>
public interface ICompassAssignmentService
{
    /// <summary>Every assignment.</summary>
    Task<IReadOnlyList<AssignmentRowDto>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>One assignment by id, or null (FR-004).</summary>
    Task<AssignmentRowDto?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an assignment. Rejects an inactive EDJEr (FR-003) or an end date before the start
    /// date (FR-008).
    /// </summary>
    Task<CompassAssignmentWrite<AssignmentRowDto>> CreateAsync(
        CreateAssignmentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adjusts or ends an assignment (FR-004, FR-008). Ending is a PUT that sets
    /// <c>EndDate</c> — the service detects the FIRST NULL-to-value transition (J14 step 5).
    /// </summary>
    Task<CompassAssignmentWrite<AssignmentRowDto>> UpdateAsync(
        int id,
        UpdateAssignmentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Permanently deletes an assignment AND every SOW under it — a true delete, not an end-date,
    /// for a record entered in error. Compass Super Admin only, on its own route group.
    /// </summary>
    /// <param name="id">The assignment to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="AdminMutationStatus.Success"/>, or <see cref="AdminMutationStatus.NotFound"/> when no such assignment exists.</returns>
    Task<AdminMutationStatus> DeleteAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// `GET /pickers/clients` — every client, never filtered by derived status (the O6
    /// named regression test, FR-009-FR-012, BR-11).
    /// </summary>
    Task<IReadOnlyList<ClientPickerRowDto>> GetClientPickersAsync(CancellationToken cancellationToken);

    /// <summary>`GET /pickers/edjers` — active EDJErs only (FR-003).</summary>
    Task<IReadOnlyList<EdjerPickerRowDto>> GetEdjerPickersAsync(CancellationToken cancellationToken);
}
