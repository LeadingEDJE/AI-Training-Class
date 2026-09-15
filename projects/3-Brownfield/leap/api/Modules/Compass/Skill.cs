using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// Super Admin-managed lookup of technical skills an EDJEr may be tagged with. Table
/// <c>compass.skill</c>, added for the technical-skills feature.
/// </summary>
public class Skill : ICompassLookup
{
    /// <summary>Primary key. Mapped to the column <c>skill_id</c>.</summary>
    public int Id { get; set; }

    /// <summary>
    /// The unique skill name, e.g. "React". Named <c>TypeName</c> rather than <c>Name</c> so this
    /// entity satisfies <see cref="ICompassLookup"/> and reuses the generic
    /// <c>CompassLookupRepository&lt;T&gt;</c>/<c>CompassLookupService</c> pair unchanged; the DTO
    /// boundary renames it to <c>Name</c> for callers.
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Whether this skill may be selected when tagging an EDJEr.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>EDJErs tagged with this skill.</summary>
    public ICollection<EmployeeSkill> EmployeeSkills { get; set; } = [];
}
