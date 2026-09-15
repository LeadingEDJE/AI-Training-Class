namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>An active skill, as offered to the Team Directory filter and EDJEr tagging (AC-6-style).</summary>
public sealed class SkillOptionDto
{
    /// <summary>The skill's identifier.</summary>
    public int Id { get; init; }

    /// <summary>The skill's display name.</summary>
    public string Name { get; init; } = string.Empty;
}
