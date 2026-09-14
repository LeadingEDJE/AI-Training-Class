using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// Spec 002 T051 and T052 — Constitution Principle VIII, asserted against a REAL bulk write instead
/// of a hand-built entry.
/// </summary>
/// <remarks>
/// <para>
/// Both tasks were blocked until feature 010 merged, and the reason is worth keeping. The
/// migration principal and its validation bypass had no production consumer, so every assertion that
/// could be written about them was either a statement about a constant or a sweep over an empty set —
/// and a sweep over an empty set passes. What exists now is a real bulk surface
/// (<c>/api/compass/v1/admin/sows</c>), so the two claims Principle VIII actually makes are finally
/// falsifiable:
/// </para>
/// <list type="number">
/// <item>T051 — attribution. A bulk write is attributed to a named system principal and is
/// distinguishable from operator activity from the audit row alone.</item>
/// <item>T052 — containment. The <c>LegacyMigrated</c> validation bypass is reachable only by
/// the migration principal, and never through the application surface.</item>
/// </list>
/// <para>
/// Containment is about IDENTITY, not authority, which is what makes it easy to get wrong. The
/// migration principal carries the Compass root privilege, so it is indistinguishable from a Compass
/// Super Admin by role. A role-based gate would therefore pass for both, handing every Super Admin at
/// a browser a permanent way around two partial database constraints. The pair of tests below is
/// deliberately shaped to catch exactly that mistake: the same route, the same authority, one
/// permitted and one refused — and a third test proving the refusal is not merely authorization
/// failing, because the refused caller succeeds on the very same route with a different SOW type.
/// </para>
/// <para>
/// What this adds over what already exists.
/// <c>CompassSowLegacyMigratedRestrictionTests</c> proves the service decides correctly given a
/// principal; <c>SowGrandfatheringTests</c> proves the database exemption is real and that the
/// application route refuses the type — and its own remarks record bypass containment as a
/// deliberate gap withdrawn to this stream. Only a request carrying the real migration token over the
/// real admin route shows the authentication handler, the endpoint's identity check, the service gate
/// and the audit attribution agreeing. A break anywhere between them leaves every other suite green.
/// </para>
/// <para>
/// No <c>ResetDatabaseAsync</c>, deliberately. This fixture is <see cref="MigrationTokenFactory"/>
/// rather than the shared factory, matching its sibling <c>CompassProvenanceThroughTheApiTests</c>;
/// every assertion here is scoped to an entity this test created, so a shared database cannot make one
/// pass or fail by accident.
/// </para>
/// </remarks>
public class MigrationPrincipalContainmentTests(MigrationTokenFactory factory)
    : IClassFixture<MigrationTokenFactory>
{
    /// <summary>The bulk surface. Authorised by the Compass root policy, which BOTH principals hold.</summary>
    private const string MigrationSowsRoute = "/api/compass/v1/admin/sows";

    private const string SowAuditEntityType = "CompassSow";

    /// <summary>
    /// Matches the host's own serialisation. <c>api/Program.cs</c> registers a
    /// <see cref="JsonStringEnumConverter"/>, so <c>sowType</c> travels as <c>"LegacyMigrated"</c>
    /// rather than as a number and a default-configured reader cannot deserialise the response.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HttpClient AsMigrationPrincipal()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            MigrationTokenFactory.Token
        );
        return client;
    }

    private async Task<int> SeedAssignmentAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employeeType = await db.Set<EmployeeType>().FirstOrDefaultAsync(Token);
        if (employeeType is null)
        {
            employeeType = new EmployeeType { TypeName = "Full Time", IsActive = true };
            db.Set<EmployeeType>().Add(employeeType);
            await db.SaveChangesAsync(Token);
        }

        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"ada-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = employeeType.Id,
            IsActive = true,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        };
        var client = new Client { ClientName = $"Acme-{Guid.NewGuid():N}", IsInternal = false };
        db.Set<Employee>().Add(employee);
        db.Set<Client>().Add(client);
        await db.SaveChangesAsync(Token);

        var assignment = new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = new DateOnly(2024, 1, 1),
        };
        db.Set<ClientAssignment>().Add(assignment);
        await db.SaveChangesAsync(Token);

        return assignment.Id;
    }

    private static object SowRequest(
        int assignmentId,
        SowType type,
        DateOnly? start = null,
        DateOnly? end = null
    ) =>
        new
        {
            clientAssignmentId = assignmentId,
            sowType = type.ToString(),
            rateIncrease = false,
            sowStartDate = (start ?? new DateOnly(2024, 4, 1)).ToString("yyyy-MM-dd"),
            sowEndDate = (end ?? new DateOnly(2024, 12, 31)).ToString("yyyy-MM-dd"),
            note = (string?)null,
        };

    private async Task<AuditLog> AuditRowForSowAsync(int sowId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        return await db
            .AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == SowAuditEntityType && a.EntityId == sowId.ToString())
            .SingleAsync(Token);
    }

    private async Task<int> CountSowsOnAsync(int assignmentId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        return await db.Set<Sow>().CountAsync(s => s.ClientAssignmentId == assignmentId, Token);
    }

    private static async Task<int> CreatedSowIdAsync(HttpResponseMessage response)
    {
        var created = await response.Content.ReadFromJsonAsync<CompassSowDto>(JsonOptions, Token);
        created.ShouldNotBeNull();
        return created.Id;
    }

    // ------------------------------------------------- T052: the bypass is contained to one IDENTITY

    [Fact]
    public async Task ACompassSuperAdmin_IsRefusedALegacyMigratedPeriod_OnTheMigrationRouteItself()
    {
        // Arrange — the Super Admin is fully authorised for this route; the group requires the Compass
        // root policy and that is exactly what they hold. Anything that refuses them here refuses them
        // on identity, which is the whole claim.
        var assignmentId = await SeedAssignmentAsync();

        // Act
        var response = await factory
            .AsCompassSuperAdmin()
            .PostAsJsonAsync(
                MigrationSowsRoute,
                SowRequest(assignmentId, SowType.LegacyMigrated),
                Token
            );

        // Assert — 403, and the exact status matters twice over. "Not 201" would also pass for a 401
        // or a 404, which would mean the route was unreachable rather than the bypass contained, and
        // the gate would be gone. And 403 rather than 400 IS the identity claim in the response: the
        // body is not malformed, so a 400 would say the request was wrong when what was wrong was the
        // caller. `CompassWriteStatus.Refused` exists for exactly this distinction.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CountSowsOnAsync(assignmentId)).ShouldBe(
            0,
            "a refused bypass must leave nothing behind"
        );
    }

    [Fact]
    public async Task TheMigrationPrincipal_IsPermittedALegacyMigratedPeriod_OnTheSameRoute()
    {
        // Arrange — the other half. Without it the test above is satisfied by a route that refuses
        // LegacyMigrated for EVERYONE, which would be containment by breakage rather than by design.
        var assignmentId = await SeedAssignmentAsync();

        // Act — a backwards range, admitted exactly as TPS recorded it. This is the bypass in use:
        // no application caller may write these dates under any type.
        var response = await AsMigrationPrincipal()
            .PostAsJsonAsync(
                MigrationSowsRoute,
                SowRequest(
                    assignmentId,
                    SowType.LegacyMigrated,
                    new DateOnly(2024, 12, 31),
                    new DateOnly(2024, 4, 1)
                ),
                Token
            );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var sowId = await CreatedSowIdAsync(response);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var sow = await db.Set<Sow>().AsNoTracking().SingleAsync(s => s.Id == sowId, Token);

        sow.HasPassedApplicationValidation.ShouldBeFalse(
            "a migrated row was admitted WITHOUT satisfying the rules, and must say so until its "
                + "first edit through the application satisfies them"
        );
    }

    [Fact]
    public async Task TheRefusedSuperAdmin_SucceedsOnTheSameRoute_WithAnyOtherType()
    {
        // Arrange — this is what makes the refusal above mean "identity" rather than "authorization".
        // The same caller, the same route, the same body but for one enum value.
        var assignmentId = await SeedAssignmentAsync();
        var superAdmin = factory.AsCompassSuperAdmin();

        // Act
        var permitted = await superAdmin.PostAsJsonAsync(
            MigrationSowsRoute,
            SowRequest(assignmentId, SowType.InitialContract),
            Token
        );

        // Assert
        permitted.StatusCode.ShouldBe(HttpStatusCode.Created);

        var sowId = await CreatedSowIdAsync(permitted);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var sow = await db.Set<Sow>().AsNoTracking().SingleAsync(s => s.Id == sowId, Token);

        sow.HasPassedApplicationValidation.ShouldBeTrue(
            "everything an application caller writes passed both rules on the way in"
        );
    }

    // --------------------------------------- T051: a real bulk write, attributed to a named principal

    [Fact]
    public async Task ABulkLoad_AttributesEveryRowToTheNamedSystemPrincipal()
    {
        // Arrange — several records in one run, because "bulk" is the case Principle VIII names and a
        // single write cannot show that attribution holds for every row rather than the first.
        var assignmentId = await SeedAssignmentAsync();
        var client = AsMigrationPrincipal();
        var sowIds = new List<int>();

        // Act — three consecutive periods, no overlap, the shape a real load produces.
        for (var year = 2021; year <= 2023; year++)
        {
            var response = await client.PostAsJsonAsync(
                MigrationSowsRoute,
                SowRequest(
                    assignmentId,
                    SowType.LegacyMigrated,
                    new DateOnly(year, 1, 1),
                    new DateOnly(year, 12, 31)
                ),
                Token
            );

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            sowIds.Add(await CreatedSowIdAsync(response));
        }

        // Assert — from the row alone, with no knowledge of which request produced it.
        foreach (var sowId in sowIds)
        {
            var entry = await AuditRowForSowAsync(sowId);

            entry.Actor.ShouldBe(
                MigrationPrincipal.EdjeId.ToString(),
                "the actor is the stable migration identifier, not a person and not blank"
            );
            entry.TriggeredBy.ShouldBe(MigrationPrincipal.AuditTriggeredBy);
            entry.EffectiveRoles.ShouldNotBeNull(
                "a migration write records the authority it held, like every other Compass write"
            );
        }
    }

    [Fact]
    public async Task AMigrationWriteAndAnOperatorWrite_AreDistinguishableFromTheAuditRowsAlone()
    {
        // Arrange — Principle VIII's actual requirement is not "attributed" but "distinguishable from
        // operator activity". Two writes of the SAME entity type, through the SAME route, differing
        // only in who made them: if the trail cannot separate those two, it cannot separate any.
        var assignmentId = await SeedAssignmentAsync();

        var migrationResponse = await AsMigrationPrincipal()
            .PostAsJsonAsync(
                MigrationSowsRoute,
                SowRequest(
                    assignmentId,
                    SowType.LegacyMigrated,
                    new DateOnly(2022, 1, 1),
                    new DateOnly(2022, 12, 31)
                ),
                Token
            );
        migrationResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var operatorResponse = await factory
            .AsCompassSuperAdmin()
            .PostAsJsonAsync(
                MigrationSowsRoute,
                SowRequest(
                    assignmentId,
                    SowType.InitialContract,
                    new DateOnly(2023, 1, 1),
                    new DateOnly(2023, 12, 31)
                ),
                Token
            );
        operatorResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act
        var migrationEntry = await AuditRowForSowAsync(await CreatedSowIdAsync(migrationResponse));
        var operatorEntry = await AuditRowForSowAsync(await CreatedSowIdAsync(operatorResponse));

        // Assert — both fields, because either one alone could be made to match by a later change that
        // reused the operator trigger for the migration, or stamped both with the same actor.
        migrationEntry.Actor.ShouldNotBe(operatorEntry.Actor);
        migrationEntry.TriggeredBy.ShouldNotBe(operatorEntry.TriggeredBy);

        operatorEntry.TriggeredBy.ShouldBe(
            "Compass Admin",
            "the operator value is the pre-existing wire value; changing it would silently reclassify "
                + "every historical Compass row for a reader grouping by TriggeredBy"
        );
        Guid.TryParse(operatorEntry.Actor, out var operatorActor).ShouldBeTrue();
        operatorActor.ShouldNotBe(
            MigrationPrincipal.EdjeId,
            "a human's identifier must never be the migration sentinel"
        );
    }
}
