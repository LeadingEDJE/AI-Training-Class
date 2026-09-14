namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// Adds a lookup value.
/// </summary>
/// <remarks>
/// Carries no active flag: a new value is created active. No acceptance criterion asks for creating a
/// value that is immediately unselectable, and AC-25/AC-26 describe retiring as an edit. Toggling
/// happens through the update request.
/// </remarks>
/// <param name="TypeName">The display name, which must not already be in use.</param>
public record CreateCompassLookupRequest(string TypeName);

/// <summary>
/// Renames a lookup value and/or changes whether it is selectable.
/// </summary>
/// <remarks>
/// One request covers both, because AC-25 and AC-26 describe a single edit operation covering rename
/// and retire — which is why there is no separate activate/deactivate route.
/// </remarks>
/// <param name="TypeName">The display name, which must not collide with another value.</param>
/// <param name="IsActive">Whether the value is offered on new and edited records.</param>
public record UpdateCompassLookupRequest(string TypeName, bool IsActive);
