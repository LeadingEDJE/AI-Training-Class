using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>Maps <see cref="EmployeeSkill"/> to <c>compass.employee_skill</c>.</summary>
public class EmployeeSkillConfiguration : CompassEntityConfiguration<EmployeeSkill>
{
    /// <inheritdoc />
    protected override string TableName => "employee_skill";

    /// <inheritdoc />
    protected override void ConfigureEntity(EntityTypeBuilder<EmployeeSkill> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("employee_skill_id");

        builder.Property(x => x.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(x => x.SkillId).HasColumnName("skill_id").IsRequired();

        builder.HasIndex(x => x.EmployeeId).HasDatabaseName("ix_employee_skill_employee_id");
        builder.HasIndex(x => x.SkillId).HasDatabaseName("ix_employee_skill_skill_id");

        builder
            .HasIndex(x => new { x.EmployeeId, x.SkillId })
            .IsUnique()
            .HasDatabaseName("ux_employee_skill_employee_id_skill_id");

        // Cascade, not Restrict: unlike ClientAssignment (independently significant — its own notes,
        // SOWs, deletion endpoint), EmployeeSkill is a pure bridge row with no meaning of its own. The
        // admin sync path (CompassEmployeeService.SyncEmployeeSkills) removes a deselected skill from
        // employee.EmployeeSkills directly; with Restrict on a required FK, EF has no way to persist a
        // severed-but-undeleted child and throws InvalidOperationException on save. Cascade lets that
        // removal delete the row, which is the only sensible outcome for a join table anyway.
        builder
            .HasOne(x => x.Employee)
            .WithMany(x => x.EmployeeSkills)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(x => x.Skill)
            .WithMany(x => x.EmployeeSkills)
            .HasForeignKey(x => x.SkillId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
