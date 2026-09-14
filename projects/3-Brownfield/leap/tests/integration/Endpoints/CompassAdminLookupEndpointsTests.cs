using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// Lookup administration against real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// These assert what the in-memory provider cannot. The endpoint tests in the unit project cover the
/// same routes, but in-memory ignores the real unique indexes, does not translate <c>lower()</c> to SQL,
/// and enforces no foreign keys — so the three properties that matter most here are only observable
/// against Postgres:
/// </para>
/// <list type="number">
/// <item>the case-insensitive comparison actually runs as SQL, against the real collation;</item>
/// <item>retiring a lookup in use leaves referencing rows untouched, with real FK constraints armed
/// (FR-007);</item>
/// <item>the <c>compass</c> schema's own tables are what get written.</item>
/// </list>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassAdminLookupEndpointsTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassAdminLookupEndpointsTests(IntegrationTestFactory factory)
        : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string EmployeeTypes = "/api/compass/v1/admin/employee-types";
    private const string InvoiceFrequencyTypes = "/api/compass/v1/admin/invoice-frequency-types";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Post_ThenGet_RoundTripsThroughTheCompassSchema()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var name = UniqueName("Contract");

        // Act
        var created = await client.PostAsJsonAsync(
            EmployeeTypes,
            new CreateCompassLookupRequest(name),
            Token
        );

        // Assert — the row is readable back through the API and present in compass.employee_type.
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var listed = await (await client.GetAsync(EmployeeTypes, Token)).Content.ReadFromJsonAsync<
            List<EmployeeTypeDto>
        >(Token);
        listed!.Select(type => type.TypeName).ShouldContain(name);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<EmployeeType>().AnyAsync(type => type.TypeName == name, Token)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("contract")]
    [InlineData("CONTRACT")]
    [InlineData(" Contract ")]
    public async Task Post_WithANameDifferingOnlyByCaseOrWhitespace_Returns409FromRealSql(
        string variant
    )
    {
        // Arrange — the comparison runs as SQL lower() here, not as a C# string comparison.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var created = await client.PostAsJsonAsync(
            EmployeeTypes,
            new CreateCompassLookupRequest("Contract"),
            Token
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act
        var response = await client.PostAsJsonAsync(
            EmployeeTypes,
            new CreateCompassLookupRequest(variant),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Put_RetiringALookupInUse_LeavesReferencingRecordsUntouched()
    {
        // Arrange — FR-007, and the reason lookups are retired rather than deleted. An EDJEr classified
        // by the type keeps that classification; nothing cascades and nothing is rewritten. With real FK
        // constraints armed, a cascade or a null-out would fail loudly here.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var typeName = UniqueName("InUse");
        var created = (
            await (
                await client.PostAsJsonAsync(
                    EmployeeTypes,
                    new CreateCompassLookupRequest(typeName),
                    Token
                )
            ).Content.ReadFromJsonAsync<EmployeeTypeDto>(Token)
        )!;

        var employeeId = await SeedEmployeeClassifiedAsAsync(created.Id);

        // Act — retire the type that employee is classified by.
        var retired = await client.PutAsJsonAsync(
            $"{EmployeeTypes}/{created.Id}",
            new UpdateCompassLookupRequest(typeName, IsActive: false),
            Token
        );

        // Assert
        retired.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var employee = await db.Set<Employee>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == employeeId, Token);

        employee.EmployeeTypeId.ShouldBe(
            created.Id,
            "the referencing record must still point at the retired type"
        );
        employee.IsActive.ShouldBeTrue("retiring a lookup must not deactivate anything that uses it");

        var type = await db.Set<EmployeeType>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == created.Id, Token);
        type.IsActive.ShouldBeFalse();
        type.TypeName.ShouldBe(typeName, "retiring must not rename");
    }

    [Fact]
    public async Task GetWithActiveOnly_OmitsARetiredTypeStillReferencedByAnEdjer()
    {
        // Arrange — the other half of FR-007: the value disappears from SELECTION while the existing
        // classification survives. This is what quickstart 3.1 steps 4-6 demonstrate.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var typeName = UniqueName("Retiring");
        var created = (
            await (
                await client.PostAsJsonAsync(
                    EmployeeTypes,
                    new CreateCompassLookupRequest(typeName),
                    Token
                )
            ).Content.ReadFromJsonAsync<EmployeeTypeDto>(Token)
        )!;
        await SeedEmployeeClassifiedAsAsync(created.Id);
        await client.PutAsJsonAsync(
            $"{EmployeeTypes}/{created.Id}",
            new UpdateCompassLookupRequest(typeName, IsActive: false),
            Token
        );

        // Act
        var active = await (
            await client.GetAsync($"{EmployeeTypes}?activeOnly=true", Token)
        ).Content.ReadFromJsonAsync<List<EmployeeTypeDto>>(Token);
        var all = await (await client.GetAsync(EmployeeTypes, Token)).Content.ReadFromJsonAsync<
            List<EmployeeTypeDto>
        >(Token);

        // Assert
        active!.Select(type => type.TypeName).ShouldNotContain(typeName);
        all!.Select(type => type.TypeName)
            .ShouldContain(typeName, "the admin screen must still show it, or it could never be reinstated");
    }

    [Fact]
    public async Task Put_ReinstatingARetiredType_MakesItSelectableAgain()
    {
        // Arrange — retiring is reversible. Nothing in the requirements makes it one-way, and a
        // one-way toggle would be a trap on a screen whose only controls are rename and retire.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var typeName = UniqueName("Reinstate");
        var created = (
            await (
                await client.PostAsJsonAsync(
                    EmployeeTypes,
                    new CreateCompassLookupRequest(typeName),
                    Token
                )
            ).Content.ReadFromJsonAsync<EmployeeTypeDto>(Token)
        )!;
        await client.PutAsJsonAsync(
            $"{EmployeeTypes}/{created.Id}",
            new UpdateCompassLookupRequest(typeName, IsActive: false),
            Token
        );

        // Act
        var response = await client.PutAsJsonAsync(
            $"{EmployeeTypes}/{created.Id}",
            new UpdateCompassLookupRequest(typeName, IsActive: true),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var active = await (
            await client.GetAsync($"{EmployeeTypes}?activeOnly=true", Token)
        ).Content.ReadFromJsonAsync<List<EmployeeTypeDto>>(Token);
        active!.Select(type => type.TypeName).ShouldContain(typeName);
    }

    [Fact]
    public async Task InvoiceFrequencyTypes_RoundTripIndependentlyOfEmployeeTypes()
    {
        // Arrange — one service serves both lookups, so this is where sharing a table would show up
        // against a real database with both unique indexes armed.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var shared = UniqueName("Shared");
        var asEmployeeType = await client.PostAsJsonAsync(
            EmployeeTypes,
            new CreateCompassLookupRequest(shared),
            Token
        );
        asEmployeeType.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act
        var asFrequency = await client.PostAsJsonAsync(
            InvoiceFrequencyTypes,
            new CreateCompassLookupRequest(shared),
            Token
        );

        // Assert
        asFrequency.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<EmployeeType>().CountAsync(type => type.TypeName == shared, Token)).ShouldBe(1);
        (
            await db.Set<InvoiceFrequencyType>().CountAsync(type => type.TypeName == shared, Token)
        ).ShouldBe(1);
    }

    [Fact]
    public async Task EveryWriteRoute_RefusesACompassAdminPrincipal()
    {
        // Arrange — SC-006 against the real pipeline and a real database, with the interface bypassed.
        await ResetDatabaseAsync();
        var readOnly = _factory.AsCompassAdmin();

        // Act & Assert
        foreach (var route in new[] { EmployeeTypes, InvoiceFrequencyTypes })
        {
            (
                await readOnly.PostAsJsonAsync(
                    route,
                    new CreateCompassLookupRequest("Smuggled"),
                    Token
                )
            ).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            (
                await readOnly.PutAsJsonAsync(
                    $"{route}/1",
                    new UpdateCompassLookupRequest("Smuggled", IsActive: false),
                    Token
                )
            ).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // Nothing was written by any of the refused calls.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (
            await db.Set<EmployeeType>().AnyAsync(type => type.TypeName == "Smuggled", Token)
        ).ShouldBeFalse();
        (
            await db.Set<InvoiceFrequencyType>().AnyAsync(type => type.TypeName == "Smuggled", Token)
        ).ShouldBeFalse();
    }

    // ---------------------------------------------------------- the check-then-act race (PR #213)

    [Fact]
    public async Task ConcurrentCreatesOfTheSameName_YieldOneCreatedAndOneConflict_NeverA500()
    {
        // Arrange — the end-to-end shape of the review finding, over HTTP. Whichever way the two
        // requests interleave, the outcome must be the same pair: one 201 and one 409. A 500 here is
        // the defect.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var name = UniqueName("Concurrent");

        // Act
        var first = client.PostAsJsonAsync(EmployeeTypes, new CreateCompassLookupRequest(name), Token);
        var second = client.PostAsJsonAsync(EmployeeTypes, new CreateCompassLookupRequest(name), Token);
        var responses = await Task.WhenAll(first, second);

        // Assert
        responses
            .Select(response => response.StatusCode)
            .ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict], ignoreOrder: true);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<EmployeeType>().CountAsync(type => type.TypeName == name, Token)).ShouldBe(
            1,
            "exactly one of the two writers may win"
        );
    }

    /// <summary>
    /// Inserts a minimal EDJEr classified by the given employee type, so a retirement has something
    /// real referencing it.
    /// </summary>
    private async Task<int> SeedEmployeeClassifiedAsAsync(int employeeTypeId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            HireDate = new DateOnly(2020, 1, 6),
            Email = $"ada.{Guid.NewGuid():N}@example.test",
            EmployeeTypeId = employeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        };

        db.Set<Employee>().Add(employee);
        await db.SaveChangesAsync(Token);

        return employee.Id;
    }
}
