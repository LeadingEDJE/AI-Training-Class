using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace LeadingEDJE.Leap.Api.Platform.HealthChecks;

/// <summary>
/// Readiness probe that verifies PostgreSQL connectivity with a lightweight SELECT 1 query.
/// </summary>
/// <remarks>
/// Deliberately a raw <see cref="NpgsqlConnection"/> probe rather than an EF Core
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/>-backed one, so readiness stays isolated from
/// the EF request-path stack and a broken request path cannot mask or trigger a readiness failure. Do
/// not consolidate this onto <c>LeapDbContext</c>.
/// </remarks>
public class NpgsqlHealthCheck(IConfiguration configuration) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = BuildConnectionString(configuration);
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("PostgreSQL connection successful");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection failed", ex);
        }
    }

    private static string BuildConnectionString(IConfiguration config)
    {
        var host = config["POSTGRES_HOST"] ?? "127.0.0.1";
        var port = config["POSTGRES_PORT"] ?? "5432";
        // Must stay identical to the defaults in api/Program.cs and DesignTimeDbContextFactory --
        // DatabaseNameDefaultsAgreeTests fails if they drift. A readiness probe reporting healthy
        // against a different database than the application uses is worse than no probe.
        var database = config["POSTGRES_DB"] ?? "leap_dev";
        var user = config["POSTGRES_USER"] ?? "timesheet";
        var password = config["POSTGRES_PASSWORD"] ?? "";

        return $"Host={host};Port={port};Database={database};Username={user};Password={password}";
    }
}
