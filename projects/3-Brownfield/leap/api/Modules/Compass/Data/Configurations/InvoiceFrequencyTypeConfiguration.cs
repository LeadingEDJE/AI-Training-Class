using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>Maps <see cref="InvoiceFrequencyType"/> to <c>compass.invoice_frequency_type</c>.</summary>
public class InvoiceFrequencyTypeConfiguration
    : CompassEntityConfiguration<InvoiceFrequencyType>
{
    /// <inheritdoc />
    protected override string TableName => "invoice_frequency_type";

    /// <inheritdoc />
    protected override void ConfigureEntity(EntityTypeBuilder<InvoiceFrequencyType> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("invoice_frequency_type_id");

        builder.Property(x => x.TypeName).HasColumnName("type_name").HasMaxLength(50).IsRequired();

        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        builder
            .HasIndex(x => x.TypeName)
            .IsUnique()
            .HasDatabaseName("ux_invoice_frequency_type_type_name");
    }
}
