using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// Mechanises the live-database findings of plan 49-01 so a future change cannot silently reintroduce
/// a second migration history — which is the failure mode a second <c>DbContext</c> would produce.
/// </summary>
/// <remarks>
/// <para>
/// LEAP is one application with submodules: ONE context, ONE model, ONE
/// <c>__EFMigrationsHistory</c> in <c>public</c>, sequential migrations. Plan 49-01 verified that by
/// hand with <c>psql</c>. Hand verification does not survive the next refactor, so it is asserted here
/// at runtime as well.
/// </para>
/// <para>
/// Casing trap (recorded as A-10). The migrations-history TABLE name stays PascalCase
/// (<c>__EFMigrationsHistory</c>) while EF-generated COLUMNS in this database are snake_cased
/// (<c>migration_id</c>, <c>product_version</c>) because of the snake_case naming convention. A query
/// that assumes either casing uniformly is wrong, so the table lookup below is case-INSENSITIVE.
/// </para>
/// </remarks>
public class CompassMigrationHistoryTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    [Fact]
    public async Task MigrationHistory_ExactlyOneTableExists_AndItIsInPublic()
    {
        // Arrange
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act — case-insensitive match, per the A-10 casing split.
        var schemas = await context.Database
            .SqlQueryRaw<string>(
                @"SELECT table_schema AS ""Value""
                  FROM information_schema.tables
                  WHERE table_name ILIKE '%migrationshistory%';")
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert — exactly one migration timeline, and it lives in public.
        schemas.ShouldHaveSingleItem(
            "a second migrations-history table means two migration timelines that can diverge");
        schemas[0].ShouldBe("public");
    }

    [Fact]
    public async Task AppliedMigrations_CreateTheCompassSchema_OnTheSingleContext()
    {
        // Arrange
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var applied = await context.Database.GetAppliedMigrationsAsync(
            TestContext.Current.CancellationToken);

        // Assert — after the Timesheet/Ooto retirement the whole schema (Platform and Compass alike)
        // is created by InitialCreate, plus AddSkills for the technical-skills feature — both on the
        // SAME context and the SAME __EFMigrationsHistory table, which is the property this test
        // actually guards. A count is not the invariant itself (a legitimate new migration is expected
        // to move it); the invariant is that it never forks into a second timeline.
        applied.Count()
            .ShouldBe(
                2,
                "the compass schema must be created by migrations on the single context/single "
                    + "history table, not a diverged second timeline");

        // No trailing semicolon: SingleAsync wraps this in a subquery to check cardinality, and an
        // embedded semicolon there is a syntax error rather than a statement terminator.
        var schemaCount = await context.Database
            .SqlQueryRaw<int>(
                @"SELECT count(*)::int AS ""Value"" FROM information_schema.schemata
                  WHERE schema_name = 'compass'")
            .SingleAsync(TestContext.Current.CancellationToken);
        schemaCount.ShouldBe(1, "the compass schema must exist after the migration runs");
    }

    [Fact]
    public async Task PendingMigrations_AreEmpty_AfterFixtureMigration()
    {
        // Arrange
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var pending = await context.Database.GetPendingMigrationsAsync(
            TestContext.Current.CancellationToken);

        // Assert
        pending.ShouldBeEmpty(
            "the fixture applies the full migration chain; a pending migration means the chain broke");
    }
}
