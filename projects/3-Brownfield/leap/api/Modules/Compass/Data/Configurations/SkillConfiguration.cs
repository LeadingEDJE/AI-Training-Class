using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>Maps <see cref="Skill"/> to <c>compass.skill</c>.</summary>
public class SkillConfiguration : CompassEntityConfiguration<Skill>
{
    /// <inheritdoc />
    protected override string TableName => "skill";

    /// <inheritdoc />
    protected override void ConfigureEntity(EntityTypeBuilder<Skill> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("skill_id");

        // 50, matching CompassLookupService.MaxTypeNameLength — the column stays no wider than what
        // validation ever lets through.
        builder.Property(x => x.TypeName).HasColumnName("name").HasMaxLength(50).IsRequired();

        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        builder
            .HasIndex(x => x.TypeName)
            .IsUnique()
            .HasDatabaseName("ux_skill_name");
    }
}
