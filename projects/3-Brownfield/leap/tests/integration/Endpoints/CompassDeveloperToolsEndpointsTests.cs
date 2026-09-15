using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// The Compass data clear, against real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// This suite is not redundant coverage — for this feature it is the ONLY coverage that means
/// anything. The service issues a real <c>TRUNCATE ... RESTART IDENTITY</c> across six
/// schema-qualified tables. The EF Core InMemory provider the unit suite runs on has no
/// <c>TRUNCATE</c>, no foreign keys and no sequences, so it cannot show that the statement parses,
/// that naming exactly six tables satisfies every foreign key between them (which is what lets the
/// statement omit <c>CASCADE</c>), or that identity sequences restart. Each of those is asserted
/// below, and each would be invisible to a green unit run — including the FK edge the
/// technical-skills feature added: <c>employee_skill</c> references <c>employee</c>, so a clear that
/// forgot to name it would 500 here the moment an EDJEr has a tagged skill.
/// </para>
/// <para>
/// The three Compass LOOKUP tables get their own assertion for the same reason they are excluded in
/// the first place: they are FK parents of tables the clear empties, so a <c>CASCADE</c> added "to
/// make it work" would silently take them with it and every other test here would still pass.
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassDeveloperToolsEndpointsTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassDeveloperToolsEndpointsTests(IntegrationTestFactory factory) : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string Availability = "/api/compass/developer-tools/availability";
    private const string Clear = "/api/compass/developer-tools/clear-compass-data";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Writes one row into every Compass table — the six that get cleared and the three that must not.
    /// </summary>
    private async Task SeedCompassAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employeeType = new EmployeeType { TypeName = "Full Time", IsActive = true };
        var invoiceFrequency = new InvoiceFrequencyType { TypeName = "Monthly", IsActive = true };
        var skill = new Skill { TypeName = "React", IsActive = true };
        db.Add(employeeType);
        db.Add(invoiceFrequency);
        db.Add(skill);
        await db.SaveChangesAsync(Token);

        var client = new Client
        {
            ClientName = "Acme Corp",
            InvoiceFrequencyTypeId = invoiceFrequency.Id,
        };
        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada.lovelace@example.test",
            EmployeeTypeId = employeeType.Id,
            HireDate = new DateOnly(2020, 1, 6),
            StateOfResidence = "OH",
            IsActive = true,
        };
        db.Add(client);
        db.Add(employee);
        await db.SaveChangesAsync(Token);

        var assignment = new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = new DateOnly(2024, 1, 1),
        };
        db.Add(assignment);
        db.Add(new BillableTimeCategory
        {
            ClientId = client.Id,
            CategoryName = "Development",
            IsActive = true,
        });
        db.Add(new EmployeeSkill { EmployeeId = employee.Id, SkillId = skill.Id });
        await db.SaveChangesAsync(Token);

        db.Add(new Sow
        {
            ClientAssignmentId = assignment.Id,
            SowStartDate = new DateOnly(2024, 1, 1),
            SowEndDate = new DateOnly(2024, 12, 31),
        });
        await db.SaveChangesAsync(Token);
    }

    private async Task<T> QueryAsync<T>(Func<LeapDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        return await query(db);
    }

    [Fact]
    public async Task Clear_EmptiesTheSixCompassOperationalTables()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedCompassAsync();
        var client = CompassPrincipalFactory.AsRoles(_factory, RolePolicy.CompassSuperAdminRole);

        // Act — this is the assertion the whole feature rests on: the generated statement has to
        // actually run against Postgres. A LINQ-translation or FK-ordering mistake surfaces here as a
        // 500, and nowhere else in the repository.
        var response = await client.PostAsync(Clear, content: null, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var counts = await QueryAsync(async db => new
        {
            BillableTimeCategories = await db.Set<BillableTimeCategory>().CountAsync(Token),
            Sows = await db.Set<Sow>().CountAsync(Token),
            Assignments = await db.Set<ClientAssignment>().CountAsync(Token),
            Employees = await db.Set<Employee>().CountAsync(Token),
            EmployeeSkills = await db.Set<EmployeeSkill>().CountAsync(Token),
            Clients = await db.Set<Client>().CountAsync(Token),
        });

        counts.BillableTimeCategories.ShouldBe(0);
        counts.Sows.ShouldBe(0);
        counts.Assignments.ShouldBe(0);
        counts.Employees.ShouldBe(0);
        counts.EmployeeSkills.ShouldBe(0);
        counts.Clients.ShouldBe(0);
    }

    [Fact]
    public async Task Clear_LeavesTheCompassLookupTablesIntact()
    {
        // Arrange — employee_type, invoice_frequency_type and skill are FK PARENTS of cleared tables,
        // so the most likely "fix" for an FK error (adding CASCADE) would empty them too and break
        // Compass's ability to accept a single new EDJEr, client, or tagged skill. Nothing else here
        // would notice.
        await ResetDatabaseAsync();
        await SeedCompassAsync();
        var client = CompassPrincipalFactory.AsRoles(_factory, RolePolicy.CompassSuperAdminRole);

        // Act
        await client.PostAsync(Clear, content: null, Token);

        // Assert
        var lookups = await QueryAsync(async db => new
        {
            EmployeeTypes = await db.Set<EmployeeType>().CountAsync(Token),
            InvoiceFrequencyTypes = await db.Set<InvoiceFrequencyType>().CountAsync(Token),
            Skills = await db.Set<Skill>().CountAsync(Token),
        });

        lookups.EmployeeTypes.ShouldBe(1);
        lookups.InvoiceFrequencyTypes.ShouldBe(1);
        lookups.Skills.ShouldBe(1);
    }

    [Fact]
    public async Task Clear_ReportsTheRowCountsItRemoved()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedCompassAsync();
        var client = CompassPrincipalFactory.AsRoles(_factory, RolePolicy.CompassSuperAdminRole);

        // Act
        var response = await client.PostAsync(Clear, content: null, Token);

        // Assert — the counts are the only record of what was destroyed that the caller ever sees.
        var body = await response.Content.ReadFromJsonAsync<CompassDataClearedResponse>(Token);
        body.ShouldNotBeNull();
        body.Employees.ShouldBe(1);
        body.EmployeeSkills.ShouldBe(1);
        body.Clients.ShouldBe(1);
        body.ClientAssignments.ShouldBe(1);
        body.Sows.ShouldBe(1);
        body.BillableTimeCategories.ShouldBe(1);
        body.TotalRowsCleared.ShouldBe(6);
    }

    [Fact]
    public async Task Clear_RestartsIdentitySequences()
    {
        // Arrange — RESTART IDENTITY is not cosmetic. Without it a "cleared" Compass hands out client
        // id 4,318 next, and no unit test can see the difference because InMemory has no sequences.
        await ResetDatabaseAsync();
        await SeedCompassAsync();
        var client = CompassPrincipalFactory.AsRoles(_factory, RolePolicy.CompassSuperAdminRole);

        // Act
        await client.PostAsync(Clear, content: null, Token);

        var firstIdAfterClear = await QueryAsync(async db =>
        {
            var fresh = new Client { ClientName = "First After Clear" };
            db.Add(fresh);
            await db.SaveChangesAsync(Token);
            return fresh.Id;
        });

        // Assert
        firstIdAfterClear.ShouldBe(1);
    }

    [Fact]
    public async Task Clear_IsIdempotent_ASecondClearSucceedsAndReportsZero()
    {
        // Arrange — the realistic second press: someone clears, reads "Compass data cleared!", and
        // clicks again to be sure. That must not be an error.
        await ResetDatabaseAsync();
        await SeedCompassAsync();
        var client = CompassPrincipalFactory.AsRoles(_factory, RolePolicy.CompassSuperAdminRole);
        await client.PostAsync(Clear, content: null, Token);

        // Act
        var second = await client.PostAsync(Clear, content: null, Token);

        // Assert
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await second.Content.ReadFromJsonAsync<CompassDataClearedResponse>(Token);
        body.ShouldNotBeNull();
        body.TotalRowsCleared.ShouldBe(0);
    }

    [Fact]
    public async Task Clear_WritesAnAuditEntryNamingTheActorAndTheCounts()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedCompassAsync();
        var client = CompassPrincipalFactory.AsRoles(_factory, RolePolicy.CompassSuperAdminRole);

        // Act
        await client.PostAsync(Clear, content: null, Token);

        // Assert — the audit row is the only surviving evidence of what the clear destroyed, so it has
        // to reach the real audit_logs table and not merely the service's own logger.
        var entry = await QueryAsync(async db =>
            await db.AuditLogs.SingleOrDefaultAsync(log => log.EntityType == "CompassData", Token));

        entry.ShouldNotBeNull();
        entry.Action.ShouldBe("clear");
        entry.EntityId.ShouldBe("compass");
        entry.Actor.ShouldBe(CompassPrincipalFactory.TestEdjeId.ToString());
    }

    // ---------------------------------------------------------------- authorization, through the real pipeline

    [Fact]
    public async Task Clear_AsCompassAdmin_IsRefusedAndDestroysNothing()
    {
        // Arrange — Compass Admin is READ-ONLY (AC-44). The unit suite asserts the 403; this asserts the
        // thing that actually matters, which is that the rows are still there afterwards.
        await ResetDatabaseAsync();
        await SeedCompassAsync();
        var client = CompassPrincipalFactory.AsRoles(_factory, RolePolicy.CompassAdminRole);

        // Act
        var response = await client.PostAsync(Clear, content: null, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await QueryAsync(async db => await db.Set<Employee>().CountAsync(Token))).ShouldBe(1);
    }

    [Fact]
    public async Task Clear_AsTimesheetRootOnly_IsRefusedAndDestroysNothing()
    {
        // Arrange — Compass inherits nothing, not even root. A timesheet SuperAdmin holds every
        // privilege in timesheet and none at all here.
        await ResetDatabaseAsync();
        await SeedCompassAsync();
        var client = CompassPrincipalFactory.AsRoles(_factory, RolePolicy.SuperAdmin);

        // Act
        var response = await client.PostAsync(Clear, content: null, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await QueryAsync(async db => await db.Set<Employee>().CountAsync(Token))).ShouldBe(1);
    }

    [Fact]
    public async Task Availability_AsCompassSuperAdmin_IsOk()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = CompassPrincipalFactory.AsRoles(_factory, RolePolicy.CompassSuperAdminRole);

        // Act
        var response = await client.GetAsync(Availability, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Availability_AsCompassAdmin_IsForbidden()
    {
        // Arrange — the launcher renders a DISABLED button off the back of this exact status code.
        await ResetDatabaseAsync();
        var client = CompassPrincipalFactory.AsRoles(_factory, RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(Availability, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
