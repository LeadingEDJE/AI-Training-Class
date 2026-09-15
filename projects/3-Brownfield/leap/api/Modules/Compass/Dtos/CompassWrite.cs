using Microsoft.AspNetCore.Http;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// The outcome of a Compass configuration write, in terms the service can express without knowing
/// anything about HTTP.
/// </summary>
/// <typeparam name="TDto">The DTO returned on success.</typeparam>
/// <param name="Status">What happened.</param>
/// <param name="Value">The written record, present only on success.</param>
/// <param name="Error">A message naming the problem, present only on failure.</param>
/// <param name="BlockingAssignments">
/// On <see cref="CompassWriteStatus.PreconditionFailed"/>, the assignments that must be end-dated first.
/// Absent otherwise.
/// </param>
public sealed record CompassWrite<TDto>(
    CompassWriteStatus Status,
    TDto? Value,
    string? Error,
    IReadOnlyList<BlockingAssignmentDto>? BlockingAssignments = null
)
    where TDto : class
{
    /// <summary>The write succeeded.</summary>
    public static CompassWrite<TDto> Succeeded(TDto value) =>
        new(CompassWriteStatus.Success, value, null);

    /// <summary>No record with that id exists.</summary>
    public static CompassWrite<TDto> NotFound() => new(CompassWriteStatus.NotFound, null, null);

    /// <summary>Another record already holds a value that must be unique.</summary>
    public static CompassWrite<TDto> Duplicate(string error) =>
        new(CompassWriteStatus.Conflict, null, error);

    /// <summary>The request was not well formed, or named something that cannot be chosen.</summary>
    public static CompassWrite<TDto> Invalid(string error) =>
        new(CompassWriteStatus.ValidationError, null, error);

    /// <summary>
    /// The caller may not perform this particular write, however well formed it is.
    /// </summary>
    /// <param name="error">A message naming what is not permitted.</param>
    /// <returns>A refused write.</returns>
    public static CompassWrite<TDto> Refused(string error) =>
        new(CompassWriteStatus.Forbidden, null, error);

    /// <summary>The request was well formed and authorised, but the state of the world forbids it.</summary>
    public static CompassWrite<TDto> Blocked(
        string error,
        IReadOnlyList<BlockingAssignmentDto> blockingAssignments
    ) => new(CompassWriteStatus.PreconditionFailed, null, error, blockingAssignments);
}

/// <summary>
/// What became of a Compass configuration write.
/// </summary>
public enum CompassWriteStatus
{
    /// <summary>The write succeeded.</summary>
    Success,

    /// <summary>The targeted row does not exist (→ 404).</summary>
    NotFound,

    /// <summary>A uniqueness collision was detected (→ 409).</summary>
    Conflict,

    /// <summary>The request failed validation, or named an unselectable value (→ 400).</summary>
    ValidationError,

    /// <summary>
    /// The request is well formed and authorised, but the current state of the data forbids it (→ 422).
    /// </summary>
    PreconditionFailed,

    /// <summary>
    /// The caller passed the route's authorization policy but may not perform THIS write (→ 403).
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="ValidationError"/> because the request IS malformed on its face — the
    /// same caller resubmitting an identical body later would still fail the schema check. Feature 014
    /// is its only current use.
    /// </remarks>
    Forbidden
}

/// <summary>
/// An assignment standing in the way of an EDJEr's deactivation.
/// </summary>
/// <param name="AssignmentId">The assignment's identity key.</param>
/// <param name="ClientId">The client the EDJEr is engaged at.</param>
/// <param name="ClientName">That client's name, so the message is actionable without a second lookup.</param>
/// <param name="StartDate">When the engagement began.</param>
public sealed record BlockingAssignmentDto(
    int AssignmentId,
    int ClientId,
    string ClientName,
    DateOnly StartDate
);

/// <summary>
/// Maps a non-success <see cref="CompassWriteStatus"/> to its HTTP result.
/// </summary>
public static class CompassWriteResults
{
    /// <summary>
    /// The HTTP error result for a non-success outcome: 404, 409, 422, or 400.
    /// </summary>
    /// <remarks>
    /// 422 and 400 are interchangeable from the caller's perspective; both mean the request needs to
    /// change before resubmission. This mapping keeps them merged wherever the platform's shared
    /// mutation-status vocabulary is reused.
    /// </remarks>
    /// <param name="status">The non-success outcome.</param>
    /// <param name="error">A message naming the problem.</param>
    /// <param name="blockingAssignments">The assignments to end first, on a 422.</param>
    public static IResult ToErrorResult(
        this CompassWriteStatus status,
        string? error,
        IReadOnlyList<BlockingAssignmentDto>? blockingAssignments = null
    ) =>
        status switch
        {
            CompassWriteStatus.NotFound => Results.NotFound(),
            CompassWriteStatus.Conflict => Results.Conflict(new { message = error }),
            CompassWriteStatus.PreconditionFailed => Results.UnprocessableEntity(
                new { message = error, blockingAssignments = blockingAssignments ?? [] }
            ),
            CompassWriteStatus.Forbidden => Results.Json(
                new { message = error },
                statusCode: StatusCodes.Status403Forbidden
            ),
            _ => Results.BadRequest(new { message = error }),
        };
}
