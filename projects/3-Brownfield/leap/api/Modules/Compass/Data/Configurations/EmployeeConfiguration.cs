using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>Maps <see cref="Employee"/> to <c>compass.employee</c>.</summary>
public class EmployeeConfiguration : CompassEntityConfiguration<Employee>
{
    /// <inheritdoc />
    protected override string TableName => "employee";

    /// <inheritdoc />
    /// <remarks>
    /// The accepted codes come from <see cref="UsStateCodes.All"/>, which
    /// <c>CompassEmployeeService</c> validates against as well. They were written out here as a private
    /// array until the service needed the same list: two copies of 51 codes can drift, and the symptom
    /// would be a 500 on a value the service believed it had accepted. Same values, so this generates a
    /// byte-identical constraint and needs no migration.
    /// </remarks>
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

        // char(2), not varchar: a state code is always exactly two characters, and the fixed width
        // is what makes the CHECK above a complete specification of the column's domain.
        builder
            .Property(x => x.StateOfResidence)
            .HasColumnName("state_of_residence")
            .HasColumnType("char(2)")
            .IsRequired();

        // varchar, not char: IANA identifiers vary in length. No CHECK generated from UsTimeZones.All,
        // unlike state_of_residence directly above — the list must be able to widen without a schema
        // change, which a generated CHECK would prevent; see UsTimeZones. HasDefaultValue is the expand
        // half of expand/contract: during a rollout the previous
        // build still inserts rows with no notion of this column, and no default makes each one a 23502.
        builder
            .Property(x => x.Timezone)
            .HasColumnName("timezone")
            .HasMaxLength(64)
            .HasDefaultValue(UsTimeZones.Default)
            .IsRequired();

        builder.Property(x => x.TimesheetRequired).HasColumnName("timesheet_required").IsRequired();

        // Named explicitly because the naming convention's handling of a trailing digit run does not
        // reliably produce the ERD's `can_submit_under_40`.
        builder
            .Property(x => x.CanSubmitUnder40)
            .HasColumnName("can_submit_under_40")
            .IsRequired();

        builder.Property(x => x.IncludeInPayroll).HasColumnName("include_in_payroll").IsRequired();

        // HasDefaultValue is the expand half of expand/contract, as for timezone above.
        // ValueGeneratedNever goes further than that block and keeps the application create path off
        // the default: HasDefaultValue alone marks the property OnAdd, so EF omits the column whenever
        // the value is true — the ordinary create — and a lost default would make every one a 23502.
        // It is not what makes an explicit false saveable; EF already sentinels a true-defaulted bool.
        builder
            .Property(x => x.IsDeliveryTeam)
            .HasColumnName("is_delivery_team")
            .IsRequired()
            .HasDefaultValue(true)
            .ValueGeneratedNever();

        // Email uniqueness is ux_employee_email_ci, a UNIQUE index on lower(btrim(email)) created as raw
        // SQL in a migration, not declared here: EF's HasIndex takes properties, not expressions, so
        // HasIndex(x => x.Email).IsUnique() would add a second, weaker index. It spans active AND
        // inactive rows — EDJErs are deactivated, not deleted (AC-NFR-6 tracks no termination date) — so
        // a filtered index would let a departed EDJEr's address be handed to someone else. BR-9.

        // The ERD calls out last_name as indexed for directory search.
        builder.HasIndex(x => x.LastName).HasDatabaseName("ix_employee_last_name");

        // TPS provenance. Nullable, because a record created in Compass has no legacy origin, and
        // unique so one TPS row cannot produce two Compass records. No HasFilter: Postgres unique
        // indexes are NULLS DISTINCT, so this already means "unique when present". Unlike the
        // case-insensitive email index above, this one indexes a property rather than an expression,
        // so HasIndex expresses it exactly.
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

        // The ERD's one self-reference. Restrict, not SetNull: silently orphaning a coaching
        // relationship is worse than refusing the delete, and the domain deactivates rather than
        // deletes anyway.
        builder
            .HasOne(x => x.Coach)
            .WithMany(x => x.Coachees)
            .HasForeignKey(x => x.CoachEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
