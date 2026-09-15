using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Platform.Domain;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// The reads and the log write a coach notice needs.
/// </summary>
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
    /// Keyed on the notification type together with the period week, per the once-per-week rule.
    /// </remarks>
    /// <param name="notificationType">The namespaced, entity-bearing type string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasNotifiedAsync(string notificationType, CancellationToken cancellationToken);

    /// <summary>Stages a notification log row. Does not persist.</summary>
    /// <param name="log">The row to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddLogAsync(NotificationLog log, CancellationToken cancellationToken);
}
