using LeadingEDJE.Leap.Api.Platform.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// The outcome of a lookup create or update, carrying enough for the endpoint to choose a status code
/// without the service knowing anything about HTTP.
/// </summary>
/// <typeparam name="TDto">The lookup DTO returned on success.</typeparam>
/// <remarks>
/// Reuses the platform's <see cref="AdminMutationStatus"/> and its
/// <c>AdminMutationStatusResults.ToErrorResult</c> mapping rather than introducing a second mechanism
/// for the same job — that enum already exists precisely to keep services free of HTTP concerns while
/// distinguishing not-found, conflict and validation failures.
/// </remarks>
/// <param name="Status">What happened.</param>
/// <param name="Value">The written lookup, present only on success.</param>
/// <param name="Error">A message naming the problem, present only on failure.</param>
public sealed record CompassLookupWrite<TDto>(
    AdminMutationStatus Status,
    TDto? Value,
    string? Error
)
    where TDto : class
{
    /// <summary>The write succeeded.</summary>
    public static CompassLookupWrite<TDto> Succeeded(TDto value) =>
        new(AdminMutationStatus.Success, value, null);

    /// <summary>No lookup with that id exists.</summary>
    public static CompassLookupWrite<TDto> NotFound() =>
        new(AdminMutationStatus.NotFound, null, null);

    /// <summary>Another lookup already carries the requested name.</summary>
    public static CompassLookupWrite<TDto> Duplicate(string error) =>
        new(AdminMutationStatus.Conflict, null, error);

    /// <summary>The request was not well formed — a blank or over-long name.</summary>
    public static CompassLookupWrite<TDto> Invalid(string error) =>
        new(AdminMutationStatus.ValidationError, null, error);
}
