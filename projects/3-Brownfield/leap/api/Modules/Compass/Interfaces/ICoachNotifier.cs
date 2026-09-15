namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Tells an EDJEr's coach that their engagement changed — once, ever, per entity.
/// </summary>
public interface ICoachNotifier
{
    /// <summary>
    /// An assignment's end date was set for the first time ever (FR-025, AC-29).
    /// </summary>
    /// <remarks>
    /// Sends once per transition — clearing and re-setting the end date sends a fresh notice each time.
    /// </remarks>
    /// <param name="assignmentId">The assignment that was end-dated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifyAssignmentEndedAsync(int assignmentId, CancellationToken cancellationToken);

    /// <summary>
    /// An extension contract period was initially added (FR-026, AC-32).
    /// </summary>
    /// <param name="sowId">The contract period that was added.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifySowExtensionAddedAsync(int sowId, CancellationToken cancellationToken);
}
