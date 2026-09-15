namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// A skill lookup value as the configuration API returns it.
/// </summary>
/// <param name="Id">The skill's identifier, used when editing it.</param>
/// <param name="Name">The display name, unique across skills.</param>
/// <param name="IsActive">Whether the value is offered when tagging an EDJEr.</param>
public record SkillDto(int Id, string Name, bool IsActive);
