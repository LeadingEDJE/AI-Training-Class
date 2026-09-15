using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

// Compass and Platform both have a stake in "Employee"-shaped naming, so alias the one this file is
// actually about rather than relying on which `using` happens to win.
using CompassEmployee = LeadingEDJE.Leap.Api.Modules.Compass.Employee;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// Proves the <c>compass</c> schema is an organizational NAMESPACE inside one database, one context
/// and one transaction — not a boundary.
/// </summary>
/// <remarks>
/// <para>
/// Rewritten for issue #50, as its own predecessor instructed. This file previously proved the
/// property with a probe table created and rolled back inside a transaction, because Compass owned no
/// tables and had to be handed to the incoming team as an empty schema. The ERD schema now exists, so
/// the evidence is a join and a save across two REAL tables in two different schemas — which the
/// original file explicitly called "strictly better evidence" and asked its successor to write.
/// </para>
/// <para>
/// It also drops the old <c>CompassSchema_RemainsEmpty_AfterTheProof</c> assertion, which is now false
/// BY DESIGN: the schema holds the ERD's seven tables. The replacement invariant — that it holds
/// exactly those seven and nothing speculative — lives in <c>CompassSchemaFromErdTests</c>.
/// </para>
/// <para>
/// What this file really guards is the one-context decision. A second <c>DbContext</c> for Compass
/// would break every assertion here: no cross-schema join in one query, and no single
/// <c>SaveChangesAsync</c> spanning both modules.
/// </para>
/// <para>
/// Note also what these tests demonstrate incidentally: raw SQL is NOT schema-aware automatically, so
/// every identifier below is explicitly schema-qualified. EF-generated SQL always qualifies; anything
/// hand-written must do so itself.
/// </para>
/// </remarks>
public class CompassSchemaIsANamespaceTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CompassAndPublicTables_JoinInASingleQuery()
    {
        // Arrange — a real row in compass.employee and a real row in public.people.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var anchor = new Person { Id = Guid.NewGuid(), Email = "cross-schema-anchor@leadingedje.com", IsActive = true };
        context.People.Add(anchor);

        var employeeType = new EmployeeType { TypeName = "Full Time", IsActive = true };
        context.Set<EmployeeType>().Add(employeeType);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var employee = new CompassEmployee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            HireDate = new DateOnly(2026, 1, 5),
            Email = "ada.lovelace@leadingedje.com",
            EmployeeTypeId = employeeType.Id,
            IsActive = true,
            StateOfResidence = "OH",
        };
        context.Set<CompassEmployee>().Add(employee);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act — ONE query spanning compass.* and public.*. SqlQuery (not SqlQueryRaw) parameterises
        // the interpolated ids; SqlQueryRaw with an inline interpolated string trips EF1002.
        var joined = await context
            .Database.SqlQuery<int>(
                $@"SELECT count(*)::int AS ""Value""
                   FROM ""compass"".""employee"" e
                   CROSS JOIN ""public"".""people"" p
                   WHERE e.employee_id = {employee.Id} AND p.id = {anchor.Id}"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        joined.ShouldHaveSingleItem();
        joined[0]
            .ShouldBe(
                1,
                "a compass table must join to a public table in one query — the schema is a "
                    + "namespace, not a boundary"
            );
    }

    [Fact]
    public async Task OneSaveChanges_WritesBothSchemasInTheSameTransaction()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act — a public entity and a compass entity added together and flushed by a SINGLE
        // SaveChangesAsync. This is precisely what a second DbContext would make impossible.
        context.People.Add(new Person { Id = Guid.NewGuid(), Email = "one-txn-anchor@leadingedje.com", IsActive = true });
        context.Set<EmployeeType>().Add(new EmployeeType { TypeName = "1099", IsActive = true });

        var written = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        written.ShouldBe(2, "both rows must be written by one SaveChangesAsync");

        var compassRows = await context
            .Set<EmployeeType>()
            .CountAsync(TestContext.Current.CancellationToken);
        var publicRows = await context.People.CountAsync(
            TestContext.Current.CancellationToken
        );
        compassRows.ShouldBe(1);
        publicRows.ShouldBe(1);
    }

    [Fact]
    public async Task CompassEntities_AreReachedWithoutADbSetOnTheContext()
    {
        // Arrange — Compass owns no DbSet property on
        // LeapDbContext, so the Platform layer never imports the Compass namespace. Set<T>() is the
        // access path, and this test is what stops someone "helpfully" adding DbSets later.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var compassMappedTables = context
            .Model.GetEntityTypes()
            .Where(t => t.GetSchema() == "compass")
            .Select(t => t.GetTableName())
            .ToList();

        var compassDbSets = typeof(LeapDbContext)
            .GetProperties()
            .Where(p =>
                p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                && p.PropertyType.GetGenericArguments()[0]
                    .Namespace?.StartsWith(
                        "LeadingEDJE.Leap.Api.Modules.Compass",
                        StringComparison.Ordinal
                    ) == true
            )
            .Select(p => p.Name)
            .ToList();

        // Assert
        compassMappedTables.Count.ShouldBe(
            9,
            "the ERD's seven entities, plus the technical-skills feature's two (Skill, "
                + "EmployeeSkill), must be mapped to the compass schema"
        );
        compassDbSets.ShouldBeEmpty(
            "Compass entities are reached with context.Set<T>(); a DbSet here would make the "
                + "Platform layer depend on the Compass module"
        );
    }

    [Fact]
    public async Task CompassSchema_ExistsAtRuntime_ThroughTheSameContext()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act — asked through the SAME context the application uses, not via a separate psql session.
        var schemaCount = await context
            .Database.SqlQueryRaw<int>(
                @"SELECT count(*)::int AS ""Value"" FROM information_schema.schemata
                  WHERE schema_name = 'compass';"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        schemaCount.ShouldHaveSingleItem();
        schemaCount[0]
            .ShouldBe(
                1,
                "the migration must have created the compass schema on the application's own connection"
            );
    }
}
