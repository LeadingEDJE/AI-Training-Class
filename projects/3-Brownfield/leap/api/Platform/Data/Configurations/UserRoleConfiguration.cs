using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Platform.Data.Configurations;

/// <summary>EF Core configuration for the UserRole entity (additive role assignments per employee).</summary>
public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    /// <summary>Applies the entity configuration to the model builder.</summary>
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.HasKey(ur => ur.Id);
        builder.Property(ur => ur.Id).ValueGeneratedOnAdd();

        builder.HasIndex(ur => new { ur.EdjeId, ur.Role }).IsUnique();
        builder.Property(ur => ur.EdjeId).IsRequired();
        builder.Property(ur => ur.Role).HasMaxLength(50).IsRequired();
    }
}
