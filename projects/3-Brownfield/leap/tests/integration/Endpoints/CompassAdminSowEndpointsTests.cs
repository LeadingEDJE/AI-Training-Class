using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using CompassClient = LeadingEDJE.Leap.Api.Modules.Compass.Client;
using CompassClientAssignment = LeadingEDJE.Leap.Api.Modules.Compass.ClientAssignment;
using CompassEmployee = LeadingEDJE.Leap.Api.Modules.Compass.Employee;
using CompassEmployeeType = LeadingEDJE.Leap.Api.Modules.Compass.EmployeeType;
using CompassSow = LeadingEDJE.Leap.Api.Modules.Compass.Sow;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// Contract-period creation against real PostgreSQL, over the real HTTP pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This file exists mostly for the two PARTIAL constraints, which no in-memory provider has.
/// The overlap rule is a GiST exclusion constraint over a <c>daterange</c> scoped to the assignment,
/// and the date-order rule is a CHECK — both carry <c>WHERE sow_type &lt;&gt; 'LegacyMigrated'</c>
/// / <c>sow_type = 'LegacyMigrated' OR ...</c>. Their partiality is the whole mechanism that lets a
/// TPS load through while keeping everything Compass creates protected, and it is only observable
/// against a real database.
/// </para>
/// <para>
/// The service-level restriction on who may write <c>LegacyMigrated</c> is proven in
/// <c>tests/unit/Services/CompassSowLegacyMigratedRestrictionTests</c>. What is proven HERE is that
/// the database actually honours the exemption the restriction is protecting — if it did not, the
/// restriction would be guarding nothing.
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassAdminSowEndpointsTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassAdminSowEndpointsTests(IntegrationTestFactory factory)
        : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string Route = "/api/compass/v1/admin/sows";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Mirrors the API's own serializer configuration.
    /// </summary>
    /// <remarks>
    /// <c>api/Program.cs</c> registers a <see cref="JsonStringEnumConverter"/>, so
    /// <c>sowType</c> travels as <c>"InitialContract"</c> rather than <c>0</c>. Reading with default
    /// options throws a <see cref="JsonException"/> that names the property but not the cause — the
    /// response is correct and the TEST is misconfigured. Worth the four lines to avoid rediscovering
    /// that.
    /// </remarks>
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Post_TranslatesToSql_AndPersistsTheContractPeriod()
    {
        // Arrange — the "it translates" case for the assignment existence check.
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(assignmentId, SowType.InitialContract),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CompassSowDto>(JsonOptions, Token);
        created.ShouldNotBeNull();
        created.HasPassedApplicationValidation.ShouldBeTrue(
            "it satisfied both rules on the way in, so the flag is honest"
        );

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<CompassSow>().CountAsync(Token)).ShouldBe(1);
    }

    [Fact]
    public async Task Post_WithAnUnknownAssignment_IsNotFound()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(999999, SowType.InitialContract),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_LegacyMigrated_AsTheCompassRoot_IsRefusedWithExactly403()
    {
        // Arrange — ⚠️ the restriction, end to end over HTTP against the real database.
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(assignmentId, SowType.LegacyMigrated),
            Token
        );

        // Assert — the exact status, and nothing written.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<CompassSow>().CountAsync(Token)).ShouldBe(0);
    }

    [Fact]
    public async Task TheOverlapConstraint_IsReal_ForANonLegacyType()
    {
        // Arrange — proves the exclusion constraint exists and bites. Two InitialContract periods on
        // the SAME assignment with overlapping ranges.
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var client = _factory.AsCompassSuperAdmin();

        var first = await client.PostAsJsonAsync(
            Route,
            Request(assignmentId, SowType.InitialContract, new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30)),
            Token
        );
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act — overlaps the first by a month.
        var second = await client.PostAsJsonAsync(
            Route,
            Request(assignmentId, SowType.InitialContract, new DateOnly(2024, 6, 1), new DateOnly(2024, 12, 31)),
            Token
        );

        // Assert — ⚠️ this asserted only "not Created" and was tacit confirmation the path was
        // UNHANDLED: the exclusion constraint raised 23P01 straight out of SaveChangesAsync and the
        // caller got a 500. Every other rule in this service returns a clean 4xx naming the problem.
        // A loose assertion here is exactly the shape this suite forbids — it
        // passes whether the service handles the overlap or crashes on it.
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<CompassSow>().CountAsync(Token)).ShouldBe(1);
    }

    [Fact]
    public async Task TheConstraintsAreExemptedForLegacyMigrated_WhichIsWhyTheRestrictionMatters()
    {
        // Arrange — writes two OVERLAPPING LegacyMigrated periods, one with a BACKWARDS range,
        // directly through the data layer. Directly, because the HTTP route refuses this type to
        // every caller except the migration principal — which is precisely the point being made:
        // the database WILL accept these, so the only thing standing between the application and a
        // permanent bypass of both constraints is the service-level restriction.
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        db.Set<CompassSow>()
            .AddRange(
                new CompassSow
                {
                    ClientAssignmentId = assignmentId,
                    SowType = SowType.LegacyMigrated,
                    SowStartDate = new DateOnly(2024, 1, 1),
                    SowEndDate = new DateOnly(2024, 12, 31),
                    HasPassedApplicationValidation = false,
                },
                new CompassSow
                {
                    ClientAssignmentId = assignmentId,
                    SowType = SowType.LegacyMigrated,
                    // Overlapping AND backwards — both rules violated at once.
                    SowStartDate = new DateOnly(2024, 12, 31),
                    SowEndDate = new DateOnly(2024, 3, 1),
                    HasPassedApplicationValidation = false,
                }
            );

        // Act
        await db.SaveChangesAsync(Token);

        // Assert — both landed. The exemption is real, and so is the need to restrict who reaches it.
        (await db.Set<CompassSow>().CountAsync(Token)).ShouldBe(2);
    }

    // ------------------------------------------------------------------------------------- helpers

    private static CompassSowRequest Request(
        int assignmentId,
        SowType type,
        DateOnly? start = null,
        DateOnly? end = null
    ) =>
        new(
            ClientAssignmentId: assignmentId,
            SowType: type,
            RateIncrease: false,
            SowStartDate: start ?? new DateOnly(2024, 1, 1),
            SowEndDate: end ?? new DateOnly(2024, 12, 31),
            Note: null
        );

    private async Task<int> SeedAssignmentAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employeeType = new CompassEmployeeType
        {
            TypeName = $"Type-{Guid.NewGuid():N}"[..20],
            IsActive = true,
        };
        db.Set<CompassEmployeeType>().Add(employeeType);
        await db.SaveChangesAsync(Token);

        var employee = new CompassEmployee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            HireDate = new DateOnly(2020, 1, 6),
            Email = $"edjer.{Guid.NewGuid():N}@example.test",
            EmployeeTypeId = employeeType.Id,
            StateOfResidence = "OH",
            IsActive = true,
        };
        db.Set<CompassEmployee>().Add(employee);

        var compassClient = new CompassClient
        {
            ClientName = $"Client-{Guid.NewGuid():N}"[..24],
            IsInternal = false,
        };
        db.Set<CompassClient>().Add(compassClient);
        await db.SaveChangesAsync(Token);

        var assignment = new CompassClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = compassClient.Id,
            StartDate = new DateOnly(2024, 1, 1),
        };
        db.Set<CompassClientAssignment>().Add(assignment);
        await db.SaveChangesAsync(Token);

        return assignment.Id;
    }

    [Fact]
    public async Task GetAll_TranslatesToSql_AndRoundTripsTheEnum()
    {
        // Arrange — two things only a real database and a real pipeline can prove: that the ordered
        // anonymous projection translates, and that SowType survives the round trip as a STRING.
        // Sending or reading it as a number is the trap that cost a diagnostic detour earlier in
        // this feature.
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var client = _factory.AsCompassSuperAdmin();

        await client.PostAsJsonAsync(Route, Request(assignmentId, SowType.InitialContract), Token);

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var sows = await response.Content.ReadFromJsonAsync<List<CompassSowDto>>(JsonOptions, Token);
        sows!.Count.ShouldBe(1);
        sows[0].SowType.ShouldBe(SowType.InitialContract);
        sows[0].HasPassedApplicationValidation.ShouldBeTrue();
    }

    [Fact]
    public async Task GetAll_SurfacesALegacyMigratedPeriod_SoTheMigrationCanVerifyItsOwnWrites()
    {
        // Arrange — the whole reason these reads were added. A migrated period must be readable, or
        // SC-002's 100% relationship verification is impossible in principle for contract periods.
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();

        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

            db.Set<CompassSow>().Add(
                new CompassSow
                {
                    ClientAssignmentId = assignmentId,
                    SowType = SowType.LegacyMigrated,
                    SowStartDate = new DateOnly(2024, 1, 1),
                    SowEndDate = new DateOnly(2024, 12, 31),
                    HasPassedApplicationValidation = false,
                }
            );
            await db.SaveChangesAsync(Token);
        }

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route, Token);

        // Assert
        var sows = await response.Content.ReadFromJsonAsync<List<CompassSowDto>>(JsonOptions, Token);
        sows.ShouldNotBeNull();
        sows.Single().SowType.ShouldBe(SowType.LegacyMigrated);
        sows.Single().HasPassedApplicationValidation.ShouldBeFalse(
            "a migrated row has NOT satisfied the rules it was exempted from"
        );
    }
}
