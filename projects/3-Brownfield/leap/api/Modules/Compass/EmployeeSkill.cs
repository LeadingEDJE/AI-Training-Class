namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// One EDJEr tagged with one technical skill. Join table <c>compass.employee_skill</c>, added for
/// the technical-skills feature.
/// </summary>
public class EmployeeSkill
{
    /// <summary>Primary key. Mapped to the column <c>employee_skill_id</c>.</summary>
    public int Id { get; set; }

    /// <summary>Required FK to the tagged <see cref="Employee"/>.</summary>
    public int EmployeeId { get; set; }

    /// <summary>Required FK to the <see cref="Skill"/>.</summary>
    public int SkillId { get; set; }

    /// <summary>The tagged EDJEr.</summary>
    public Employee? Employee { get; set; }

    /// <summary>The skill.</summary>
    public Skill? Skill { get; set; }
}
