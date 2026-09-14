using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// Verifies that every migrated Compass table carries its TPS provenance, against the LIVE Postgres
/// catalog and the LIVE constraint behaviour.
/// </summary>
/// <remarks>
/// <para>
/// Why the target owns this rather than the migration tool alone. The ETL keeps a crosswalk
/// mapping each TPS identifier to the Compass record it produced, and that crosswalk is the
/// idempotency check, the foreign-key resolver and the resume point all at once. It used to be the
/// ONLY record of where a Compass row came from, which made losing it unrecoverable and made
/// "back up the crosswalk" a production-day gate with no second line of defence.
/// </para>
/// <para>
/// With <c>legacy_tps_id</c> on the row itself, provenance is durable in the same database that
/// holds the record, backed up by the same backup, and answerable — "what did this Compass record
/// come from?" — years after the migration tool and its file are gone. A lost crosswalk is now
/// REBUILDABLE by reading these columns back.
/// </para>
/// <para>
/// The crosswalk is still kept, and that is deliberate. Reconciliation reports three counts
/// per entity — source, what the ETL believes it loaded, and what Compass actually holds. Collapsing
/// the middle column into the third would leave the report comparing Compass against itself, unable
/// to detect the drift it exists to detect.
/// </para>
/// <para>
/// No <c>HasFilter</c>, and that is not an omission. Postgres unique indexes are
/// <c>NULLS DISTINCT</c> by default, so a plain unique index already permits unlimited nulls and
/// means exactly "unique when present". A partial index would be redundant here, and this repository
/// has no <c>HasFilter</c> idiom to match — the uniqueness tests below pin the behaviour rather than
/// the declaration.
/// </para>
/// </remarks>
public class CompassLegacyProvenanceTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private const string CompassSchema = "compass";
    private const string UniqueViolation = "23505";

    /// <summary>The five tables a TPS migration creates rows in.</summary>
    /// <remarks>
    /// The two lookup tables (<c>employee_type</c>, <c>invoice_frequency_type</c>) are deliberately
    /// absent: the migration READS them and creates none, so a provenance column there would always
    /// be null and would assert a relationship that does not exist.
    /// </remarks>
    private static readonly string[] MigratedTables =
    [
        "billable_time_category",
        "client",
        "client_assignment",
        "employee",
        "sow",
    ];

    [Fact]
    public async Task EveryMigratedTable_HasANullableLegacyTpsIdColumn()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var columns = await context
            .Database.SqlQueryRaw<string>(
                $@"SELECT table_name AS ""Value""
                   FROM information_schema.columns
                   WHERE table_schema = '{CompassSchema}'
                     AND column_name = 'legacy_tps_id'
                     AND is_nullable = 'YES'
                   ORDER BY table_name;"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert — NULLABLE is half the assertion. A record a person creates has no TPS origin and
        // must be insertable without inventing one, so a NOT NULL column here would break every
        // ordinary create in Compass.
        columns.ShouldBe(
            MigratedTables,
            ignoreOrder: false,
            customMessage: "every table the migration writes to must carry its TPS provenance"
        );
    }

    [Fact]
    public async Task LookupTables_DoNotCarryProvenance()
    {
        // Arrange — the migration reads employee_type and invoice_frequency_type and creates
        // neither. A provenance column there would be permanently null and would imply a migration
        // relationship that does not exist.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var strays = await context
            .Database.SqlQueryRaw<string>(
                $@"SELECT table_name AS ""Value""
                   FROM information_schema.columns
                   WHERE table_schema = '{CompassSchema}'
                     AND column_name = 'legacy_tps_id'
                     AND table_name IN ('employee_type', 'invoice_frequency_type');"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        strays.ShouldBeEmpty("the migration creates no lookup rows, so they have no TPS origin");
    }

    [Fact]
    public async Task LegacyTpsId_IsUniqueWhenPresent()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        await context
            .Database.ExecuteSqlRawAsync(
                """
                INSERT INTO compass.client (client_name, is_internal, legacy_tps_id)
                VALUES ('First Client', false, 'tps-client-1');
                """,
                TestContext.Current.CancellationToken
            );

        // Act — a second Compass record claiming the SAME TPS origin means one source row produced
        // two records. Every relationship resolved afterwards would point at whichever won, and the
        // migration would look complete while being silently wrong.
        var duplicate = await Should.ThrowAsync<PostgresException>(
            async () =>
                await context.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO compass.client (client_name, is_internal, legacy_tps_id)
                    VALUES ('Second Client', false, 'tps-client-1');
                    """,
                    TestContext.Current.CancellationToken
                )
        );

        // Assert
        duplicate.SqlState.ShouldBe(
            UniqueViolation,
            "one TPS row must not be able to produce two Compass records"
        );
    }

    [Fact]
    public async Task LegacyTpsId_PermitsManyRowsWithoutProvenance()
    {
        // Arrange — ⚠️ the assertion that makes the unique index safe to add at all. Compass records
        // created by a person carry no TPS origin, so if nulls collided under the unique index the
        // SECOND hand-created client of any kind would fail. Postgres unique indexes are NULLS
        // DISTINCT by default, which is precisely why no partial index is needed here.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act — inserting both in one statement means a collision fails the test loudly rather than
        // leaving one row behind for a later assertion to misread.
        var inserted = await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO compass.client (client_name, is_internal, legacy_tps_id)
            VALUES ('Hand Made One', false, NULL), ('Hand Made Two', false, NULL);
            """,
            TestContext.Current.CancellationToken
        );

        // Assert
        inserted.ShouldBe(2);
    }
}
