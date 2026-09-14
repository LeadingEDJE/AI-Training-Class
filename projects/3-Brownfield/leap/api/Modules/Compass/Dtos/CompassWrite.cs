using Microsoft.AspNetCore.Http;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// The outcome of a Compass configuration write, in terms the service can express without knowing
/// anything about HTTP.
/// </summary>
/// <remarks>
/// This exists next to the platform's <c>AdminMutationStatus</c> rather than as a new member on it:
/// that enum is shared with four Timesheet admin endpoint groups and its <c>ToErrorResult</c> maps
/// anything it does not recognise to <c>400</c>, so a <c>PreconditionFailed</c> member there would
/// make a Compass 422 silently answer 400 and would give Timesheet a status it can never return. The
/// 422 case is genuinely Compass's — AC-19's deactivation guard — so the vocabulary is Compass's.
/// Named for the module rather than for EDJErs because the client service is its second consumer;
/// converging <see cref="CompassLookupWrite{TDto}"/> onto it is a later refactor-only change, which
/// must not share a commit with behaviour (Principle VI).
/// </remarks>
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

    /// <summary>
    /// The request was well formed and authorised, but the state of the world forbids it — and the
    /// response says what has to change first.
    /// </summary>
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
    /// Distinct from <see cref="ValidationError"/> because the request is not malformed — a different
    /// caller sending the identical body would succeed. Feature 010's only use is the
    /// <c>LegacyMigrated</c> contract-period type, which Principle VIII restricts to the migration
    /// principal; a Compass Super Admin sending it is refused on identity, not on content.
    /// </remarks>
    Forbidden
}

/// <summary>
/// An assignment standing in the way of an EDJEr's deactivation.
/// </summary>
/// <remarks>
/// AC-19 requires the refusal to identify the assignments that must be end-dated first, so it carries
/// the client's name as well as its id: an id alone is not actionable by a human. No assignment is
/// ever auto-ended (AC-19: the true end date frequently differs from the deactivation date), and the
/// recovery path — the Super Admin ends them and retries — belongs to the assignment surface (AC-45,
/// spec A-4). This DTO reports; it does not imply Compass will act.
/// </remarks>
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
/// <remarks>
/// Lives in the Compass module rather than in <c>api/Platform/</c> because the 422 arm is Compass's own
/// requirement. It mirrors the platform's <c>AdminMutationStatusResults</c> shape so the two read
/// alike — every endpoint keeps its bespoke success arm and delegates every failure here.
/// </remarks>
public static class CompassWriteResults
{
    /// <summary>
    /// The HTTP error result for a non-success outcome: 404, 409, 422, or 400.
    /// </summary>
    /// <remarks>
    /// 422 is distinguished from 400 deliberately. A 400 tells the caller their request was
    /// wrong; a 422 tells them the request was right and the world is not ready for it. Collapsing them
    /// would lose the difference between "you sent something invalid" and "end these three assignments
    /// and try again", and only the second is actionable.
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
            // Explicit arm, not the default. The default below maps to 400, so a Forbidden that fell
            // through would answer "your request was malformed" to a caller whose identity was the
            // problem, making the LegacyMigrated restriction look like a validation quirk.
            // Results.Forbid() is deliberately avoided: with no scheme named it defers to the
            // authentication handler, which for a cookie scheme is a redirect, not a 403.
            CompassWriteStatus.Forbidden => Results.Json(
                new { message = error },
                statusCode: StatusCodes.Status403Forbidden
            ),
            _ => Results.BadRequest(new { message = error }),
        };
}
