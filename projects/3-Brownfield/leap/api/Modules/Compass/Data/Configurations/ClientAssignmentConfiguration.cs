using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>Maps <see cref="ClientAssignment"/> to <c>compass.client_assignment</c>.</summary>
public class ClientAssignmentConfiguration : CompassEntityConfiguration<ClientAssignment>
{
    /// <inheritdoc />
    protected override string TableName => "client_assignment";

    /// <inheritdoc />
    protected override void ConfigureTable(TableBuilder<ClientAssignment> table) =>
        // NULL end_date is open-ended and must stay legal, so the CHECK only bites when a value is
        // present. `end_date >= start_date` alone would reject every open-ended assignment, since
        // NULL >= date is NULL, not true.
        table.HasCheckConstraint(
            "ck_client_assignment_end_on_or_after_start",
            "end_date IS NULL OR end_date >= start_date"
        );

    /// <inheritdoc />
    protected override void ConfigureEntity(EntityTypeBuilder<ClientAssignment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("client_assignment_id");

        builder.Property(x => x.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(x => x.ClientId).HasColumnName("client_id").IsRequired();

        builder.Property(x => x.StartDate).HasColumnName("start_date").IsRequired();
        builder.Property(x => x.EndDate).HasColumnName("end_date");

        builder.Property(x => x.Note).HasColumnName("note");

        // Nullable with no default, which makes the migration additive: a populated table takes the
        // column without a backfill and the previously-running code neither selects nor inserts it
        // (expand/contract). EF's FK convention also creates an
        // index nothing queries by; leave it, because hand-editing it out of the migration would
        // leave the ModelSnapshot disagreeing with the database.
        builder
            .Property(x => x.InvoiceFrequencyTypeId)
            .HasColumnName("invoice_frequency_type_id");

        // The directory, the dashboard and all three reports filter assignments by employee and by
        // client, so both FKs are indexed rather than relying on the FK alone (Postgres does not
        // index the referencing side automatically).
        builder.HasIndex(x => x.EmployeeId).HasDatabaseName("ix_client_assignment_employee_id");
        builder.HasIndex(x => x.ClientId).HasDatabaseName("ix_client_assignment_client_id");

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
            .HasDatabaseName("ux_client_assignment_legacy_tps_id");

        builder
            .HasOne(x => x.Employee)
            .WithMany(x => x.ClientAssignments)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(x => x.InvoiceFrequencyType)
            .WithMany()
            .HasForeignKey(x => x.InvoiceFrequencyTypeId)
            // Restrict, matching every other FK here: retiring a cadence must never delete the
            // assignments that reference it, and a retired value stays readable (004 US3's rule).
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(x => x.Client)
            .WithMany(x => x.ClientAssignments)
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
