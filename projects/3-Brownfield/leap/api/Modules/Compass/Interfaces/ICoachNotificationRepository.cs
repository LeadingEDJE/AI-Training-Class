using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Platform.Domain;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// The reads and the log write a coach notice needs.
/// </summary>
/// <remarks>
/// Module-owned even though <see cref="NotificationLog"/> is a platform entity, and deliberately not a
/// widening of <c>INotificationLogRepository</c>: the query shape Compass needs is one that interface
/// cannot express, see <see cref="HasNotifiedAsync"/>. No method persists except where its name says
/// so — <c>SaveChangesAsync</c> belongs to the service layer, reached through
/// <c>ICompassUnitOfWork</c> (Principle III).
/// </remarks>
public interface ICoachNotificationRepository
{
    /// <summary>
    /// The facts behind an assignment-end notice, or null if the assignment is gone.
    /// </summary>
    /// <param name="assignmentId">The assignment that was end-dated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CoachNotificationFacts?> GetAssignmentEndFactsAsync(
        int assignmentId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// The facts behind an extension-SOW notice, or null if the period is gone.
    /// </summary>
    /// <param name="sowId">The contract period that was added.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CoachNotificationFacts?> GetSowExtensionFactsAsync(
        int sowId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Whether this exact notification has already been recorded — the once-ever check (FR-027).
    /// </summary>
    /// <remarks>
    /// Keyed on the notification type alone, and never on <c>PeriodWeekStart</c>. That is why this
    /// cannot be <c>INotificationLogRepository.GetByIdempotencyKeyAsync</c>, which takes the week as part
    /// of its key: two assignments for one EDJEr can end on the same day, and keying on the week would
    /// collapse them into a single notice (contract §4, FR-027b, SC-014). The entity identity travels
    /// inside <paramref name="notificationType"/> instead, which makes the type alone exact.
    /// </remarks>
    /// <param name="notificationType">The namespaced, entity-bearing type string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasNotifiedAsync(string notificationType, CancellationToken cancellationToken);

    /// <summary>Stages a notification log row. Does not persist.</summary>
    /// <param name="log">The row to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddLogAsync(NotificationLog log, CancellationToken cancellationToken);
}
