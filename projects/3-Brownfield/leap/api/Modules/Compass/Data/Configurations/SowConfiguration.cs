using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>Maps <see cref="Sow"/> to <c>compass.sow</c>.</summary>
/// <remarks>
/// Three of the four integrity rules on this table are declared here as CHECK constraints. The
/// fourth — SOW periods must not overlap within one assignment — has no EF fluent equivalent and is
/// added as raw SQL in the migration (<c>EXCLUDE USING gist</c>). All four are verified against the
/// live catalog by <c>CompassSchemaFromErdTests</c>.
///
/// Two are partial, keyed on <see cref="SowType"/>: the overlap and end-on-or-after-start rules skip
/// <see cref="SowType.LegacyMigrated"/> so a TPS load is admitted as recorded (FR-053), which
/// Postgres allows through a <c>WHERE</c> clause rather than dropping the constraints.
/// </remarks>
public class SowConfiguration : CompassEntityConfiguration<Sow>
{
    /// <inheritdoc />
    protected override string TableName => "sow";

    /// <inheritdoc />
    protected override void ConfigureTable(TableBuilder<Sow> table)
    {
        // Both dates are required, so this needs no NULL guard — unlike client_assignment.end_date.
        // Partial: LegacyMigrated is exempt (FR-053), because legacy TPS contains backwards ranges no
        // one can now reconstruct and blocking the load on them would put a data-cleansing project on
        // the cutover critical path. Written as an implication so the exemption is visible at the call
        // site rather than hidden in a WHERE clause.
        table.HasCheckConstraint(
            "ck_sow_end_on_or_after_start",
            "sow_type = 'LegacyMigrated' OR sow_end_date >= sow_start_date"
        );

        // The ERD: "rate_increase — Extensions only (CHECK-enforced)", restated against the
        // three-value type. Not partial: a rate increase is a fact someone entered, not an artefact
        // of the legacy shape, so LegacyMigrated does not inherit the permission. TPS tracks no
        // extensions at all, so no migrated row has one to record.
        table.HasCheckConstraint(
            "ck_sow_rate_increase_requires_extension",
            "NOT rate_increase OR sow_type = 'SowExtension'"
        );

        // The enum is stored as its NAME, so the column is a varchar and the set of legal values has
        // to be asserted here — nothing else stops a typo from being persisted. Postgres native enum
        // types were not used: adding a value to one is a schema migration with its own transaction
        // rules, and the repository already persists enums as strings (TimesheetStatus).
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

        // Stored as the enum NAME, matching TimesheetConfiguration/TimeCategoryConfiguration. The
        // length mirrors those (50); the longest value, "InitialContract", is 15.
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

        // TPS provenance. Nullable, because a record created in Compass has no legacy origin, and
        // unique so one TPS row cannot produce two Compass records. NO HasFilter: Postgres unique
        // indexes are NULLS DISTINCT by default, so this already means "unique when present" and a
        // partial index would be redundant.
        builder
            .Property(x => x.LegacyTpsId)
            .HasColumnName("legacy_tps_id")
            .HasMaxLength(64);

        builder
            .HasIndex(x => x.LegacyTpsId)
            .IsUnique()
            .HasDatabaseName("ux_sow_legacy_tps_id");

        // The "SOW expiring < 90 days" dashboard tile scans by assignment and end date.
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
