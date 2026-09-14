using Npgsql;
using Testcontainers.PostgreSql;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Support;

/// <summary>
/// Container start helpers shared by every Testcontainers-backed fixture in this project.
/// </summary>
public static class PostgreSqlContainerExtensions
{
    private static readonly TimeSpan HostConnectivityTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HostConnectivityPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Starts the container and does not return until the container is reachable
    /// from this process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ALWAYS use this instead of a bare <see cref="DockerContainer.StartAsync"/> in a fixture that
    /// connects to the database. Testcontainers' default PostgreSQL wait strategy gates on
    /// <c>pg_isready</c> executed INSIDE the container, so <c>StartAsync</c> can return before the
    /// Docker host is able to reach the published port.
    /// </para>
    /// <para>
    /// On Docker Desktop the two are effectively simultaneous, which is why this is invisible in
    /// CI. On a VM-backed daemon whose published ports traverse a userspace forwarder
    /// (Colima/Lima, Rancher Desktop, WSL2) establishing the forward lands measurably later —
    /// 2.1–2.4s behind in-container readiness when measured on Colima. A fixture that starts a
    /// container and immediately opens a host-side connection to run migrations then dies with
    /// <c>Npgsql.NpgsqlException: Failed to connect to 127.0.0.1:PORT ... Connection refused</c> at
    /// 0ms, and it takes every test in the class down with it because the failure is in
    /// <c>InitializeAsync</c> of a class fixture. The symptom reads as a database or migration
    /// fault and points nowhere near the container runtime.
    /// </para>
    /// <para>
    /// Polling a real connection rather than a bare TCP probe also covers the window where the
    /// forward is live but the server is not yet accepting, so this subsumes the in-container
    /// readiness check instead of merely racing it. On a daemon with no forwarding lag the first
    /// attempt succeeds and the cost is one extra connection open.
    /// </para>
    /// </remarks>
    public static async Task StartAndAwaitHostConnectivityAsync(
        this PostgreSqlContainer container,
        CancellationToken cancellationToken = default)
    {
        await container.StartAsync(cancellationToken);

        var deadline = DateTime.UtcNow.Add(HostConnectivityTimeout);

        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(container.GetConnectionString());
                await connection.OpenAsync(cancellationToken);
                return;
            }
            catch (NpgsqlException ex) when (ex is not PostgresException && DateTime.UtcNow < deadline)
            {
                await Task.Delay(HostConnectivityPollInterval, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="migrate"/>, retrying while it fails with a transient
    /// <see cref="NpgsqlException"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="StartAndAwaitHostConnectivityAsync"/> covers the first connection this process
    /// opens to the container. It does not cover the next one: measured under heavy concurrent
    /// Docker load (several fixtures' Testcontainers Postgres instances starting at once), a
    /// second, separate connection — the one <c>LeapDbContext</c>'s migration step opens —
    /// can still die mid-read with <c>Npgsql.NpgsqlException: Exception while reading from stream
    /// … EndOfStreamException</c>, specifically inside
    /// <c>NpgsqlHistoryRepository.GetAppliedMigrationsAsync</c>. That failure surfaces as a class
    /// fixture's <c>InitializeAsync</c> throwing and reads like a migration or schema fault; it is
    /// the same forwarder/resource-pressure race the connectivity helper's own remarks describe,
    /// just on a later connection.
    /// </para>
    /// <para>
    /// Safe to retry: <see cref="Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync"/>
    /// re-reads the migrations history table and applies only what is still pending, so a retry
    /// after a dropped connection cannot double-apply a migration.
    /// </para>
    /// <para>
    /// The catch excludes <see cref="PostgresException"/> deliberately: it is the server actually
    /// responding (a bad migration statement, a constraint violation), not a dropped transport —
    /// retrying it for up to <see cref="HostConnectivityTimeout"/> would silently delay a real
    /// migration defect instead of surfacing it immediately.
    /// </para>
    /// </remarks>
    public static async Task MigrateWithRetryAsync(
        Func<Task> migrate,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.Add(HostConnectivityTimeout);

        while (true)
        {
            try
            {
                await migrate();
                return;
            }
            catch (NpgsqlException ex) when (ex is not PostgresException && DateTime.UtcNow < deadline)
            {
                await Task.Delay(HostConnectivityPollInterval, cancellationToken);
            }
        }
    }
}
