using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Platform.Data.Configurations;

/// <summary>EF Core configuration for the AuditLog entity (append-only change tracking records).</summary>
public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    /// <summary>Applies the entity configuration to the model builder.</summary>
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(50);
        builder.Property(a => a.EntityId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Actor).IsRequired().HasMaxLength(100);
        builder.Property(a => a.TriggeredBy).IsRequired().HasMaxLength(255);
        builder.Property(a => a.Reason).IsRequired().HasMaxLength(2000);
        builder.Property(a => a.Changes).HasColumnType("jsonb");
        builder.Property(a => a.Timestamp)
            .IsRequired();
        // EffectiveRoles is unconfigured on purpose — convention gives it the native text[] mapping.
        // Why that is right, and why it takes no default: AuditLog.EffectiveRoles, asserted against
        // the live catalog by CompassAuditTests.

        builder.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}
