using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

// Compass's own entities. Aliased rather than imported bare, matching CompassAdminEdjerEndpointsTests:
// this project has a `.Compass` namespace, so an unqualified import reads as though it referred to that.
using CompassClient = LeadingEDJE.Leap.Api.Modules.Compass.Client;
using CompassClientAssignment = LeadingEDJE.Leap.Api.Modules.Compass.ClientAssignment;
using CompassEmployee = LeadingEDJE.Leap.Api.Modules.Compass.Employee;
using CompassEmployeeType = LeadingEDJE.Leap.Api.Modules.Compass.EmployeeType;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// US7/#65 — the AC-19 deactivation precondition, against real PostgreSQL over the real HTTP pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Two things here are covered nowhere else, and they are the reason this file exists.
/// <c>CompassAdminEdjerEndpointsTests</c> already proves the write path refuses (FR-040/FR-041) and
/// permits once every assignment is ended (FR-044). What it does not cover: a future end date
/// must not block (FR-045), and a Super Admin must be able to end the blockers and retry with no Ops
/// handoff (FR-043, AC-45, J14d). Both are asserted below.
/// </para>
/// <para>
/// FR-045 can only be tested here. The condition is the ABSENCE of an end date, enforced by the
/// repository's <c>end_date IS NULL</c> predicate — so a test against an in-memory double proves only
/// that the double filters the way it was written. The stricter "not yet ended" reading would deadlock a
/// confirmed rollout, where the departure is agreed and dated but has not arrived, which is exactly the
/// case a real NULL-vs-future-date distinction has to get right.
/// </para>
/// <para>
/// The blockers route needs one real-SQL case whose only job is "it translates."
/// <c>GetOpenAssignmentsAsync</c> shipped a
/// projected-member ordering that Npgsql cannot render and that the in-memory provider evaluates
/// happily — every unit test passed while the route answered 500. That query is this guard's input.
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassDeactivationGuardTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassDeactivationGuardTests(IntegrationTestFactory factory)
        : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string EdjerRoute = "/api/compass/v1/admin/edjers";
    private const string AssignmentRoute = "/api/compass/assignments";
    private const string BlockersRoute = "/api/compass/assignments/blockers";

    private static readonly DateOnly AssignmentStart = new(2024, 4, 1);

    /// <summary>
    /// Mirrors the application's own HTTP JSON configuration.
    /// </summary>
    /// <remarks>
    /// <c>api/Program.cs</c> registers a <see cref="JsonStringEnumConverter"/>, so the verdict's status
    /// travels as <c>"Blocked"</c> rather than <c>3</c>. Reading it back with default options throws
    /// rather than mis-parsing, which is the good failure — but it is a failure about the test's options,
    /// not the route, so the converter belongs here explicitly.
    /// </remarks>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------ FR-040/FR-041: the refusal

    [Fact]
    public async Task GetBlockers_ForAnEdjerHoldingAnOpenAssignment_NamesTheAssignmentAndItsClient()
    {
        // Arrange — the route the EDJEr edit form calls BEFORE offering the toggle, so the operator sees
        // what to end rather than discovering it as a 422 after committing to the action.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        var edjer = await CreateEdjerAsync(client, Request(employeeTypeId));
        var clientId = await SeedClientAsync("Buckeye Mutual");
        var assignmentId = await SeedAssignmentAsync(edjer.Id, clientId, endDate: null);

        // Act
        var response = await client.GetAsync($"{BlockersRoute}/{edjer.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var verdict = await response.Content.ReadFromJsonAsync<EdjerDeactivationVerdict>(JsonOptions, Token);
        verdict.ShouldNotBeNull();
        verdict.Status.ShouldBe(EdjerDeactivationStatus.Blocked);
        verdict.BlockingAssignments.Count.ShouldBe(1);
        verdict.BlockingAssignments[0].AssignmentId.ShouldBe(assignmentId);
        verdict.BlockingAssignments[0].ClientId.ShouldBe(clientId);
        verdict.BlockingAssignments[0].ClientName.ShouldBe(
            "Buckeye Mutual",
            "FR-041: an id alone is not actionable by a human, so the name comes from a real join"
        );
        verdict.BlockingAssignments[0].StartDate.ShouldBe(AssignmentStart);
    }

    // ------------------------------------------------------- FR-042: nothing is ended by the refusal

    [Fact]
    public async Task ARefusedDeactivation_EndsNoAssignment_AndLeavesTheEdjerActive()
    {
        // Arrange — the case AC-19 calls out explicitly. An engagement's true end date frequently
        // differs from the date somebody happened to attempt a deactivation, so defaulting it would
        // record a date that is simply wrong and would do so silently.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        var edjer = await CreateEdjerAsync(client, Request(employeeTypeId));
        var clientId = await SeedClientAsync("Buckeye Mutual");
        await SeedAssignmentAsync(edjer.Id, clientId, endDate: null);
        await SeedAssignmentAsync(edjer.Id, await SeedClientAsync("Granville Retail"), endDate: null);

        // Act
        var response = await client.PutAsJsonAsync(
            $"{EdjerRoute}/{edjer.Id}",
            Request(employeeTypeId, edjer.Email, isActive: false),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var stored = await db.Set<CompassEmployee>()
            .AsNoTracking()
            .SingleAsync(employee => employee.Id == edjer.Id, Token);
        stored.IsActive.ShouldBeTrue("the EDJEr must remain active");

        var assignments = await db.Set<CompassClientAssignment>().AsNoTracking().ToListAsync(Token);
        assignments.Count.ShouldBe(2);
        assignments.ShouldAllBe(
            assignment => assignment.EndDate == null,
            "FR-042: no assignment is ever auto-ended, under any circumstance"
        );
    }

    // ----------------------------------------------------------- FR-045: a FUTURE end date is not open

    [Fact]
    public async Task GetBlockers_ForAnEdjerWhoseOnlyAssignmentEndsInTheFuture_IsPermitted()
    {
        // Arrange — FR-045, and the requirement the spec marks [extrapolation]. AC-19's precondition is
        // "without an end date", NOT "not yet ended". A confirmed rollout — departure agreed, date set,
        // date not yet arrived — must not deadlock the deactivation, and reading the criterion strictly
        // would deadlock exactly that case. Only a real database distinguishes NULL from a future date.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        var edjer = await CreateEdjerAsync(client, Request(employeeTypeId));
        var clientId = await SeedClientAsync("Buckeye Mutual");
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
        await SeedAssignmentAsync(edjer.Id, clientId, endDate: future);

        // Act
        var response = await client.GetAsync($"{BlockersRoute}/{edjer.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var verdict = await response.Content.ReadFromJsonAsync<EdjerDeactivationVerdict>(JsonOptions, Token);
        verdict.ShouldNotBeNull();
        verdict.Status.ShouldBe(
            EdjerDeactivationStatus.Permitted,
            "a dated departure that has not arrived is not an open-ended engagement"
        );
        verdict.BlockingAssignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeactivatingAnEdjerWhoseOnlyAssignmentEndsInTheFuture_Succeeds()
    {
        // Arrange — the write path's half of FR-045. The guard and the write must agree; asserting only
        // the read route would leave a route that says "go ahead" and a write that refuses.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        var edjer = await CreateEdjerAsync(client, Request(employeeTypeId));
        var clientId = await SeedClientAsync("Buckeye Mutual");
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
        await SeedAssignmentAsync(edjer.Id, clientId, endDate: future);

        // Act
        var response = await client.PutAsJsonAsync(
            $"{EdjerRoute}/{edjer.Id}",
            Request(employeeTypeId, edjer.Email, isActive: false),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var stored = await db.Set<CompassEmployee>()
            .AsNoTracking()
            .SingleAsync(employee => employee.Id == edjer.Id, Token);
        stored.IsActive.ShouldBeFalse();

        var assignment = await db.Set<CompassClientAssignment>().AsNoTracking().SingleAsync(Token);
        assignment.EndDate.ShouldBe(future, "the deactivation must not have rewritten the agreed date");
    }

    // ------------------------------------------ FR-043 / AC-45 / J14d: a Super Admin, acting alone

    [Fact]
    public async Task ASuperAdminAlone_EndsTheBlockers_ThenDeactivates_WithNoOpsHandoff()
    {
        // Arrange — J14d end to end, and BR-17's point: no assignment function is reserved against the
        // Compass root. One principal for every step. If the assignment PUT needed Compass Ops
        // specifically, this test fails at the arrangement rather than the assertion, which is the
        // failure worth having.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var superAdmin = _factory.AsCompassSuperAdmin();

        var edjer = await CreateEdjerAsync(superAdmin, Request(employeeTypeId));
        var clientId = await SeedClientAsync("Buckeye Mutual");
        var assignmentId = await SeedAssignmentAsync(edjer.Id, clientId, endDate: null);

        var refused = await superAdmin.PutAsJsonAsync(
            $"{EdjerRoute}/{edjer.Id}",
            Request(employeeTypeId, edjer.Email, isActive: false),
            Token
        );
        refused.StatusCode.ShouldBe(
            HttpStatusCode.UnprocessableEntity,
            "the arrangement depends on the first attempt being refused"
        );

        // Act — the same principal ends the blocker, then retries. No second actor anywhere.
        var ended = await superAdmin.PutAsJsonAsync(
            $"{AssignmentRoute}/{assignmentId}",
            new UpdateAssignmentRequest(AssignmentStart, AssignmentStart.AddMonths(6), null),
            Token
        );
        ended.StatusCode.ShouldBe(HttpStatusCode.OK, "FR-043: the Super Admin ends it themselves");

        var retried = await superAdmin.PutAsJsonAsync(
            $"{EdjerRoute}/{edjer.Id}",
            Request(employeeTypeId, edjer.Email, isActive: false),
            Token
        );

        // Assert
        retried.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var stored = await db.Set<CompassEmployee>()
            .AsNoTracking()
            .SingleAsync(employee => employee.Id == edjer.Id, Token);
        stored.IsActive.ShouldBeFalse("the retry after the operator's own fix must succeed");
    }

    // -------------------------------------------------------------------------- FR-044: the clear path

    [Fact]
    public async Task GetBlockers_ForAnEdjerWhoseAssignmentsAreAllEnded_IsPermitted()
    {
        // Arrange — FR-044. After the start date, or ck_client_assignment_end_on_or_after_start rejects
        // the arrangement itself, which would be the constraint doing its job rather than a hint about
        // the guard.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        var edjer = await CreateEdjerAsync(client, Request(employeeTypeId));
        var clientId = await SeedClientAsync("Granville Retail Group");
        await SeedAssignmentAsync(edjer.Id, clientId, endDate: new DateOnly(2024, 9, 30));

        // Act
        var response = await client.GetAsync($"{BlockersRoute}/{edjer.Id}", Token);

        // Assert
        var verdict = await response.Content.ReadFromJsonAsync<EdjerDeactivationVerdict>(JsonOptions, Token);
        verdict.ShouldNotBeNull();
        verdict.Status.ShouldBe(EdjerDeactivationStatus.Permitted);
    }

    [Fact]
    public async Task GetBlockers_ForAnAlreadyInactiveEdjer_ReportsAlreadyInactive()
    {
        // Arrange — the guard is on the TRANSITION active to inactive. An already-inactive EDJEr is not
        // toggling anything, and reporting Permitted would tell the form a deactivation is available
        // when there is nothing left to deactivate.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        var edjer = await CreateEdjerAsync(client, Request(employeeTypeId, isActive: false));

        // Act
        var response = await client.GetAsync($"{BlockersRoute}/{edjer.Id}", Token);

        // Assert
        var verdict = await response.Content.ReadFromJsonAsync<EdjerDeactivationVerdict>(JsonOptions, Token);
        verdict.ShouldNotBeNull();
        verdict.Status.ShouldBe(EdjerDeactivationStatus.AlreadyInactive);
    }

    [Fact]
    public async Task GetBlockers_ForAnUnknownEdjer_Returns404()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync($"{BlockersRoute}/987654", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --------------------------------------------------------------------------------- authorization

    [Fact]
    public async Task GetBlockers_AsCompassOps_IsAllowed()
    {
        // Arrange — Ops is the role that end-dates assignments, so Ops is who needs to know which ones
        // to end. The route is mounted on the assignment write group for exactly that reason.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var edjer = await CreateEdjerAsync(_factory.AsCompassSuperAdmin(), Request(employeeTypeId));

        // Act
        var response = await _factory.AsCompassOps().GetAsync($"{BlockersRoute}/{edjer.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBlockers_AsCompassAdmin_IsRefusedAtTheServer()
    {
        // Arrange — AC-44: "Compass Admin" is READ-ONLY however administrative the name sounds, and this
        // route exists to serve a deactivation decision that role can never make. The exact status is
        // asserted, never "not 200": under a stale DevBypass API a loose assertion passes and the gate
        // silently disappears.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var edjer = await CreateEdjerAsync(_factory.AsCompassSuperAdmin(), Request(employeeTypeId));

        // Act
        var response = await _factory.AsCompassAdmin().GetAsync($"{BlockersRoute}/{edjer.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBlockers_AsCompassSales_IsRefusedAtTheServer()
    {
        // Arrange — Sales reads the dashboard and the reports; it has no part in a deactivation.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var edjer = await CreateEdjerAsync(_factory.AsCompassSuperAdmin(), Request(employeeTypeId));

        // Act
        var response = await _factory.AsCompassSales().GetAsync($"{BlockersRoute}/{edjer.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ----------------------------------------------------------------------------------- arrangement

    private async Task<int> SeedEmployeeTypeAsync(bool isActive = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employeeType = new CompassEmployeeType
        {
            TypeName = $"Type-{Guid.NewGuid():N}"[..20],
            IsActive = isActive,
        };
        db.Set<CompassEmployeeType>().Add(employeeType);
        await db.SaveChangesAsync(Token);

        return employeeType.Id;
    }

    private async Task<int> SeedClientAsync(string clientName)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var client = new CompassClient { ClientName = clientName, IsInternal = false };
        db.Set<CompassClient>().Add(client);
        await db.SaveChangesAsync(Token);

        return client.Id;
    }

    /// <summary>Inserts an assignment row directly and returns its id.</summary>
    /// <remarks>
    /// Inserted rather than POSTed so the arrangement cannot be perturbed by the assignment write
    /// surface's own validation — a test of the guard should fail for the guard's reasons. The one place
    /// the real write surface IS used is the FR-043 round trip, where exercising it is the point.
    /// </remarks>
    private async Task<int> SeedAssignmentAsync(int employeeId, int clientId, DateOnly? endDate)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var assignment = new CompassClientAssignment
        {
            EmployeeId = employeeId,
            ClientId = clientId,
            StartDate = AssignmentStart,
            EndDate = endDate,
        };
        db.Set<CompassClientAssignment>().Add(assignment);
        await db.SaveChangesAsync(Token);

        return assignment.Id;
    }

    private static CompassEdjerRequest Request(
        int employeeTypeId,
        string? email = null,
        bool isActive = true
    ) =>
        new(
            "Ada",
            "Lovelace",
            new DateOnly(2020, 1, 6),
            email ?? $"edjer.{Guid.NewGuid():N}@example.test",
            employeeTypeId,
            CoachEmployeeId: null,
            "OH",
            isActive,
            TimesheetRequired: true,
            CanSubmitUnder40: false,
            IncludeInPayroll: true
        );

    private static async Task<CompassEdjerDto> CreateEdjerAsync(
        HttpClient client,
        CompassEdjerRequest request
    )
    {
        var response = await client.PostAsJsonAsync(EdjerRoute, request, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, "the arrangement itself must succeed");

        var created = await response.Content.ReadFromJsonAsync<CompassEdjerDto>(Token);
        created.ShouldNotBeNull();
        return created;
    }
}
