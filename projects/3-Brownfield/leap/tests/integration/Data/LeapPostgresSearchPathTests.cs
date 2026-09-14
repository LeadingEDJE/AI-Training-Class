using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Data;

/// <summary>
/// Proves, against a REAL PostgreSQL server, that <see cref="LeapDbContextOptionsExtensions.UseLeapPostgres"/>
/// resolves the migration history correctly once a schema named after the connecting role exists.
/// </summary>
/// <remarks>
/// <para>
/// The scenario, exactly as it happened (issue #208). Migration
/// <c>20260810202407_MoveModuleTablesToSchemas</c> created a Postgres schema named <c>timesheet</c> —
/// the same name as the role every environment connects as (<c>appsettings.json</c>'s
/// <c>POSTGRES_USER=timesheet</c>, hard-coded and not derived). Postgres's default search_path is
/// <c>"$user", public</c>; once a schema named after the role exists, <c>"$user"</c> stops being a
/// no-op and starts resolving unqualified relations — notably EF's own
/// <c>"__EFMigrationsHistory"</c> — against <c>timesheet</c> before <c>public</c>. A fresh connection
/// then finds no history there, concludes nothing is applied, and reruns <c>InitialCreate</c> against
/// tables that already exist (SQLSTATE 42P07) — the Helm migrate hook's <c>BackoffLimitExceeded</c>.
/// </para>
/// <para>
/// This class starts its OWN container, with username <c>timesheet</c> to reproduce the exact
/// collision, rather than reusing <c>IntegrationTestFactory</c>'s (which uses the same username but
/// registers its <c>DbContext</c> via a raw <c>UseNpgsql</c> call, not <c>UseLeapPostgres</c> — it
/// never exercises the fix this class exists to prove).
/// </para>
/// </remarks>
public sealed class LeapPostgresSearchPathTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16")
        .WithUsername("timesheet")
        .WithPassword("searchpathprobe")
        .WithDatabase("leap_search_path_probe")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAndAwaitHostConnectivityAsync(TestContext.Current.CancellationToken);

        // Migrate through UseLeapPostgres itself, exactly as both api/Program.cs and
        // DesignTimeDbContextFactory do. This is what creates the `timesheet` schema (among the
        // module schemas) and populates public."__EFMigrationsHistory".
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseLeapPostgres(_container.GetConnectionString())
            .Options;
        await PostgreSqlContainerExtensions.MigrateWithRetryAsync(async () =>
        {
            await using var migrationContext = new LeapDbContext(options);
            await migrationContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        // Reproduce the EXACT bad state issue #208 found in the wild: an empty duplicate history
        // table inside a `timesheet` schema, shaped identically to EF's own. Schema search_path
        // resolution tries each schema in order and only defers to the next one when the CURRENT
        // schema has no matching relation at all -- so without an actual timesheet."__EFMigrationsHistory"
        // to find, a fresh connection correctly falls through to public regardless of "$user".
        // The collision only bites once both copies exist, which is the confirmed state on the
        // affected deploy; how it first got created there was not fully reconstructed.
        //
        // The Timesheet module (and the migration that once created this schema as a side effect of
        // owning it) is retired, so the schema is created explicitly here instead -- the scenario
        // this class exists to guard against only needs a schema NAMED THE SAME AS THE CONNECTING
        // ROLE, not a live Timesheet module.
        await using var rawConnection = new NpgsqlConnection(_container.GetConnectionString());
        await rawConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using var createDuplicateHistoryTable = new NpgsqlCommand(
            """
            CREATE SCHEMA IF NOT EXISTS timesheet;
            CREATE TABLE timesheet."__EFMigrationsHistory" (
                migration_id character varying(150) NOT NULL,
                product_version character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY (migration_id)
            );
            """,
            rawConnection);
        await createDuplicateHistoryTable.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task ANewConnection_AfterTheTimesheetSchemaExists_ResolvesCurrentSchemaToPublic()
    {
        // Arrange -- a brand-new context/connection, the same way a fresh deploy's migration Job
        // or a new API pod opens one; nothing here is inherited from the migration above.
        await using var context = OpenNewConnection();

        // Act -- SqlQuery<T>'s scalar shape reads a column literally named "Value";
        // UseSnakeCaseNamingConvention does not touch that internal shape.
        var currentSchema = await context.Database
            .SqlQuery<string>($"SELECT current_schema() AS \"Value\"")
            .SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        currentSchema.ShouldBe("public");
    }

    [Fact]
    public async Task ANewConnection_AfterTheTimesheetSchemaExists_SeesNoPendingMigrations()
    {
        // Arrange
        await using var context = OpenNewConnection();

        // Act -- this is the exact call the Helm migrate hook makes. Before the fix, this
        // resolved "__EFMigrationsHistory" against the empty `timesheet` schema copy and reported
        // every migration pending, including InitialCreate.
        var pending = await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);

        // Assert
        pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task DespiteTheDuplicateInTimesheetSchema_EFStillReadsThePopulatedPublicHistory()
    {
        // Arrange -- the fixture deliberately created an empty timesheet."__EFMigrationsHistory"
        // (the confirmed issue #208 state), so both copies genuinely exist in the catalog.
        await using var context = OpenNewConnection();

        var historyTables = await context.Database
            .SqlQuery<HistoryTableRow>(
                $"""
                SELECT table_schema, table_name
                FROM information_schema.tables
                WHERE table_name = '__EFMigrationsHistory'
                ORDER BY table_schema
                """)
            .ToListAsync(TestContext.Current.CancellationToken);
        historyTables.Select(t => t.TableSchema).ShouldBe(["public", "timesheet"]);

        // Act -- read the catalog directly rather than through EF, so this does not just assert
        // the model against itself.
        var publicRowCount = await context.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM public.\"__EFMigrationsHistory\"")
            .SingleAsync(TestContext.Current.CancellationToken);
        var timesheetRowCount = await context.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM timesheet.\"__EFMigrationsHistory\"")
            .SingleAsync(TestContext.Current.CancellationToken);

        // Assert -- public carries every applied migration; the duplicate stays the empty
        // decoy it started as. Neither of these depends on search_path -- both are fully
        // schema-qualified reads -- so this pins the fixture's own claimed state, and the
        // ANewConnection_* tests above are what actually prove UseLeapPostgres reads the right
        // one when it is NOT told which schema to use.
        publicRowCount.ShouldBeGreaterThan(0);
        timesheetRowCount.ShouldBe(0);
    }

    private LeapDbContext OpenNewConnection()
    {
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseLeapPostgres(_container.GetConnectionString())
            .Options;
        return new LeapDbContext(options);
    }

    private sealed record HistoryTableRow
    {
        public string TableSchema { get; init; } = string.Empty;
        public string TableName { get; init; } = string.Empty;
    }
}
