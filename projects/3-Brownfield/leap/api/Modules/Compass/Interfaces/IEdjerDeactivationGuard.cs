using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// May this EDJEr be deactivated, and if not, which assignments block it (FR-040..FR-045).
/// </summary>
public interface IEdjerDeactivationGuard
{
    /// <summary>
    /// Evaluates the EDJEr with this id against the AC-19 precondition, loading them first.
    /// </summary>
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
    /// Pass the entity after applying the requested change — the verdict is evaluated against the
    /// EDJEr's proposed new state rather than its current stored state.
    /// </remarks>
    /// <param name="employee">The EDJEr being considered for deactivation, as currently stored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verdict, carrying the blocking assignments only when it is Blocked.</returns>
    Task<EdjerDeactivationVerdict> EvaluateAsync(
        Employee employee,
        CancellationToken cancellationToken
    );
}
