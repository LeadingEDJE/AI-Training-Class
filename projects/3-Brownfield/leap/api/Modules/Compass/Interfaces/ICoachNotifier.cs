namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Tells an EDJEr's coach that their engagement changed — once, ever, per entity.
/// </summary>
/// <remarks>
/// Call these only after the triggering write has committed (contract §6): a failed write must not
/// send a real email, and an email cannot be recalled. Ordering is the caller's responsibility, because
/// only the caller knows whether its own commit succeeded. Neither method throws for an outcome the
/// caller could not act on — no coach is a normal case, not an error (FR-034), and a delivery failure
/// does not throw either; both are recorded in the notification log with distinguishable statuses
/// (FR-035). Dispatch is <c>IEmailSender</c>, never <c>INotificationService</c>: that
/// service resolves a channel preference from a Timesheet directory type (Principle V,
/// <c>DirectoryConsumerBoundaryTests</c>) and could divert a message AC-43 requires be mailed.
/// </remarks>
public interface ICoachNotifier
{
    /// <summary>
    /// An assignment's end date was set for the first time ever (FR-025, AC-29).
    /// </summary>
    /// <remarks>
    /// Idempotent per assignment. Calling it again — for a later edit, or after the end date was cleared
    /// and set once more — sends nothing, because "first time" means once ever rather than once per
    /// transition.
    /// </remarks>
    /// <param name="assignmentId">The assignment that was end-dated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifyAssignmentEndedAsync(int assignmentId, CancellationToken cancellationToken);

    /// <summary>
    /// An extension contract period was initially added (FR-026, AC-32).
    /// </summary>
    /// <remarks>
    /// Only for <c>SowType.SowExtension</c>. An <c>InitialContract</c> notifies nobody (FR-028, AC-31) —
    /// that is the engagement starting, not changing — and the caller decides which it was.
    /// </remarks>
    /// <param name="sowId">The contract period that was added.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifySowExtensionAddedAsync(int sowId, CancellationToken cancellationToken);
}
