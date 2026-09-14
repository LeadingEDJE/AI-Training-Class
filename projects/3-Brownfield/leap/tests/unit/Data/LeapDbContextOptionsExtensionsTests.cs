using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

public class LeapDbContextOptionsExtensionsTests
{
    private const string TestConnectionString = "Host=localhost;Database=test;Username=test;Password=test";

    // Issue #208: migration 20260810202407_MoveModuleTablesToSchemas created a Postgres schema
    // named `timesheet` -- the same name as the RDS role every environment connects as. Postgres's
    // default search_path ("$user", public) now resolves "$user" to that schema, so an unqualified
    // relation lookup (notably EF's own "__EFMigrationsHistory") can silently resolve against
    // `timesheet` instead of `public`, where a SEPARATE, empty history table then exists. EF reads
    // that empty table, concludes nothing is applied, and reruns InitialCreate against tables that
    // already exist -- SQLSTATE 42P07, surfaced as the Helm migrate hook's BackoffLimitExceeded.
    private const string ConnectionStringWithTimesheetUserAndSearchPath =
        "Host=localhost;Database=leap_dev;Username=timesheet;Password=test;Search Path=timesheet";

    private static string? EffectiveSearchPath(DbContextOptionsBuilder builder) =>
        builder.Options.Extensions
            .OfType<RelationalOptionsExtension>()
            .Select(e => new Npgsql.NpgsqlConnectionStringBuilder(e.ConnectionString).SearchPath)
            .SingleOrDefault();

    [Fact]
    public void UseLeapPostgres_NonGeneric_ReturnsSameBuilder()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();

        // Act
        var result = builder.UseLeapPostgres(TestConnectionString);

        // Assert — fluent interface must return the same builder
        result.ShouldBeSameAs(builder);
    }

    [Fact]
    public void UseLeapPostgres_NonGeneric_RegistersNpgsqlExtension()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();

        // Act
        builder.UseLeapPostgres(TestConnectionString);

        // Assert — Npgsql provider extension must be registered
        var extensions = builder.Options.Extensions.Select(e => e.GetType().FullName ?? string.Empty);
        extensions.ShouldContain(
            e => e.Contains("NpgsqlOptionsExtension"),
            "UseNpgsql() must register the Npgsql provider extension");
    }

    [Fact]
    public void UseLeapPostgres_NonGeneric_RegistersSnakeCaseNamingConvention()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();

        // Act
        builder.UseLeapPostgres(TestConnectionString);

        // Assert — snake_case naming convention extension must be registered
        var extensions = builder.Options.Extensions.Select(e => e.GetType().FullName ?? string.Empty);
        extensions.ShouldContain(
            e => e.Contains("NamingConventionsOptionsExtension"),
            "UseSnakeCaseNamingConvention() must register the naming-convention extension");
    }

    [Fact]
    public void UseLeapPostgres_Generic_ReturnsSameBuilder()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder<LeapDbContext>();

        // Act
        var result = builder.UseLeapPostgres(TestConnectionString);

        // Assert — fluent interface must return the same generic builder
        result.ShouldBeSameAs(builder);
    }

    [Fact]
    public void UseLeapPostgres_Generic_RegistersNpgsqlExtension()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder<LeapDbContext>();

        // Act
        builder.UseLeapPostgres(TestConnectionString);

        // Assert — Npgsql provider extension must be registered on the generic builder
        var extensions = builder.Options.Extensions.Select(e => e.GetType().FullName ?? string.Empty);
        extensions.ShouldContain(
            e => e.Contains("NpgsqlOptionsExtension"),
            "UseNpgsql() must register the Npgsql provider extension on the generic builder");
    }

    [Fact]
    public void UseLeapPostgres_Generic_RegistersSnakeCaseNamingConvention()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder<LeapDbContext>();

        // Act
        builder.UseLeapPostgres(TestConnectionString);

        // Assert — snake_case naming convention extension must be registered on the generic builder
        var extensions = builder.Options.Extensions.Select(e => e.GetType().FullName ?? string.Empty);
        extensions.ShouldContain(
            e => e.Contains("NamingConventionsOptionsExtension"),
            "UseSnakeCaseNamingConvention() must register the naming-convention extension on the generic builder");
    }

    [Fact]
    public void UseLeapPostgres_NonGeneric_ForcesPublicSearchPath_RegardlessOfIncomingValue()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder();

        // Act
        builder.UseLeapPostgres(ConnectionStringWithTimesheetUserAndSearchPath);

        // Assert — issue #208: the `timesheet` role must never resolve unqualified relations
        // (like "__EFMigrationsHistory") against the `timesheet` schema via Postgres's "$user"
        // default. Forcing `public` here, once, in the one extension both runtime and migrations
        // share, closes that off regardless of what search_path the incoming connection string named.
        EffectiveSearchPath(builder).ShouldBe("public");
    }

    [Fact]
    public void UseLeapPostgres_Generic_ForcesPublicSearchPath_RegardlessOfIncomingValue()
    {
        // Arrange
        var builder = new DbContextOptionsBuilder<LeapDbContext>();

        // Act
        builder.UseLeapPostgres(ConnectionStringWithTimesheetUserAndSearchPath);

        // Assert — same guarantee via the generic overload the design-time factory uses.
        EffectiveSearchPath((DbContextOptionsBuilder)builder).ShouldBe("public");
    }

    [Fact]
    public void UseLeapPostgres_NonGeneric_ForcesPublicSearchPath_WhenIncomingStringNamesNoSearchPathAtAll()
    {
        // Arrange -- the common case: nothing in the connection string opts into non-default
        // resolution, so today this relies entirely on the server-side default -- which issue #208
        // showed is not safe once a schema named after the connecting role exists.
        var builder = new DbContextOptionsBuilder();

        // Act
        builder.UseLeapPostgres(TestConnectionString);

        // Assert
        EffectiveSearchPath(builder).ShouldBe("public");
    }
}
