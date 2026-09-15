using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>Data access for <see cref="Sow"/>.</summary>
public interface ICompassSowRepository
{
    /// <summary>Every SOW period under one assignment, ordered by start date, oldest first.</summary>
    /// <param name="clientAssignmentId">The owning assignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Sow>> GetByAssignmentIdAsync(
        int clientAssignmentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The tracked SOW with this id, or null. Tracked deliberately, matching
    /// <see cref="ICompassAssignmentRepository.GetByIdAsync"/> — the service mutates what it returns.
    /// </summary>
    /// <param name="id">The SOW's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Sow?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Stages a new SOW for insertion. Does not persist.</summary>
    /// <param name="sow">The SOW to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(Sow sow, CancellationToken cancellationToken);

    /// <summary>
    /// Stages a TRACKED SOW for permanent removal. Does not persist — the caller commits through
    /// <c>IAuditService.LogAsync</c>, as every Compass write does.
    /// </summary>
    /// <param name="sow">The tracked SOW to remove, as returned by <see cref="GetByIdAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAsync(Sow sow, CancellationToken cancellationToken);

    /// <summary>
    /// The periods on the same assignment whose dates intersect the candidate range — FR-019's overlap
    /// pre-check. Compares against siblings, never today, so it misses the currency gate.
    /// </summary>
    /// <param name="clientAssignmentId">The assignment the candidate period belongs to.</param>
    /// <param name="candidateStartDate">The candidate period's start date.</param>
    /// <param name="candidateEndDate">The candidate period's end date.</param>
    /// <param name="excludingSowId">
    /// The SOW being edited, excluded so a period does not collide with itself on update. Null when
    /// creating.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Sow>> GetOverlappingAsync(
        int clientAssignmentId,
        DateOnly candidateStartDate,
        DateOnly candidateEndDate,
        int? excludingSowId,
        CancellationToken cancellationToken);

    /// <summary>Whether a client assignment with this identifier exists.</summary>
    /// <param name="clientAssignmentId">The assignment identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the assignment exists.</returns>
    /// <remarks>
    /// Checked after the insert attempt, to surface the underlying foreign-key violation message
    /// directly to the caller.
    /// </remarks>
    Task<bool> AssignmentExistsAsync(int clientAssignmentId, CancellationToken cancellationToken);

    /// <summary>Every contract period.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All periods, ordered by identifier.</returns>
    Task<IReadOnlyList<CompassSowDto>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>One contract period.</summary>
    /// <param name="id">The period identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The period, or <c>null</c> when none has that identifier.</returns>
    Task<CompassSowDto?> GetAsync(int id, CancellationToken cancellationToken);
}
