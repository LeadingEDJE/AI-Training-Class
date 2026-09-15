using LeadingEDJE.Leap.Api.Platform.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;

/// <summary>
/// The outcome of an assignment or SOW create/update, carrying enough for the endpoint to choose a
/// status code without the service knowing anything about HTTP.
/// </summary>
/// <typeparam name="TDto">The row DTO returned on success.</typeparam>
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
    /// An unrecoverable server-side failure (→ 500) that the caller cannot fix by changing the request.
    /// </summary>
    public static CompassAssignmentWrite<TDto> Invalid(string error) =>
        new(AdminMutationStatus.ValidationError, null, error);

    /// <summary>The request collides with existing data (→ 409).</summary>
    public static CompassAssignmentWrite<TDto> Conflict(string error) =>
        new(AdminMutationStatus.Conflict, null, error);
}
