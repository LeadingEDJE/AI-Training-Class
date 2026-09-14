using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Configurations;

/// <summary>Maps <see cref="Client"/> to <c>compass.client</c>.</summary>
/// <remarks>
/// Note what is NOT configured here: there is no status column. Active/Inactive is derived from the
/// client's assignments, and <c>CompassSchemaFromErdTests.Client_HasNoStoredStatusColumn</c> fails if
/// one is ever added.
/// </remarks>
public class ClientConfiguration : CompassEntityConfiguration<Client>
{
    /// <inheritdoc />
    protected override string TableName => "client";

    /// <inheritdoc />
    protected override void ConfigureEntity(EntityTypeBuilder<Client> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("client_id");

        builder
            .Property(x => x.ClientName)
            .HasColumnName("client_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.MsaSignedDate).HasColumnName("msa_signed_date");
        builder.Property(x => x.NdaSignedDate).HasColumnName("nda_signed_date");

        builder.Property(x => x.IsInternal).HasColumnName("is_internal").IsRequired();

        builder.Property(x => x.InvoiceFrequencyTypeId).HasColumnName("invoice_frequency_type_id");

        builder.HasIndex(x => x.ClientName).IsUnique().HasDatabaseName("ux_client_client_name");

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
            .HasDatabaseName("ux_client_legacy_tps_id");

        builder
            .HasOne(x => x.InvoiceFrequencyType)
            .WithMany(x => x.Clients)
            .HasForeignKey(x => x.InvoiceFrequencyTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
