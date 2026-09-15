namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// Adds a skill.
/// </summary>
/// <remarks>
/// Carries no active flag: a new skill is created active, matching every other Compass lookup.
/// </remarks>
/// <param name="Name">The display name, which must not already be in use.</param>
public record CreateSkillRequest(string Name);

/// <summary>
/// Renames a skill and/or changes whether it is selectable.
/// </summary>
/// <param name="Name">The display name, which must not collide with another skill.</param>
/// <param name="IsActive">Whether the skill is offered on new and edited EDJEr records.</param>
public record UpdateSkillRequest(string Name, bool IsActive);
