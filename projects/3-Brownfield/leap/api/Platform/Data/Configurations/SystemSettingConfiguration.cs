using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Platform.Data.Configurations;

/// <summary>EF Core configuration for the SystemSetting entity (application-wide key-value settings).</summary>
public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    /// <summary>Applies the entity configuration to the model builder.</summary>
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.HasKey(s => s.Key);

        builder.Property(s => s.Key).IsRequired().HasMaxLength(255);
        builder.Property(s => s.Value).IsRequired().HasMaxLength(2000);
        builder.Property(s => s.Description).HasMaxLength(2000);
    }
}
