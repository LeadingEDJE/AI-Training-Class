using LeadingEDJE.Leap.Api.Platform.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;

/// <summary>
/// The outcome of an assignment or SOW create/update, carrying enough for the endpoint to choose a
/// status code without the service knowing anything about HTTP.
/// </summary>
/// <typeparam name="TDto">The row DTO returned on success.</typeparam>
/// <remarks>
/// Mirrors <see cref="LeadingEDJE.Leap.Api.Modules.Compass.Dtos.CompassLookupWrite{TDto}"/> — a result
/// carrier rather than exceptions for business outcomes (contracts/assignment-write-surface.md §5),
/// chosen partly because <c>scripts/check-coverage.sh</c> enforces 100% per-file coverage on new
/// backend code: every rejection path needs a test, and a result value is cheaper to drive than a
/// thrown exception. Unlike the lookup surface, this one distinguishes NotFound from a validation
/// failure from a conflict — an assignment/SOW write has all three (FR-004, FR-008, FR-019).
/// </remarks>
/// <param name="Status">What happened.</param>
/// <param name="Value">The written row, present only on success.</param>
/// <param name="Error">A message naming the problem, present only on failure.</param>
public sealed record CompassAssignmentWrite<TDto>(
    AdminMutationStatus Status,
    TDto? Value,
    string? Error
)
    where TDto : class
{
    /// <summary>The write succeeded.</summary>
    public static CompassAssignmentWrite<TDto> Succeeded(TDto value) =>
        new(AdminMutationStatus.Success, value, null);

    /// <summary>No assignment or SOW with that id exists (→ 404).</summary>
    public static CompassAssignmentWrite<TDto> NotFound() =>
        new(AdminMutationStatus.NotFound, null, null);

    /// <summary>
    /// The request failed a business rule the caller can fix by changing the request (→ 400) — e.g.
    /// FR-008's end-before-start rejection, or FR-003's inactive-EDJEr precondition.
    /// </summary>
    public static CompassAssignmentWrite<TDto> Invalid(string error) =>
        new(AdminMutationStatus.ValidationError, null, error);

    /// <summary>
    /// The request collides with existing data (→ 409) — e.g. FR-019's SOW overlap, from the service's
    /// pre-check or from <see cref="LeadingEDJE.Leap.Api.Modules.Compass.Services.CompassWriteFailure"/>.
    /// </summary>
    public static CompassAssignmentWrite<TDto> Conflict(string error) =>
        new(AdminMutationStatus.Conflict, null, error);
}
