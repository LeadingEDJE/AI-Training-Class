using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeadingEDJE.Leap.Api.Platform.Data;

/// <summary>
/// Single source of truth for the <see cref="LeapDbContext"/> options chain — Npgsql provider plus
/// snake_case naming — shared by runtime and design-time registration so the two cannot drift.
/// </summary>
public static class LeapDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the options builder with the Npgsql provider and the snake_case naming
    /// convention required by the migrations under <c>Data/Migrations/</c>.
    /// </summary>
    /// <remarks>
    /// It also pins <c>search_path</c> to <c>public</c>. Every environment connects as the role
    /// <c>timesheet</c> and the Timesheet module owns a schema of that same name, so under Postgres's
    /// default <c>"$user", public</c> an unqualified lookup — EF's own <c>"__EFMigrationsHistory"</c>
    /// above all — can resolve in the wrong schema, which makes EF conclude nothing was applied and
    /// rerun <c>InitialCreate</c> against existing tables. Pinning it here covers both the runtime and
    /// the design-time path. Do not add <c>MigrationsHistoryTable(...)</c> instead:
    /// <c>scripts/check-one-context.sh</c> asserts zero of those.
    /// </remarks>
    public static DbContextOptionsBuilder UseLeapPostgres(
        this DbContextOptionsBuilder builder, string connectionString)
    {
        var pinnedConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = "public",
        }.ConnectionString;

        return builder.UseNpgsql(pinnedConnectionString)
                      .UseSnakeCaseNamingConvention();
    }

    /// <inheritdoc cref="UseLeapPostgres(DbContextOptionsBuilder, string)" />
    public static DbContextOptionsBuilder<TContext> UseLeapPostgres<TContext>(
        this DbContextOptionsBuilder<TContext> builder, string connectionString)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)builder).UseLeapPostgres(connectionString);
        return builder;
    }
}
