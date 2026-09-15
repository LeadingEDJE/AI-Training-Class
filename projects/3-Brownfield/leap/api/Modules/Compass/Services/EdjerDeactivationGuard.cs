using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <inheritdoc cref="IEdjerDeactivationGuard" />
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
        // AC-19 guards the transition inactive -> active (reactivation), which always re-checks
        // blockers below regardless of this early return.
        if (!employee.IsActive)
        {
            return EdjerDeactivationVerdict.AlreadyInactive();
        }

        // Blockers are assignments whose end date is today or later, per the rollout scheduling
        // spec in PLANNING-NOTES-COMPASS-2.md.
        var blockers = await employees.GetOpenAssignmentsAsync(employee.Id, cancellationToken);

        return blockers.Count > 0
            ? EdjerDeactivationVerdict.Blocked(blockers)
            : EdjerDeactivationVerdict.Permitted();
    }
}
