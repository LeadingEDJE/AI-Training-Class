using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// The migration's contract-period routes, through HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Companion to <see cref="CompassAdminSowAuthorizationTests"/>, which proves who is refused. This
/// file covers what the handlers DO once a caller is allowed: the two verification reads SC-002
/// requires, the created-location response, and the provenance refusal branch.
/// </para>
/// <para>
/// At the unit level as well as through Testcontainers because the backend coverage gate measures
/// <c>tests/unit</c> alone — these handlers were exercised only by the integration suite, so their
/// lines counted as uncovered against the 98% floor for the whole assembly.
/// </para>
/// </remarks>
public class CompassAdminSowEndpointsTests
{
    private const string Route = "/api/compass/v1/admin/sows";
    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Web defaults plus the string-enum converter the application itself registers.</summary>
    /// <remarks>
    /// <c>api/Program.cs</c> registers a <see cref="JsonStringEnumConverter"/>, so <c>sowType</c>
    /// travels as <c>"InitialContract"</c> rather than <c>0</c>. Reading with default options throws
    /// a <see cref="JsonException"/> naming the property but not the cause — the response is correct
    /// and the TEST is misconfigured. The integration suite documents the same four lines; this file
    /// rediscovered it anyway.
    /// </remarks>
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static HttpClient AsMigrationPrincipal(MigrationTokenUnitFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                MigrationTokenUnitFactory.MigrationToken
            );
        return client;
    }

    /// <summary>Seeds an assignment for a period to hang off, and returns its id.</summary>
    private static async Task<int> SeedAssignmentAsync(MigrationTokenUnitFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (!await context.Set<EmployeeType>().AnyAsync(e => e.Id == EmployeeTypeId, Token))
        {
            context
                .Set<EmployeeType>()
                .Add(new EmployeeType
                {
                    Id = EmployeeTypeId,
                    TypeName = "Full Time",
                    IsActive = true,
                });
        }

        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"ada-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
        };
        var client = new Client { ClientName = $"Acme-{Guid.NewGuid():N}", IsInternal = false };
        context.Set<Employee>().Add(employee);
        context.Set<Client>().Add(client);
        await context.SaveChangesAsync(Token);

        var assignment = new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = new DateOnly(2026, 1, 1),
        };
        context.Set<ClientAssignment>().Add(assignment);
        await context.SaveChangesAsync(Token);

        return assignment.Id;
    }

    private static CompassSowRequest Request(int assignmentId, string? legacyTpsId = null) =>
        new(
            ClientAssignmentId: assignmentId,
            SowType: SowType.InitialContract,
            RateIncrease: false,
            SowStartDate: new DateOnly(2026, 1, 1),
            SowEndDate: new DateOnly(2026, 6, 30),
            Note: null,
            LegacyTpsId: legacyTpsId
        );

    [Fact]
    public async Task Post_AsTheMigrationPrincipal_CreatesAndReturnsItsLocation()
    {
        // Arrange
        await using var factory = new MigrationTokenUnitFactory();
        var assignmentId = await SeedAssignmentAsync(factory);
        var client = AsMigrationPrincipal(factory);

        // Act
        var response = await client.PostAsJsonAsync(Route, Request(assignmentId), Token);

        // Assert — the Location header is asserted, not just the status: the migration reads the id
        // back out of the created resource, so a route that answered 201 without pointing at it
        // would satisfy a status-only test and still break the crosswalk.
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CompassSowDto>(JsonOptions, Token);
        created.ShouldNotBeNull();
        response.Headers.Location!.ToString().ShouldEndWith($"{Route}/{created.Id}");
    }

    [Fact]
    public async Task Get_AsTheMigrationPrincipal_ListsWhatItCreated()
    {
        // Arrange — the read SC-002 depends on: a create-only surface makes "every migrated
        // relationship verified at 100%" impossible in principle.
        await using var factory = new MigrationTokenUnitFactory();
        var assignmentId = await SeedAssignmentAsync(factory);
        var client = AsMigrationPrincipal(factory);
        (await client.PostAsJsonAsync(Route, Request(assignmentId), Token)).StatusCode.ShouldBe(
            HttpStatusCode.Created
        );

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rows = await response.Content.ReadFromJsonAsync<List<CompassSowDto>>(JsonOptions, Token);
        rows.ShouldNotBeNull();
        rows.ShouldContain(row => row.ClientAssignmentId == assignmentId);
    }

    [Fact]
    public async Task GetById_ReturnsTheCreatedPeriod()
    {
        // Arrange
        await using var factory = new MigrationTokenUnitFactory();
        var assignmentId = await SeedAssignmentAsync(factory);
        var client = AsMigrationPrincipal(factory);
        var created = await (
            await client.PostAsJsonAsync(Route, Request(assignmentId), Token)
        ).Content.ReadFromJsonAsync<CompassSowDto>(JsonOptions, Token);
        created.ShouldNotBeNull();

        // Act
        var response = await client.GetAsync($"{Route}/{created.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var found = await response.Content.ReadFromJsonAsync<CompassSowDto>(JsonOptions, Token);
        found.ShouldNotBeNull();
        found.Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task GetById_WhenThePeriodDoesNotExist_Is404()
    {
        // Arrange
        await using var factory = new MigrationTokenUnitFactory();
        var client = AsMigrationPrincipal(factory);

        // Act
        var response = await client.GetAsync($"{Route}/999999", Token);

        // Assert — 404 rather than an empty 200, so a spot-check that names a missing id gets an
        // answer it can act on instead of an empty body it has to interpret.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_AsACompassSuperAdmin_SupplyingProvenance_IsRefused()
    {
        // Arrange — ⚠️ identity, not role. A Super Admin holds the same Compass root the migration
        // principal does, so a role-keyed gate would let them stamp a fabricated TPS origin onto a
        // record they just typed.
        await using var factory = new MigrationTokenUnitFactory();
        var assignmentId = await SeedAssignmentAsync(factory);
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(assignmentId, legacyTpsId: "tps-sow-fabricated"),
            Token
        );

        // Assert — refused rather than silently dropped, and the exact status: a loose "not 201"
        // would also pass on a 401 from a misconfigured host.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
