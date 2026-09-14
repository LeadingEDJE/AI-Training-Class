using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Records which coach notices a Compass service asked for, without sending anything (US4/#68).
/// </summary>
/// <remarks>
/// <para>
/// Shared by <c>CompassAssignmentServiceTests</c> and <c>CompassSowServiceTests</c> rather than
/// duplicated in both: they need identical behaviour, and the point of the double is to let each assert
/// that its service asked for the notice at the right moment and NOT at the wrong ones — which is a claim
/// about the caller, not about the notifier.
/// </para>
/// <para>
/// What the notice CONTAINS is asserted against real PostgreSQL and a real outbox in
/// <c>tests/integration/Compass/CoachNotificationTests</c>. Composing a message here would only prove
/// this class composes it the way it was written.
/// </para>
/// </remarks>
public sealed class RecordingCoachNotifier : ICoachNotifier
{
    /// <summary>Assignment ids the service asked to notify about, in order.</summary>
    public List<int> AssignmentsEnded { get; } = [];

    /// <summary>SOW ids the service asked to notify about, in order.</summary>
    public List<int> SowExtensionsAdded { get; } = [];

    /// <summary>Runs at the start of every notice, so a test can observe WHEN it was asked for.</summary>
    /// <remarks>
    /// The service-level claim this exists for is not "the notice happened" but "the notice happened
    /// after the caller's atomic scope closed" (contract §6). That is a statement about the moment of
    /// the call, so it can only be observed from inside the call.
    /// </remarks>
    public Action? OnNotify { get; set; }

    /// <inheritdoc />
    public Task NotifyAssignmentEndedAsync(int assignmentId, CancellationToken cancellationToken)
    {
        OnNotify?.Invoke();
        AssignmentsEnded.Add(assignmentId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifySowExtensionAddedAsync(int sowId, CancellationToken cancellationToken)
    {
        OnNotify?.Invoke();
        SowExtensionsAdded.Add(sowId);
        return Task.CompletedTask;
    }
}
