using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>Maps <see cref="EmployeeType"/> to <c>compass.employee_type</c>.</summary>
public class EmployeeTypeConfiguration : CompassEntityConfiguration<EmployeeType>
{
    /// <inheritdoc />
    protected override string TableName => "employee_type";

    /// <inheritdoc />
    protected override void ConfigureEntity(EntityTypeBuilder<EmployeeType> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("employee_type_id");

        builder.Property(x => x.TypeName).HasColumnName("type_name").HasMaxLength(50).IsRequired();

        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        builder
            .HasIndex(x => x.TypeName)
            .IsUnique()
            .HasDatabaseName("ux_employee_type_type_name");
    }
}
