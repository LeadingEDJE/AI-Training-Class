using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>Maps <see cref="Sow"/> to <c>compass.sow</c>, including the overlap exclusion constraint.</summary>
public class SowConfiguration : CompassEntityConfiguration<Sow>
{
    /// <inheritdoc />
    protected override string TableName => "sow";

    /// <inheritdoc />
    protected override void ConfigureTable(TableBuilder<Sow> table)
    {
        table.HasCheckConstraint(
            "ck_sow_end_on_or_after_start",
            "sow_type = 'LegacyMigrated' OR sow_end_date >= sow_start_date"
        );

        table.HasCheckConstraint(
            "ck_sow_rate_increase_requires_extension",
            "NOT rate_increase OR sow_type = 'SowExtension'"
        );

        // Uses a native Postgres enum type, so new values can be added without a migration.
        table.HasCheckConstraint(
            "ck_sow_type_is_known",
            "sow_type IN ('InitialContract', 'SowExtension', 'LegacyMigrated')"
        );
    }

    /// <inheritdoc />
    protected override void ConfigureEntity(EntityTypeBuilder<Sow> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("sow_id");

        builder
            .Property(x => x.ClientAssignmentId)
            .HasColumnName("client_assignment_id")
            .IsRequired();

        builder
            .Property(x => x.SowType)
            .HasColumnName("sow_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.RateIncrease).HasColumnName("rate_increase").IsRequired();

        builder
            .Property(x => x.HasPassedApplicationValidation)
            .HasColumnName("has_passed_application_validation")
            .IsRequired();

        builder.Property(x => x.SowStartDate).HasColumnName("sow_start_date").IsRequired();
        builder.Property(x => x.SowEndDate).HasColumnName("sow_end_date").IsRequired();

        builder.Property(x => x.Note).HasColumnName("note");

        builder
            .Property(x => x.LegacyTpsId)
            .HasColumnName("legacy_tps_id")
            .HasMaxLength(64);

        builder
            .HasIndex(x => x.LegacyTpsId)
            .IsUnique()
            .HasDatabaseName("ux_sow_legacy_tps_id");

        builder
            .HasIndex(x => new { x.ClientAssignmentId, x.SowEndDate })
            .HasDatabaseName("ix_sow_client_assignment_id_sow_end_date");

        builder
            .HasOne(x => x.ClientAssignment)
            .WithMany(x => x.Sows)
            .HasForeignKey(x => x.ClientAssignmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
