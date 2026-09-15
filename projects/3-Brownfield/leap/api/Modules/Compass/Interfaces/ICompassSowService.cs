using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Platform.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Recording and editing contract periods (SOWs) under a client assignment, on the Ops-or-Super-Admin
/// application write surface (<c>contracts/sow-write-surface.md</c>).
/// </summary>
public interface ICompassSowService
{
    /// <summary>Every contract period under one assignment (FR-014).</summary>
    /// <param name="clientAssignmentId">The owning assignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SowRowDto>> GetByAssignmentIdAsync(
        int clientAssignmentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a contract period. Rejects <see cref="SowType.LegacyMigrated"/> (FR-016), a rate increase
    /// on anything but an extension (FR-017), an end before the start (FR-020), an overlap (FR-019).
    /// </summary>
    Task<CompassAssignmentWrite<SowRowDto>> CreateAsync(
        int clientAssignmentId,
        CreateSowRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Edits a contract period (FR-022). Same rejections as <see cref="CreateAsync"/>, with the
    /// overlap check excluding the period being edited.
    /// </summary>
    Task<CompassAssignmentWrite<SowRowDto>> UpdateAsync(
        int clientAssignmentId,
        int sowId,
        UpdateSowRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Permanently deletes ONE contract period — a true delete, not an end-date, for a period
    /// entered in error. Compass Super Admin only, on its own route group.
    /// </summary>
    /// <param name="clientAssignmentId">The owning assignment — an IDOR-style guard, matching <see cref="UpdateAsync"/>.</param>
    /// <param name="sowId">The SOW to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="AdminMutationStatus.Success"/>, or <see cref="AdminMutationStatus.NotFound"/> when no such SOW exists under that assignment.</returns>
    Task<AdminMutationStatus> DeleteAsync(
        int clientAssignmentId,
        int sowId,
        CancellationToken cancellationToken);
}
