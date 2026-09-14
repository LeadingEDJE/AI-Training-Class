using System.Diagnostics;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Data;

/// <summary>
/// Proves the database bootstrap against a REAL PostgreSQL server: it creates an absent database, is a
/// no-op on the second run, tolerates a genuine concurrent creator, degrades without the
/// create-database right, and fails loudly — within its bound — when the server is unreachable.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists on top of the unit suite. The unit tests drive a hand-written double, and a
/// double only ever agrees with whatever its author believed the server does. The two SQLSTATEs this
/// phase depends on — <c>42P04</c> and <c>42501</c> — are server behaviours. If PostgreSQL raised a
/// different code than the double was programmed with, every unit test would be green and the deploy
/// would still fail.
/// </para>
/// <para>
/// This class starts its OWN container rather than reusing <c>IntegrationTestFactory</c>'s. It
/// creates and drops databases and adds a role, which would disturb a fixture other tests share.
/// </para>
/// </remarks>
public sealed class DatabaseBootstrapperIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16")
        .WithUsername("bootstrapowner")
        .WithPassword("bootstrappass")
        .WithDatabase("bootstrap_maintenance_probe")
        .Build();

    /// <summary>Names are unique per run so a re-run cannot collide with a leftover.</summary>
    private readonly string _runSuffix = Guid.NewGuid().ToString("N")[..12];

    private readonly List<string> _createdDatabases = [];

    private string MaintenanceConnectionString => BuildConnectionString("postgres", "bootstrapowner", "bootstrappass");

    public async ValueTask InitializeAsync() =>
        await _container.StartAndAwaitHostConnectivityAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        // Best effort: the container is torn down immediately afterwards anyway, but dropping keeps the
        // catalogue assertions honest if this class is ever pointed at a shared server.
        foreach (var name in _createdDatabases)
        {
            try
            {
                await ExecuteAsync(MaintenanceConnectionString, $"DROP DATABASE IF EXISTS \"{name}\"");
            }
            catch (PostgresException)
            {
                // The container is going away; a failed cleanup drop is not a test failure.
            }
        }

        await _container.DisposeAsync();
    }

    // -------------------------------------------------------------- task 1: create, repeat, race

    [Fact]
    public async Task Bootstrap_AgainstAnAbsentDatabase_CreatesItOnARealServer()
    {
        // Arrange
        var name = TrackName($"p51_create_{_runSuffix}");
        (await CountDatabasesAsync(name)).ShouldBe(0);

        // Act
        var result = await RunBootstrapAsync(name);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.Created);

        // Read the CATALOGUE, not the outcome. An outcome that says created while the database does
        // not exist is exactly what this test is here to exclude.
        (await CountDatabasesAsync(name)).ShouldBe(1);
    }

    [Fact]
    public async Task Bootstrap_RunTwice_IssuesNoSecondCreateAndLeavesExactlyOneDatabase()
    {
        // Arrange
        var name = TrackName($"p51_repeat_{_runSuffix}");
        (await RunBootstrapAsync(name)).Outcome.ShouldBe(DatabaseBootstrapOutcome.Created);

        // Act
        var second = await RunBootstrapAsync(name);

        // Assert
        second.Outcome.ShouldBe(DatabaseBootstrapOutcome.AlreadyPresent);
        (await CountDatabasesAsync(name)).ShouldBe(1);
    }

    [Fact]
    public async Task Bootstrap_TwoConcurrentCreators_BothCompleteAndExactlyOneDatabaseExists()
    {
        // Arrange — started together, not sequenced, so the SERVER produces the duplicate condition
        // rather than the test simulating it.
        var name = TrackName($"p51_race_{_runSuffix}");

        // Act
        var first = RunBootstrapAsync(name);
        var second = RunBootstrapAsync(name);
        var results = await Task.WhenAll(first, second);

        // Assert — neither threw.
        var acceptable = new[]
        {
            DatabaseBootstrapOutcome.Created,
            DatabaseBootstrapOutcome.AlreadyPresent,
            DatabaseBootstrapOutcome.RaceLostToConcurrentCreator,
        };
        results[0].Outcome.ShouldBeOneOf(acceptable);
        results[1].Outcome.ShouldBeOneOf(acceptable);
        results.ShouldContain(r => r.Outcome == DatabaseBootstrapOutcome.Created);

        (await CountDatabasesAsync(name)).ShouldBe(1);
    }

    [Fact]
    public async Task Bootstrap_CreatesOutsideAnyTransaction_ProvenByTheCreateSucceedingAtAll()
    {
        // Arrange — PostgreSQL rejects CREATE DATABASE inside a transaction block with
        // "CREATE DATABASE cannot run inside a transaction block" (SQLSTATE 25001). A create that
        // succeeds against a real server therefore proves the adapter opened a plain connection with
        // no transaction and no EF execution strategy wrapping it in one. This cannot be proven by a
        // unit test, because a double will happily "succeed" either way.
        var name = TrackName($"p51_notx_{_runSuffix}");

        // Act
        var result = await RunBootstrapAsync(name);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.Created);
        (await CountDatabasesAsync(name)).ShouldBe(1);

        // And the control: the same statement INSIDE a transaction really does fail on this server,
        // so the assertion above is not vacuous.
        var inTransaction = await Should.ThrowAsync<PostgresException>(async () =>
        {
            await using var connection = new NpgsqlConnection(MaintenanceConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"p51_intx_{_runSuffix}\"", connection, transaction);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        });
        inTransaction.SqlState.ShouldBe("25001");
    }

    // ------------------------------------------------- task 2: privilege degradation, bounded wait

    [Fact]
    public async Task Bootstrap_AsARoleWithoutTheCreateDatabaseRight_DegradesAndCreatesNothing()
    {
        // Arrange — a REAL role that exists and can connect but holds NOCREATEDB.
        var roleName = $"p51_norights_{_runSuffix}";
        await ExecuteAsync(
            MaintenanceConnectionString,
            $"CREATE ROLE \"{roleName}\" WITH LOGIN NOCREATEDB PASSWORD 'norightspass'");

        var name = $"p51_denied_{_runSuffix}";
        (await CountDatabasesAsync(name)).ShouldBe(0);

        var deniedConnectionString = BuildConnectionString("postgres", roleName, "norightspass");

        // Act — no exception may escape.
        var result = await RunBootstrapAsync(name, deniedConnectionString);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.DegradedNoCreatePrivilege);

        // The degradation must be DISTINGUISHABLE from success, and it must not have created anything.
        result.Outcome.ShouldNotBe(DatabaseBootstrapOutcome.Created);
        result.Outcome.ShouldNotBe(DatabaseBootstrapOutcome.AlreadyPresent);
        (await CountDatabasesAsync(name)).ShouldBe(0);
    }

    [Fact]
    public async Task Bootstrap_AgainstAnUnreachableServer_FailsLoudlyWithinItsBound()
    {
        // Arrange — a loopback port with nothing listening. Three attempts, half a second apart.
        const int attempts = 3;
        var delay = TimeSpan.FromMilliseconds(500);
        var unreachable = "Host=127.0.0.1;Port=1;Database=postgres;Username=nobody;Password=nobody;Timeout=2";

        var bootstrapper = new DatabaseBootstrapper(
            new NpgsqlBootstrapCommandsMirror(unreachable),
            new DatabaseBootstrapOptions("true", $"p51_unreachable_{_runSuffix}", "127.0.0.1", "1", attempts, delay),
            NullLogger<DatabaseBootstrapper>.Instance);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);
        stopwatch.Stop();

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.FailedServerUnreachable);
        result.Attempts.ShouldBe(attempts);

        // BOTH bounds matter. Only the lower bound proves it genuinely retried rather than giving up
        // on the first attempt; only the upper bound proves it genuinely stopped rather than being cut
        // off by the harness. A test asserting neither would pass against a loop doing either wrong
        // thing. Two delays separate three attempts.
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }

    // ------------------------------------------------------------------------------ helpers

    private string TrackName(string name)
    {
        _createdDatabases.Add(name);
        return name;
    }

    private Task<DatabaseBootstrapResult> RunBootstrapAsync(string databaseName, string? connectionString = null)
    {
        var bootstrapper = new DatabaseBootstrapper(
            new NpgsqlBootstrapCommandsMirror(connectionString ?? MaintenanceConnectionString),
            new DatabaseBootstrapOptions("true", databaseName, _container.Hostname, _container.GetMappedPublicPort(5432).ToString(), 10, TimeSpan.FromMilliseconds(200)),
            NullLogger<DatabaseBootstrapper>.Instance);

        return bootstrapper.RunAsync(TestContext.Current.CancellationToken);
    }

    private string BuildConnectionString(string database, string username, string password) =>
        $"Host={_container.Hostname};Port={_container.GetMappedPublicPort(5432)};Database={database};Username={username};Password={password}";

    private async Task<int> CountDatabasesAsync(string name)
    {
        await using var connection = new NpgsqlConnection(MaintenanceConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", connection);
        command.Parameters.AddWithValue("name", name);
        return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A re-statement of the Npgsql adapter that ships in <c>api/Program.cs</c>.
    /// </summary>
    /// <remarks>
    /// It MIRRORS the shipped adapter and is not the shipped adapter. The real one is declared in
    /// <c>api/Program.cs</c> — deliberately, because that file is the repository's one sanctioned
    /// startup-code coverage exclusion and a new <c>api/**/*.cs</c> file holding raw provider I/O could
    /// not reach the 100% per-file floor from <c>tests/unit</c>. A type declared in the entry-point file
    /// cannot be referenced from a test project, so it is re-stated here.
    /// <para>
    /// What keeps the two from drifting is not this comment: it is the recorded end-to-end run of
    /// the SHIPPED binary in <c>51-FINDINGS.md</c> § "Shipped-entry-point evidence", which exercises the
    /// same four paths through the real executable and records its exit codes. If you change either
    /// implementation, re-run that and update the evidence.
    /// </para>
    /// </remarks>
    private sealed class NpgsqlBootstrapCommandsMirror(string maintenanceConnectionString) : IDatabaseBootstrapCommands
    {
        public async Task ProbeServerAsync(CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(maintenanceConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
        }

        public async Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(maintenanceConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
            command.Parameters.AddWithValue("name", databaseName);
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }

        public async Task CreateDatabaseAsync(string quotedIdentifier, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(maintenanceConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand($"CREATE DATABASE {quotedIdentifier}", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
