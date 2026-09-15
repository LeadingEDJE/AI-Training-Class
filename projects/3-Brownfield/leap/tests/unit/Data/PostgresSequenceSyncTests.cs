using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Data.SeedData;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

/// <summary>
/// Unit-level coverage for <see cref="PostgresSequenceSync"/>. The live MAX(id) resync is exercised by
/// the Testcontainers integration suite; here we cover the DB-free statement-shaping (against a real
/// Npgsql model, no connection opened) and the non-relational (InMemory) early-return guard.
/// </summary>
public class PostgresSequenceSyncTests
{
    private static LeapDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new LeapDbContext(options);
    }

    private static LeapDbContext CreateNpgsqlModelContext()
    {
        // A bogus connection string is fine: reading Model never opens a connection, and the snake_case
        // Npgsql model is exactly what BuildResyncStatements shapes SQL against.
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseLeapPostgres("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        return new LeapDbContext(options);
    }

    [Fact]
    public async Task ResyncIdentitySequencesAsync_OnNonRelationalProvider_ReturnsWithoutError()
    {
        // Arrange — InMemory provider is not relational, so the resync must no-op.
        using var context = CreateInMemoryContext();
        var ct = TestContext.Current.CancellationToken;

        // Act
        await PostgresSequenceSync.ResyncIdentitySequencesAsync(context, ct);

        // Assert — the guard short-circuited before any SQL was attempted.
        context.Database.IsRelational().ShouldBeFalse();
    }

    [Fact]
    public void BuildResyncStatements_ForNpgsqlModel_EmitsSetvalForIntIdentityTables()
    {
        // Arrange
        using var context = CreateNpgsqlModelContext();

        // Act
        var statements = PostgresSequenceSync.BuildResyncStatements(context.Model);

        // Assert — every int/long identity-keyed table (e.g. user_roles) gets one setval statement.
        statements.ShouldNotBeEmpty();
        // Unqualified: user_roles is a `public`-schema Platform table (int Id, ValueGeneratedOnAdd),
        // and Qualify() only schema-prefixes a non-null schema. Timesheet's time_categories, which
        // this used to pin, is gone with the module.
        statements.ShouldContain(s => s.Contains("'\"user_roles\"'"));
        statements.ShouldAllBe(s => s.StartsWith("SELECT setval(pg_get_serial_sequence(") && s.EndsWith(");"));
    }

    [Fact]
    public void BuildResyncStatements_ForCompassTables_SchemaQualifiesEveryIdentifier()
    {
        // Arrange — the Compass ERD entities are the first in this model mapped outside `public`.
        using var context = CreateNpgsqlModelContext();

        // Act
        var statements = PostgresSequenceSync.BuildResyncStatements(context.Model);

        // Assert — every compass statement must name the schema, in BOTH the pg_get_serial_sequence
        // argument and the FROM clause. Unqualified, they resolve through search_path to a
        // non-existent public.employee and the whole seed/import path errors — which is exactly how
        // this surfaced: 7 unrelated integration tests failed the moment compass tables were mapped.
        var compassStatements = statements.Where(s => s.Contains("compass")).ToList();
        compassStatements.Count.ShouldBe(
            9, "the ERD's seven tables plus the technical-skills feature's two (skill, "
                + "employee_skill) are all int-identity keyed");
        compassStatements.ShouldAllBe(s => s.Contains("'\"compass\".\"") && s.Contains("FROM \"compass\".\""));
        statements.ShouldNotContain(s => s.Contains("'\"employee\"'"), "must never be unqualified");
    }

    [Fact]
    public void BuildResyncStatements_ForNpgsqlModel_SkipsGuidKeyedTables()
    {
        // Arrange
        using var context = CreateNpgsqlModelContext();

        // Act
        var statements = PostgresSequenceSync.BuildResyncStatements(context.Model);

        // Assert — absorbed GUID-keyed tables (people/employees/clients) are NOT int identities, so no
        // setval is generated for them (guard on int/long + OnAdd filters them out).
        statements.ShouldNotContain(s => s.Contains("'\"people\"'") || s.Contains("'\"employees\"'"));
    }
}
