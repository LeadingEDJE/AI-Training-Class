using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Platform.Data.Configurations;

/// <summary>EF Core configuration for <see cref="Person"/> (<c>public.people</c>).</summary>
/// <remarks>
/// The real uniqueness guard is <c>ix_people_email_lower</c>, a raw-SQL functional unique index on
/// <c>LOWER(email)</c> (EF's <c>HasIndex</c> takes properties, not expressions, so it cannot express
/// it) — ported by hand into the generated migration. The plain, non-unique index declared here
/// mirrors the original schema and lets an exact-match lookup use an index.
/// </remarks>
public class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    /// <summary>Applies the entity configuration to the model builder.</summary>
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("people");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.EdjeId).HasColumnName("edje_id");
        builder.Property(p => p.Email).HasColumnName("email").HasMaxLength(255);
        builder.Property(p => p.FirstName).HasColumnName("first_name").HasMaxLength(255);
        builder.Property(p => p.LastName).HasColumnName("last_name").HasMaxLength(255);
        builder.Property(p => p.IsActive).HasColumnName("is_active");
        builder.Property(p => p.Source).HasColumnName("source").HasMaxLength(64);
        builder.Property(p => p.Title).HasColumnName("title");

        builder.HasIndex(p => p.Email).HasDatabaseName("ix_people_email");
    }
}
