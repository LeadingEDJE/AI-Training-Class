using Microsoft.AspNetCore.Http;

namespace LeadingEDJE.Leap.Api.Platform.Dtos;

/// <summary>
/// Outcome of an admin create/update/deactivate mutation, mapped to an HTTP status by the endpoint
/// layer. Keeps the service layer free of HTTP concerns.
/// </summary>
public enum AdminMutationStatus
{
    /// <summary>The mutation succeeded.</summary>
    Success,

    /// <summary>The targeted row does not exist (→ 404).</summary>
    NotFound,

    /// <summary>A case-insensitive email collision was detected (→ 409).</summary>
    EmailConflict,

    /// <summary>A unique-constraint collision was detected (e.g. EmployeeNumber after retries) (→ 409).</summary>
    Conflict,

    /// <summary>The request failed input validation, e.g. missing/invalid email or blank name (→ 400).</summary>
    ValidationError
}

/// <summary>
/// Maps a non-success <see cref="AdminMutationStatus"/> to its canonical HTTP error result. Admin
/// endpoints keep their own success arm and delegate every failure here.
/// </summary>
public static class AdminMutationStatusResults
{
    /// <summary>
    /// The HTTP error result for a non-success mutation outcome: 404 for <see cref="AdminMutationStatus.NotFound"/>,
    /// 409 (with the error message) for either conflict variant, 400 (with the error message) otherwise.
    /// </summary>
    public static IResult ToErrorResult(this AdminMutationStatus status, string? error) => status switch
    {
        AdminMutationStatus.NotFound => Results.NotFound(),
        AdminMutationStatus.EmailConflict or AdminMutationStatus.Conflict => Results.Conflict(new { message = error }),
        _ => Results.BadRequest(new { message = error }),
    };
}
