using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// May this EDJEr be deactivated, and if not, which assignments block it (FR-040..FR-045).
/// </summary>
/// <remarks>
/// One question, one answer, and deliberately nothing else. The guard never ends an assignment: FR-042
/// forbids auto-ending under any circumstance, because an engagement's true end date frequently
/// differs from the date somebody happened to attempt a deactivation. It reports; the operator acts.
/// It is a service rather than inline logic because it has two callers — the write path, and the
/// read-only blockers route the EDJEr edit form queries before offering the toggle — and two
/// independent derivations of "is this EDJEr deactivatable" would drift. The condition is the absence
/// of an end date, never a date comparison (FR-045): a future end date does not block, or a confirmed
/// rollout would deadlock, and this keeps the guard clear of the currency-derivation gate.
/// </remarks>
public interface IEdjerDeactivationGuard
{
    /// <summary>
    /// Evaluates the EDJEr with this id against the AC-19 precondition, loading them first.
    /// </summary>
    /// <remarks>
    /// For a caller that holds only an identifier — the blockers route. A caller that has already
    /// loaded the EDJEr should use <see cref="EvaluateAsync(Employee, CancellationToken)"/> instead
    /// and save the round trip.
    /// </remarks>
    /// <param name="employeeId">The EDJEr being considered for deactivation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The verdict, carrying the blocking assignments when — and only when — it is
    /// <see cref="EdjerDeactivationStatus.Blocked"/>. <see cref="EdjerDeactivationStatus.NotFound"/>
    /// when no EDJEr holds that id.
    /// </returns>
    Task<EdjerDeactivationVerdict> EvaluateAsync(
        int employeeId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Evaluates an already-loaded EDJEr against the AC-19 precondition.
    /// </summary>
    /// <remarks>
    /// The overload the write path uses: <c>CompassEmployeeService</c> has the EDJEr in hand before it
    /// decides anything, and re-reading it by id there buys nothing. Pass the entity as it was read,
    /// before applying the request: the verdict turns on the EDJEr's current
    /// <see cref="Employee.IsActive"/> — AC-19 guards the transition — so an instance already mutated
    /// to the requested value would report <see cref="EdjerDeactivationStatus.AlreadyInactive"/> and
    /// wave every blocker through. Never returns <see cref="EdjerDeactivationStatus.NotFound"/>: the
    /// caller has the EDJEr.
    /// </remarks>
    /// <param name="employee">The EDJEr being considered for deactivation, as currently stored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verdict, carrying the blocking assignments only when it is Blocked.</returns>
    Task<EdjerDeactivationVerdict> EvaluateAsync(
        Employee employee,
        CancellationToken cancellationToken
    );
}
