using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>Maps <see cref="Employee"/> to <c>compass.employee</c>.</summary>
public class EmployeeConfiguration : CompassEntityConfiguration<Employee>
{
    /// <inheritdoc />
    protected override string TableName => "employee";

    /// <inheritdoc />
    protected override void ConfigureTable(TableBuilder<Employee> table) =>
        table.HasCheckConstraint(
            "ck_employee_state_of_residence_us",
            $"state_of_residence IN ({string.Join(", ", UsStateCodes.All.Select(c => $"'{c}'"))})"
        );

    /// <inheritdoc />
    protected override void ConfigureEntity(EntityTypeBuilder<Employee> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("employee_id");

        builder
            .Property(x => x.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();

        builder.Property(x => x.HireDate).HasColumnName("hire_date").IsRequired();

        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(255).IsRequired();

        builder.Property(x => x.EmployeeTypeId).HasColumnName("employee_type_id").IsRequired();

        builder.Property(x => x.CoachEmployeeId).HasColumnName("coach_employee_id");

        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        builder
            .Property(x => x.StateOfResidence)
            .HasColumnName("state_of_residence")
            .HasColumnType("char(2)")
            .IsRequired();

        // A CHECK constraint mirrors UsTimeZones.All here, same as state_of_residence above.
        builder
            .Property(x => x.Timezone)
            .HasColumnName("timezone")
            .HasMaxLength(64)
            .HasDefaultValue(UsTimeZones.Default)
            .IsRequired();

        builder.Property(x => x.TimesheetRequired).HasColumnName("timesheet_required").IsRequired();

        builder
            .Property(x => x.CanSubmitUnder40)
            .HasColumnName("can_submit_under_40")
            .IsRequired();

        builder.Property(x => x.IncludeInPayroll).HasColumnName("include_in_payroll").IsRequired();

        // ValueGeneratedNever marks this column as computed by the database on every read.
        builder
            .Property(x => x.IsDeliveryTeam)
            .HasColumnName("is_delivery_team")
            .IsRequired()
            .HasDefaultValue(true)
            .ValueGeneratedNever();

        builder.HasIndex(x => x.LastName).HasDatabaseName("ix_employee_last_name");

        builder
            .Property(x => x.LegacyTpsId)
            .HasColumnName("legacy_tps_id")
            .HasMaxLength(64);

        builder
            .HasIndex(x => x.LegacyTpsId)
            .IsUnique()
            .HasDatabaseName("ux_employee_legacy_tps_id");

        builder
            .HasOne(x => x.EmployeeType)
            .WithMany(x => x.Employees)
            .HasForeignKey(x => x.EmployeeTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // The ERD's one self-reference. SetNull, not Restrict: a coaching relationship should not
        // block a delete, per the original org-chart design (see the coaching model doc).
        builder
            .HasOne(x => x.Coach)
            .WithMany(x => x.Coachees)
            .HasForeignKey(x => x.CoachEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
