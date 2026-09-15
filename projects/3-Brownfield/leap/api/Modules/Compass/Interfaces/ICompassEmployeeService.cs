using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// EDJEr configuration — AC-17, AC-18, AC-19, BR-9, BR-10.
/// </summary>
public interface ICompassEmployeeService
{
    /// <summary>Every EDJEr, active and inactive, as the administration list renders them.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every EDJEr, ordered by family then given name.</returns>
    Task<IReadOnlyList<CompassEdjerSummaryDto>> GetEdjersAsync(CancellationToken cancellationToken);

    /// <summary>One EDJEr's full configuration record.</summary>
    /// <param name="id">The EDJEr's identity key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record, or null when no such EDJEr exists.</returns>
    Task<CompassEdjerDto?> GetEdjerAsync(int id, CancellationToken cancellationToken);

    /// <summary>Adds an EDJEr, who becomes selectable across Compass on save (FR-011).</summary>
    /// <param name="request">The EDJEr's configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The created record, or a rejection: a conflict when the email is already held by any EDJEr active
    /// or inactive, a validation failure when a field is missing, the state is not one of the 50 plus DC,
    /// or the employee type is unknown or inactive.
    /// </returns>
    Task<CompassWrite<CompassEdjerDto>> CreateEdjerAsync(
        CompassEdjerRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>Updates an EDJEr's profile, classification, coach, state, and time-tracking flags.</summary>
    /// <param name="id">The EDJEr to update.</param>
    /// <param name="request">The new configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The updated record, or a rejection. Beyond the create path's rejections, this one can also refuse a
    /// deactivation: turning the active flag off while the EDJEr holds an assignment with no end
    /// date is a precondition failure naming those assignments, and the EDJEr stays active (FR-019,
    /// BR-10). No assignment is ever auto-ended.
    /// </returns>
    Task<CompassWrite<CompassEdjerDto>> UpdateEdjerAsync(
        int id,
        CompassEdjerRequest request,
        CancellationToken cancellationToken
    );
}
