using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

/// <summary>
/// Every branch of the database bootstrap, driven by a hand-written in-memory double rather than a
/// mocking framework.
/// </summary>
/// <remarks>
/// The two server conditions the orchestrator classifies are constructed as real
/// <see cref="PostgresException"/> instances carrying their SQLSTATE codes, NOT matched on message
/// text — message text is localised and version-dependent; the condition code is the contract. The
/// same two conditions are re-proven against a real PostgreSQL server in
/// <c>tests/integration/Data/DatabaseBootstrapperIntegrationTests.cs</c>, because a double only ever
/// agrees with whatever its author believed the server does.
/// </remarks>
public class DatabaseBootstrapperTests
{
    private const string ValidName = "leap_dev_9";

    private static DatabaseBootstrapOptions Options(
        string? enabledSetting = "true",
        string databaseName = ValidName,
        int waitAttempts = 3,
        int waitDelayMilliseconds = 0) =>
        new(
            enabledSetting,
            databaseName,
            host: "db.example.test",
            port: "5432",
            waitAttempts,
            TimeSpan.FromMilliseconds(waitDelayMilliseconds));

    private static DatabaseBootstrapper Bootstrapper(FakeBootstrapCommands commands, DatabaseBootstrapOptions options) =>
        new(commands, options, NullLogger<DatabaseBootstrapper>.Instance);

    // ---------------------------------------------------------------- the gate

    [Fact]
    public async Task RunAsync_GateSetToFalse_SkipsWithoutAttemptingAnything()
    {
        // Arrange
        var commands = new FakeBootstrapCommands();
        var bootstrapper = Bootstrapper(commands, Options(enabledSetting: "false"));

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.SkippedGateDisabled);
        commands.ProbeCount.ShouldBe(0);
        commands.ExistsCount.ShouldBe(0);
        commands.CreateCount.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_GateAbsent_TreatsItAsOn()
    {
        // Arrange
        var commands = new FakeBootstrapCommands { DatabaseExists = true };
        var bootstrapper = Bootstrapper(commands, Options(enabledSetting: null));

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.AlreadyPresent);
        commands.ProbeCount.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_GateUnparseable_TreatsItAsOn()
    {
        // Arrange
        var commands = new FakeBootstrapCommands { DatabaseExists = true };
        var bootstrapper = Bootstrapper(commands, Options(enabledSetting: "not-a-boolean"));

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.AlreadyPresent);
        commands.ProbeCount.ShouldBe(1);
    }

    // ------------------------------------------------------------ name validation

    [Fact]
    public async Task RunAsync_PlainLowercaseNameWithDigitsAndUnderscores_IsAccepted()
    {
        // Arrange
        var commands = new FakeBootstrapCommands { DatabaseExists = false };
        var bootstrapper = Bootstrapper(commands, Options(databaseName: "leap_dev_9"));

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.Created);
    }

    [Theory]
    [InlineData("leap\"dev")]          // an embedded double quote — the identifier-escape vector
    [InlineData("leap';DROP DATABASE timesheet_dev;--")] // a statement terminator
    [InlineData("leap dev")]           // an embedded space
    [InlineData("-leap")]              // a leading hyphen
    [InlineData("9leap")]              // a leading digit
    [InlineData("")]                   // empty
    [InlineData("leap-dev")]           // an embedded hyphen
    [InlineData("leap\ndev")]          // an embedded newline
    public async Task RunAsync_InvalidName_IsRejectedBeforeAnyCommandIsBuilt(string databaseName)
    {
        // Arrange
        var commands = new FakeBootstrapCommands();
        var bootstrapper = Bootstrapper(commands, Options(databaseName: databaseName));

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.RejectedInvalidName);
        commands.ProbeCount.ShouldBe(0);
        commands.ExistsCount.ShouldBe(0);
        commands.CreateCount.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_NameLongerThanTheServerIdentifierLimit_IsRejected()
    {
        // Arrange — PostgreSQL truncates identifiers at NAMEDATALEN-1 = 63 bytes.
        var commands = new FakeBootstrapCommands();
        var bootstrapper = Bootstrapper(commands, Options(databaseName: new string('a', 64)));

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.RejectedInvalidName);
        commands.ProbeCount.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_NameExactlyAtTheServerIdentifierLimit_IsAccepted()
    {
        // Arrange
        var commands = new FakeBootstrapCommands { DatabaseExists = true };
        var bootstrapper = Bootstrapper(commands, Options(databaseName: new string('a', 63)));

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.AlreadyPresent);
    }

    // ------------------------------------------------------------------ the wait

    [Fact]
    public async Task RunAsync_ServerReachableImmediately_TakesNoDelayAndRecordsOneAttempt()
    {
        // Arrange
        var commands = new FakeBootstrapCommands { DatabaseExists = true };
        var bootstrapper = Bootstrapper(commands, Options(waitDelayMilliseconds: 30_000));

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.AlreadyPresent);
        result.Attempts.ShouldBe(1);
        commands.ProbeCount.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_ServerReachableOnTheThirdAttempt_SucceedsAndRecordsThreeAttempts()
    {
        // Arrange
        var commands = new FakeBootstrapCommands { DatabaseExists = true, ProbeFailuresBeforeSuccess = 2 };
        var bootstrapper = Bootstrapper(commands, Options(waitAttempts: 5));

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.AlreadyPresent);
        result.Attempts.ShouldBe(3);
        commands.ProbeCount.ShouldBe(3);
    }

    [Fact]
    public async Task RunAsync_ServerNeverReachable_FailsLoudlyAndAttemptsNoCreate()
    {
        // Arrange
        var commands = new FakeBootstrapCommands { ProbeFailuresBeforeSuccess = int.MaxValue };
        var bootstrapper = Bootstrapper(commands, Options(waitAttempts: 4));

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.FailedServerUnreachable);
        result.Attempts.ShouldBe(4);
        result.Message.ShouldContain("db.example.test");
        result.Message.ShouldContain("4");
        commands.ProbeCount.ShouldBe(4);
        commands.ExistsCount.ShouldBe(0);
        commands.CreateCount.ShouldBe(0);
    }

    // ------------------------------------------------------------ create / already present

    [Fact]
    public async Task RunAsync_DatabaseAlreadyPresent_IssuesNoCreate()
    {
        // Arrange
        var commands = new FakeBootstrapCommands { DatabaseExists = true };
        var bootstrapper = Bootstrapper(commands, Options());

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.AlreadyPresent);
        commands.ExistsCount.ShouldBe(1);
        commands.CreateCount.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_DatabaseAbsent_IssuesExactlyOneCreateWithAQuotedIdentifier()
    {
        // Arrange
        var commands = new FakeBootstrapCommands { DatabaseExists = false };
        var bootstrapper = Bootstrapper(commands, Options());

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.Created);
        commands.CreateCount.ShouldBe(1);
        commands.LastCreatedIdentifier.ShouldBe("\"leap_dev_9\"");
        commands.LastCheckedName.ShouldBe("leap_dev_9");
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DuplicateDatabase)] // 42P04 — the server's own existence check caught it
    [InlineData(PostgresErrorCodes.UniqueViolation)]   // 23505 — a genuinely overlapping create collided on pg_database's unique index
    public async Task RunAsync_ConcurrentCreatorWins_IsTreatedAsAlreadyPresentAndNoExceptionEscapes(string sqlState)
    {
        // Arrange — a losing racer receives EITHER code depending on how far its statement got. That
        // is not a guess: a real PostgreSQL server produced 23505 under a real concurrent race in
        // DatabaseBootstrapperIntegrationTests, while this double had only ever been programmed with
        // 42P04.
        var commands = new FakeBootstrapCommands
        {
            DatabaseExists = false,
            ExistsOnRecheck = true,
            CreateThrows = PostgresError(sqlState),
        };
        var bootstrapper = Bootstrapper(commands, Options());

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.RaceLostToConcurrentCreator);
        commands.CreateCount.ShouldBe(1);

        // The classification is CONFIRMED, not assumed: the orchestrator re-checks existence.
        commands.ExistsCount.ShouldBe(2);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DuplicateDatabase)]
    [InlineData(PostgresErrorCodes.UniqueViolation)]
    public async Task RunAsync_RaceLikeConditionButDatabaseStillAbsent_PropagatesInsteadOfSwallowing(string sqlState)
    {
        // Arrange — 23505 is a GENERIC unique violation. If the database is genuinely still not there,
        // this was not a race and must not be silently reported as success.
        var commands = new FakeBootstrapCommands
        {
            DatabaseExists = false,
            ExistsOnRecheck = false,
            CreateThrows = PostgresError(sqlState),
        };
        var bootstrapper = Bootstrapper(commands, Options());

        // Act / Assert
        var caught = await Should.ThrowAsync<PostgresException>(
            () => bootstrapper.RunAsync(TestContext.Current.CancellationToken));
        caught.SqlState.ShouldBe(sqlState);
    }

    [Fact]
    public async Task RunAsync_CallerHasNoCreateDatabaseRight_DegradesAndSaysTheDeployIsContinuing()
    {
        // Arrange — 42501 insufficient_privilege.
        var commands = new FakeBootstrapCommands
        {
            DatabaseExists = false,
            CreateThrows = PostgresError(PostgresErrorCodes.InsufficientPrivilege),
        };
        var bootstrapper = Bootstrapper(commands, Options());

        // Act
        var result = await bootstrapper.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Outcome.ShouldBe(DatabaseBootstrapOutcome.DegradedNoCreatePrivilege);
        result.Message.ShouldContain("continuing");
    }

    [Fact]
    public async Task RunAsync_UnclassifiedServerError_PropagatesUnchanged()
    {
        // Arrange — 53300 too_many_connections is neither classified condition.
        var thrown = PostgresError(PostgresErrorCodes.TooManyConnections);
        var commands = new FakeBootstrapCommands { DatabaseExists = false, CreateThrows = thrown };
        var bootstrapper = Bootstrapper(commands, Options());

        // Act / Assert
        var caught = await Should.ThrowAsync<PostgresException>(
            () => bootstrapper.RunAsync(TestContext.Current.CancellationToken));
        caught.SqlState.ShouldBe(PostgresErrorCodes.TooManyConnections);
    }

    // --------------------------------------------------------------- cancellation

    [Fact]
    public async Task RunAsync_TokenAlreadyCancelled_StopsBeforeProbingRatherThanRunningToTheBound()
    {
        // Arrange
        var commands = new FakeBootstrapCommands { ProbeFailuresBeforeSuccess = int.MaxValue };
        var bootstrapper = Bootstrapper(commands, Options(waitAttempts: 1000, waitDelayMilliseconds: 1000));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act / Assert
        await Should.ThrowAsync<OperationCanceledException>(() => bootstrapper.RunAsync(cancelled.Token));
        commands.ProbeCount.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_TokenCancelledDuringTheWait_StopsPromptly()
    {
        // Arrange
        using var cancelled = new CancellationTokenSource();
        var commands = new FakeBootstrapCommands
        {
            ProbeFailuresBeforeSuccess = int.MaxValue,
            OnProbe = () => cancelled.Cancel(),
        };
        var bootstrapper = Bootstrapper(commands, Options(waitAttempts: 1000, waitDelayMilliseconds: 1000));

        // Act / Assert
        await Should.ThrowAsync<OperationCanceledException>(() => bootstrapper.RunAsync(cancelled.Token));
        commands.ProbeCount.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_ProbeItselfObservesCancellation_PropagatesInsteadOfLoggingAnUnreachableServer()
    {
        // Arrange — cancellation surfacing from inside the probe is cancellation, not an unreachable
        // server. Swallowing it would log a misleading warning and burn the remaining attempts.
        var commands = new FakeBootstrapCommands
        {
            OnProbe = () => throw new OperationCanceledException("deliberate probe cancellation"),
        };
        var bootstrapper = Bootstrapper(commands, Options(waitAttempts: 1000, waitDelayMilliseconds: 1000));

        // Act / Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => bootstrapper.RunAsync(TestContext.Current.CancellationToken));
        commands.ProbeCount.ShouldBe(1);
    }

    /// <summary>Builds a real provider exception carrying the given SQLSTATE, so classification is proven against the code rather than against message text.</summary>
    private static PostgresException PostgresError(string sqlState) =>
        new("deliberate test condition", "ERROR", "ERROR", sqlState);

    /// <summary>Hand-written in-memory double for the command seam. Records what it was asked to do and can be programmed to throw a specific server condition.</summary>
    private sealed class FakeBootstrapCommands : IDatabaseBootstrapCommands
    {
        public int ProbeCount { get; private set; }
        public int ExistsCount { get; private set; }
        public int CreateCount { get; private set; }
        public string? LastCreatedIdentifier { get; private set; }
        public string? LastCheckedName { get; private set; }

        public bool DatabaseExists { get; init; }

        /// <summary>What the SECOND and later existence checks return, so the post-race confirmation can be programmed independently of the first check.</summary>
        public bool? ExistsOnRecheck { get; init; }

        public int ProbeFailuresBeforeSuccess { get; init; }
        public Exception? CreateThrows { get; init; }
        public Action? OnProbe { get; init; }

        public Task ProbeServerAsync(CancellationToken cancellationToken)
        {
            ProbeCount++;
            OnProbe?.Invoke();
            if (ProbeCount <= ProbeFailuresBeforeSuccess)
            {
                throw new InvalidOperationException("deliberate probe failure");
            }

            return Task.CompletedTask;
        }

        public Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken cancellationToken)
        {
            ExistsCount++;
            LastCheckedName = databaseName;
            return Task.FromResult(ExistsCount > 1 && ExistsOnRecheck is not null ? ExistsOnRecheck.Value : DatabaseExists);
        }

        public Task CreateDatabaseAsync(string quotedIdentifier, CancellationToken cancellationToken)
        {
            CreateCount++;
            LastCreatedIdentifier = quotedIdentifier;
            if (CreateThrows is not null)
            {
                throw CreateThrows;
            }

            return Task.CompletedTask;
        }
    }
}
