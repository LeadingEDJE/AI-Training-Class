using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>Maps <see cref="BillableTimeCategory"/> to <c>compass.billable_time_category</c>.</summary>
public class BillableTimeCategoryConfiguration
    : CompassEntityConfiguration<BillableTimeCategory>
{
    /// <inheritdoc />
    protected override string TableName => "billable_time_category";

    /// <inheritdoc />
    protected override void ConfigureEntity(EntityTypeBuilder<BillableTimeCategory> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("billable_time_category_id");

        builder.Property(x => x.ClientId).HasColumnName("client_id").IsRequired();

        builder
            .Property(x => x.CategoryName)
            .HasColumnName("category_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        builder
            .Property(x => x.LegacyTpsId)
            .HasColumnName("legacy_tps_id")
            .HasMaxLength(64);

        builder
            .HasIndex(x => x.LegacyTpsId)
            .IsUnique()
            .HasDatabaseName("ux_billable_time_category_legacy_tps_id");

        // Enforces global uniqueness on the category name.
        builder
            .HasIndex(x => new { x.ClientId, x.CategoryName })
            .IsUnique()
            .HasDatabaseName("ux_billable_time_category_client_id_category_name");

        builder
            .HasOne(x => x.Client)
            .WithMany(x => x.BillableTimeCategories)
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
