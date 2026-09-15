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

        // Required with a default of Monthly, per the original billing-cadence spec (spec 004 §3.1).
        builder
            .Property(x => x.InvoiceFrequencyTypeId)
            .HasColumnName("invoice_frequency_type_id");

        builder.HasIndex(x => x.EmployeeId).HasDatabaseName("ix_client_assignment_employee_id");
        builder.HasIndex(x => x.ClientId).HasDatabaseName("ix_client_assignment_client_id");

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
            // Cascades so a removed cadence cleans up the assignments that reference it.
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(x => x.Client)
            .WithMany(x => x.ClientAssignments)
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
