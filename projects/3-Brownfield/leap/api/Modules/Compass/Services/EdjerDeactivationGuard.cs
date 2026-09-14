using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <inheritdoc cref="IEdjerDeactivationGuard" />
/// <remarks>
/// The intended second caller is the EDJEr edit form, which does not exist yet — the seam that must
/// be tracked, tracked here. Today the guard has two callers:
/// <see cref="CompassEmployeeService"/>'s update path, which enforces it, and
/// <c>GET /api/compass/assignments/blockers/{employeeId}</c>, which reports it. The form will be the
/// third, calling the route before it offers the active toggle so the operator sees what to end-date
/// instead of meeting a 422 after committing to the action. Only the server side is delivered, so the
/// full deactivation loop cannot be signed off until that form exists.
/// </remarks>
public class EdjerDeactivationGuard(ICompassEmployeeRepository employees) : IEdjerDeactivationGuard
{
    /// <inheritdoc />
    public async Task<EdjerDeactivationVerdict> EvaluateAsync(
        int employeeId,
        CancellationToken cancellationToken
    )
    {
        var employee = await employees.GetByIdAsync(employeeId, cancellationToken);

        return employee is null
            ? EdjerDeactivationVerdict.NotFound()
            : await EvaluateAsync(employee, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EdjerDeactivationVerdict> EvaluateAsync(
        Employee employee,
        CancellationToken cancellationToken
    )
    {
        // AC-19 guards the transition active -> inactive. An already-inactive EDJEr is not toggling
        // anything, so the question does not arise, and answering Permitted would tell the edit form a
        // deactivation is available when there is nothing left to deactivate. The blockers are not read
        // in this state: it costs a query to report assignments nobody can act on, and inviting someone
        // to end them for the wrong reason is worse than not showing them.
        if (!employee.IsActive)
        {
            return EdjerDeactivationVerdict.AlreadyInactive();
        }

        // The condition is the ABSENCE of an end date, not a date comparison (FR-045) -- the repository's
        // predicate is `end_date IS NULL`. A future end date is therefore not a blocker, which is what
        // keeps a confirmed rollout from deadlocking, and it keeps this path clear of the R-1
        // currency-derivation gate.
        var blockers = await employees.GetOpenAssignmentsAsync(employee.Id, cancellationToken);

        return blockers.Count > 0
            ? EdjerDeactivationVerdict.Blocked(blockers)
            : EdjerDeactivationVerdict.Permitted();
    }
}
