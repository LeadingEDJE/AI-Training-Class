using System.Text.RegularExpressions;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LeadingEDJE.Leap.Api.Platform.Data;

/// <summary>What the bootstrap actually did. Modelled explicitly rather than as a boolean so two different meanings can never collapse into one <c>true</c>.</summary>
public enum DatabaseBootstrapOutcome
{
    /// <summary>The gate was set to a false value, so nothing was attempted.</summary>
    SkippedGateDisabled,

    /// <summary>The configured name is not a valid identifier and was rejected before any statement was built.</summary>
    RejectedInvalidName,

    /// <summary>The server never became reachable within the configured bound. A hard failure and the only outcome that exits non-zero.</summary>
    FailedServerUnreachable,

    /// <summary>The database was already there; no create was issued.</summary>
    AlreadyPresent,

    /// <summary>The database was absent and this run created it.</summary>
    Created,

    /// <summary>A concurrent bootstrap created it first. Benign — the database exists either way.</summary>
    RaceLostToConcurrentCreator,

    /// <summary>The caller lacks the create-database right. The deploy continues, because a least-privilege deployment already has the database.</summary>
    DegradedNoCreatePrivilege,
}

/// <summary>Everything the bootstrap needs, supplied by the composition root.</summary>
/// <param name="enabledSetting">The raw configuration value of the gate. Deliberately a string, not a bool, so "absent or unparseable means on" is the orchestrator's testable behaviour rather than the caller's.</param>
/// <param name="databaseName">The database the application is configured to use.</param>
/// <param name="host">The server host, used only in log and result messages.</param>
/// <param name="port">The server port, used only in log and result messages.</param>
/// <param name="waitAttempts">How many times to probe the server before giving up.</param>
/// <param name="waitDelay">How long to wait between probes.</param>
public sealed class DatabaseBootstrapOptions(
    string? enabledSetting,
    string databaseName,
    string host,
    string port,
    int waitAttempts,
    TimeSpan waitDelay)
{
    /// <summary>The raw gate value. Absent or unparseable means on.</summary>
    public string? EnabledSetting { get; } = enabledSetting;

    /// <summary>The database the application is configured to use.</summary>
    public string DatabaseName { get; } = databaseName;

    /// <summary>The server host.</summary>
    public string Host { get; } = host;

    /// <summary>The server port.</summary>
    public string Port { get; } = port;

    /// <summary>How many times to probe before declaring the server unreachable.</summary>
    public int WaitAttempts { get; } = waitAttempts;

    /// <summary>How long to wait between probes.</summary>
    public TimeSpan WaitDelay { get; } = waitDelay;
}

/// <summary>The outcome plus the evidence a caller needs to log or act on it.</summary>
/// <param name="outcome">What happened.</param>
/// <param name="attempts">How many server probes were made.</param>
/// <param name="message">A human-readable single line suitable for a deploy log.</param>
public sealed class DatabaseBootstrapResult(DatabaseBootstrapOutcome outcome, int attempts, string message)
{
    /// <summary>What happened.</summary>
    public DatabaseBootstrapOutcome Outcome { get; } = outcome;

    /// <summary>How many server probes were made.</summary>
    public int Attempts { get; } = attempts;

    /// <summary>A human-readable single line suitable for a deploy log.</summary>
    public string Message { get; } = message;
}

/// <summary>
/// Waits for the database server, then creates the configured database if absent — idempotently,
/// tolerantly of a concurrent creator, and degrading rather than crashing without create rights.
/// </summary>
/// <remarks>
/// This type performs no input or output of its own; every server interaction goes through
/// <see cref="IDatabaseBootstrapCommands"/>, which is what makes full line coverage reachable from
/// the unit suite without a coverage exclusion. It is the deploy Job's init step, and the EF
/// migration bundle that follows it does not retry — pointed at an unreachable server it exits 1 in
/// about two seconds — so the bounded wait lives here rather than there.
/// </remarks>
/// <param name="commands">The command seam. The only route to the server.</param>
/// <param name="options">Connection inputs, the gate and the wait bounds.</param>
/// <param name="logger">Structured logger.</param>
public sealed partial class DatabaseBootstrapper(
    IDatabaseBootstrapCommands commands,
    DatabaseBootstrapOptions options,
    ILogger<DatabaseBootstrapper> logger)
{
    // PostgreSQL truncates identifiers at NAMEDATALEN-1. A longer name would be silently truncated by
    // the server, so two different configured names could resolve to the same database.
    private const int MaxIdentifierLength = 63;

    /// <summary>Runs the bootstrap and reports what it did. Only <see cref="DatabaseBootstrapOutcome.FailedServerUnreachable"/> is a failure.</summary>
    /// <param name="cancellationToken">Stops the wait loop promptly rather than letting it run to its bound.</param>
    public async Task<DatabaseBootstrapResult> RunAsync(CancellationToken cancellationToken)
    {
        // Mirrors the StartupTasks:SeedReferenceData gate in api/Program.cs: absent or unparseable means ON.
        var enabled = !bool.TryParse(options.EnabledSetting, out var parsed) || parsed;
        if (!enabled)
        {
            logger.LogInformation("Database bootstrap skipped because StartupTasks:EnsureDatabase is false");
            return new DatabaseBootstrapResult(
                DatabaseBootstrapOutcome.SkippedGateDisabled,
                0,
                "Database bootstrap skipped: StartupTasks:EnsureDatabase is false.");
        }

        // Validation runs FIRST — before anything that could build a statement. CREATE DATABASE cannot
        // take a parameter, so the configured name is the one value in this phase that crosses from a
        // chart value into raw SQL.
        var databaseName = options.DatabaseName;
        if (!IsValidIdentifier(databaseName))
        {
            logger.LogError(
                "Configured database name is not an acceptable identifier and was rejected: {DatabaseName}",
                LogSanitizer.Clean(databaseName));
            return new DatabaseBootstrapResult(
                DatabaseBootstrapOutcome.RejectedInvalidName,
                0,
                "Configured database name is not an acceptable identifier; refusing to build a statement from it.");
        }

        var (reachable, attempts) = await WaitForServerAsync(cancellationToken);
        if (!reachable)
        {
            var failure =
                $"Database server {options.Host}:{options.Port} was not reachable after {attempts} attempts.";
            logger.LogError(
                "Database server {Host}:{Port} was not reachable after {Attempts} attempts; failing loudly rather than reporting success against a database that was never reached",
                LogSanitizer.Clean(options.Host),
                LogSanitizer.Clean(options.Port),
                attempts);

            // Deliberately does not fall through to a create. A create against a server we could not
            // reach would either fail confusingly or, worse, appear to work.
            return new DatabaseBootstrapResult(DatabaseBootstrapOutcome.FailedServerUnreachable, attempts, failure);
        }

        if (await commands.DatabaseExistsAsync(databaseName, cancellationToken))
        {
            logger.LogInformation(
                "Database {DatabaseName} already exists; no create issued",
                LogSanitizer.Clean(databaseName));
            return new DatabaseBootstrapResult(
                DatabaseBootstrapOutcome.AlreadyPresent,
                attempts,
                $"Database {databaseName} already exists.");
        }

        try
        {
            await commands.CreateDatabaseAsync(QuoteIdentifier(databaseName), cancellationToken);
        }
        // Classified by SQLSTATE, never by message text, which is localised and version-dependent.
        // The loser of a CREATE DATABASE race gets either 42P04 duplicate_database, when the server's
        // own existence check sees it at statement start, or 23505 unique_violation, when two
        // statements overlap and it collides on the index over pg_database. 23505 is generic, so
        // existence is re-checked and the exception rethrown if the database really is absent.
        catch (PostgresException ex) when (
            ex.SqlState == PostgresErrorCodes.DuplicateDatabase ||
            ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            var confirmedPresent = await commands.DatabaseExistsAsync(databaseName, cancellationToken);
            if (!confirmedPresent)
            {
                // The condition looked like a race but the database is genuinely not there, so it was
                // not one. Do not report a success that did not happen.
                return RethrowUnconfirmedRace(ex);
            }

            logger.LogInformation(
                ex,
                "Database {DatabaseName} was created concurrently by another bootstrap ({SqlState}); confirmed present, treating as already present",
                LogSanitizer.Clean(databaseName),
                LogSanitizer.Clean(ex.SqlState));
            return new DatabaseBootstrapResult(
                DatabaseBootstrapOutcome.RaceLostToConcurrentCreator,
                attempts,
                $"Database {databaseName} was created concurrently; treating as already present.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            logger.LogWarning(
                ex,
                "This role cannot create databases, so {DatabaseName} was not created; continuing, because a least-privilege deployment already has it. The migration step will fail loudly if it does not",
                LogSanitizer.Clean(databaseName));
            return new DatabaseBootstrapResult(
                DatabaseBootstrapOutcome.DegradedNoCreatePrivilege,
                attempts,
                $"No permission to create {databaseName}; continuing on the assumption it already exists.");
        }

        logger.LogInformation("Created database {DatabaseName}", LogSanitizer.Clean(databaseName));
        return new DatabaseBootstrapResult(
            DatabaseBootstrapOutcome.Created,
            attempts,
            $"Created database {databaseName}.");
    }

    private async Task<(bool Reachable, int Attempts)> WaitForServerAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= options.WaitAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await commands.ProbeServerAsync(cancellationToken);
                return (true, attempt);
            }
            catch (OperationCanceledException)
            {
                // A cancelled probe is cancellation, not an unreachable server. Rethrowing keeps the two
                // apart; swallowing it here would log a misleading "not reachable" warning and then burn
                // the remaining attempts.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Database server {Host}:{Port} not reachable on attempt {Attempt}/{Attempts}",
                    LogSanitizer.Clean(options.Host),
                    LogSanitizer.Clean(options.Port),
                    attempt,
                    options.WaitAttempts);

                if (attempt < options.WaitAttempts)
                {
                    await Task.Delay(options.WaitDelay, cancellationToken);
                }
            }
        }

        return (false, options.WaitAttempts);
    }

    /// <summary>
    /// Rethrows a race-shaped condition that could not be confirmed.
    /// </summary>
    /// <remarks>
    /// A method rather than a bare <c>throw;</c>: a <c>throw</c> inside a <c>catch</c> is followed by a
    /// closing brace whose async-state-machine sequence point can never execute, leaving one
    /// permanently uncovered line, and this file must reach 100% from the unit suite alone. An
    /// expression-bodied <c>throw</c> has no trailing brace. Accepted cost: <c>throw ex</c> restarts the
    /// stack trace. The exception object is unchanged — type, message, <c>SqlState</c>, <c>Data</c> and
    /// inner exception — and this is a one-shot process doing one operation, so the lost frames tell a
    /// reader nothing new. The alternative was an uncoverable line or a new coverage exclusion.
    /// </remarks>
    private static DatabaseBootstrapResult RethrowUnconfirmedRace(PostgresException ex) => throw ex;

    /// <summary>Quotes the identifier for the create statement.</summary>
    /// <remarks>
    /// The second of two independent locks, deliberate rather than redundant.
    /// <see cref="IsValidIdentifier"/> has already rejected every character that could escape the
    /// quotes, so the doubling here can never fire today. Do not delete either lock because the other
    /// makes it unnecessary — the point is that loosening one later cannot silently become an
    /// injection.
    /// </remarks>
    private static string QuoteIdentifier(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static bool IsValidIdentifier(string name) =>
        name.Length > 0 && name.Length <= MaxIdentifierLength && IdentifierPattern().IsMatch(name);

    // Anchored and conservative: a leading letter or underscore, then letters, digits and underscores.
    // Rejects quotes, semicolons, whitespace, hyphens and everything non-ASCII. A rejection is loud and
    // costs a deploy; a permissive pattern here costs arbitrary SQL run as the database owner.
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentifierPattern();
}
