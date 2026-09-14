using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// US3/#66 — the SOW write surface against real PostgreSQL: create Initial Contract and Extension
/// periods under an assignment, list and edit them, and confirm overlap/date-order are actionable
/// messages rather than database errors (contracts/sow-write-surface.md).
/// </summary>
/// <remarks>
/// Authorization denials live in the unit project's <c>CompassSowAuthorizationTests</c>, mirroring the
/// assignment surface's convention (research R-7) so a denial failure is not mistaken for behaviour.
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassSowEndpointsTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassSowEndpointsTests(IntegrationTestFactory factory) : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string SowsUrl(int assignmentId) => $"/api/compass/assignments/{assignmentId}/sows";

    /// <summary>Seeds one active EDJEr, one client, and one open-ended assignment; returns the assignment id.</summary>
    /// <param name="clientIsInternal">
    /// Whether the seeded client is an internal EDJE ("beach") client — issue #518's create guard.
    /// </param>
    private async Task<int> SeedAssignmentAsync(bool clientIsInternal = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (!await db.Set<EmployeeType>().AnyAsync(Token))
        {
            db.Set<EmployeeType>().Add(new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        }

        var employee = new Employee
        {
            FirstName = "Maya",
            LastName = "Alvarez",
            Email = $"maya-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        };
        var client = new Client
        {
            ClientName = $"Buckeye-{Guid.NewGuid():N}",
            IsInternal = clientIsInternal,
        };
        db.Set<Employee>().Add(employee);
        db.Set<Client>().Add(client);
        await db.SaveChangesAsync(Token);

        var assignment = new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = new DateOnly(2024, 4, 1),
        };
        db.Set<ClientAssignment>().Add(assignment);
        await db.SaveChangesAsync(Token);

        return assignment.Id;
    }

    private static CreateSowRequest InitialContract(
        DateOnly start,
        DateOnly end,
        string? note = null
    ) => new(SowType.InitialContract, RateIncrease: false, start, end, note);

    private static CreateSowRequest Extension(
        DateOnly start,
        DateOnly end,
        bool rateIncrease = false,
        string? note = null
    ) => new(SowType.SowExtension, rateIncrease, start, end, note);

    // ------------------------------- Issue #518: an internal client's assignment has no SOWs

    [Fact]
    public async Task Post_UnderAnInternalClientsAssignment_IsRejectedWith400()
    {
        // Arrange — issue #518. Asserting the EXACT status, never "not 201": a loose denial assertion
        // passes against a misconfigured environment and quietly retires the gate.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync(clientIsInternal: true);

        // Act
        var response = await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31)),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_UnderAnExternalClientsAssignment_StillCreates()
    {
        // Arrange — the control for the test above, so the refusal is proven to turn on the client's
        // internal flag rather than on the POST path being broken.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync(clientIsInternal: false);

        // Act
        var response = await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31)),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // ---------------------------------------------------------------- T064

    [Fact]
    public async Task Post_InitialContract_Creates()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var request = InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31), "Initial 9-month SOW");

        // Act
        var response = await client.PostAsJsonAsync(SowsUrl(assignmentId), request, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();
        var dto = await response.Content.ReadFromJsonAsync<SowRowDto>(Token);
        dto.ShouldNotBeNull();
        dto.SowType.ShouldBe("InitialContract");
        dto.SowStartDate.ShouldBe(new DateOnly(2024, 4, 1));
        dto.SowEndDate.ShouldBe(new DateOnly(2024, 12, 31));
    }

    [Fact]
    public async Task Post_Extension_Creates()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31)),
            Token);
        var request = Extension(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), rateIncrease: true, "FY25 renewal");

        // Act
        var response = await client.PostAsJsonAsync(SowsUrl(assignmentId), request, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<SowRowDto>(Token);
        dto!.SowType.ShouldBe("SowExtension");
        dto.RateIncrease.ShouldBe(true);
    }

    [Fact]
    public async Task Get_ReturnsEveryPeriodForTheAssignment()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31)),
            Token);

        // Act
        var response = await client.GetAsync(SowsUrl(assignmentId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<SowRowDto>>(Token);
        rows.ShouldNotBeNull();
        rows.ShouldContain(r => r.SowType == "InitialContract");
    }

    [Fact]
    public async Task Put_EditingAPeriod_Persists()
    {
        // Arrange — FR-022
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var created = await (await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31)),
            Token)).Content.ReadFromJsonAsync<SowRowDto>(Token);
        var update = new UpdateSowRequest(
            SowType.InitialContract, RateIncrease: false, created!.SowStartDate, created.SowEndDate, "Edited note");

        // Act
        var response = await client.PutAsJsonAsync($"{SowsUrl(assignmentId)}/{created.Id}", update, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<SowRowDto>(Token);
        dto!.Note.ShouldBe("Edited note");
    }

    [Fact]
    public async Task Put_NotFound_Returns404()
    {
        // Arrange — FR-022
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var update = new UpdateSowRequest(
            SowType.InitialContract, RateIncrease: false, new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31), null);

        // Act
        var response = await client.PutAsJsonAsync($"{SowsUrl(assignmentId)}/999999", update, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- T065

    [Fact]
    public async Task Post_OverlappingPeriod_IsRejectedAndIdentifiesTheConflict()
    {
        // Arrange — FR-019, BR-3
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31)),
            Token);
        var overlapping = Extension(new DateOnly(2024, 10, 1), new DateOnly(2025, 6, 30));

        // Act
        var response = await client.PostAsJsonAsync(SowsUrl(assignmentId), overlapping, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync(Token);

        // mm/dd/yyyy over the wire too (issue #234, #306). The SOW form renders this message verbatim
        // as its whole-form error, so the date a user reads is decided here, not in the SPA.
        body.ShouldContain("04/01/2024", Case.Insensitive);
        body.ShouldNotContain(
            "2024-04-01",
            Case.Insensitive,
            "the ISO wire format is exactly what #234 exists to keep off a screen"
        );
    }

    // ---------------------------------------------------------------- T066

    [Fact]
    public async Task Post_EndDateBeforeStartDate_IsRejected()
    {
        // Arrange — FR-020, FR-023
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var request = InitialContract(new DateOnly(2024, 12, 31), new DateOnly(2024, 4, 1));

        // Act
        var response = await client.PostAsJsonAsync(SowsUrl(assignmentId), request, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_EndDateBeforeStartDate_IsRejected()
    {
        // Arrange — FR-020, FR-023, applies on edit too
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var created = await (await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31)),
            Token)).Content.ReadFromJsonAsync<SowRowDto>(Token);
        var update = new UpdateSowRequest(
            SowType.InitialContract, RateIncrease: false, new DateOnly(2024, 12, 31), new DateOnly(2024, 4, 1), null);

        // Act
        var response = await client.PutAsJsonAsync($"{SowsUrl(assignmentId)}/{created!.Id}", update, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- T067

    [Fact]
    public async Task Post_PeriodsWithAGapBetweenThem_AreBothAccepted()
    {
        // Arrange — FR-021: gaps are allowed, only overlap is refused
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var first = await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 9, 30)),
            Token);

        // Act — a gap of Oct 1 – Dec 31 separates the two periods
        var second = await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            Extension(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
            Token);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // ---------------------------------------------------------------- T068

    [Fact]
    public async Task Post_RateIncreaseOnInitialContract_IsRejected()
    {
        // Arrange — FR-017, AC-35: rate increase is Extension-only
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var request = new CreateSowRequest(
            SowType.InitialContract, RateIncrease: true, new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31), null);

        // Act
        var response = await client.PostAsJsonAsync(SowsUrl(assignmentId), request, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- T069

    [Fact]
    public async Task Post_LegacyMigrated_IsRejectedOnInput()
    {
        // Arrange — FR-016: reserved for the migration principal, refused here regardless of caller
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var request = new CreateSowRequest(
            SowType.LegacyMigrated, RateIncrease: false, new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31), null);

        // Act
        var response = await client.PostAsJsonAsync(SowsUrl(assignmentId), request, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_LegacyMigrated_IsRejectedOnInput()
    {
        // Arrange — FR-016 applies to the update request too
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var created = await (await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31)),
            Token)).Content.ReadFromJsonAsync<SowRowDto>(Token);
        var update = new UpdateSowRequest(
            SowType.LegacyMigrated, RateIncrease: false, created!.SowStartDate, created.SowEndDate, null);

        // Act
        var response = await client.PutAsJsonAsync($"{SowsUrl(assignmentId)}/{created.Id}", update, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- T070

    [Fact]
    public async Task Post_SuccessfulSave_SetsHasPassedApplicationValidationTrue()
    {
        // Arrange — FR-024. Not exposed in the DTO (contract §3), so verified against the row directly.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var created = await (await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31)),
            Token)).Content.ReadFromJsonAsync<SowRowDto>(Token);

        // Act
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var stored = await db.Set<Sow>().AsNoTracking().SingleAsync(s => s.Id == created!.Id, Token);

        // Assert
        stored.HasPassedApplicationValidation.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- T071

    [Fact]
    public async Task Put_EditingAPeriod_DoesNotReNotifyTheCoach()
    {
        // Arrange — FR-027, AC-34: editing never re-notifies. CoachNotifier itself does not exist
        // until US4 (Phase 6); this regression test is authored ahead of it so the invariant is
        // already under test the moment the notifier is wired in, mirroring the O6 pattern from US2.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var created = await (await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            Extension(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31)),
            Token)).Content.ReadFromJsonAsync<SowRowDto>(Token);

        using var seedScope = _factory.Services.CreateScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var before = await seedDb.NotificationLogs.CountAsync(Token);

        // Act
        var update = new UpdateSowRequest(
            SowType.SowExtension, RateIncrease: true, created!.SowStartDate, created.SowEndDate, "Edited");
        var response = await client.PutAsJsonAsync($"{SowsUrl(assignmentId)}/{created.Id}", update, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert
        var after = await seedDb.NotificationLogs.CountAsync(Token);
        after.ShouldBe(before);
    }

    // ---------------------------------------------------------------- T072

    [Fact]
    public async Task Get_WithNoNoteRecorded_OmitsTheNoteKeyEntirely()
    {
        // Arrange — ADR-008 rule 2, FR-018, BR-1: withheld data must be ABSENT, never merely null.
        // This resource's whole route group requires Compass Ops or the Compass root (contract §1), so
        // every reachable caller here is already elevated — mirroring AssignmentRowDto's own documented
        // deviation, there is no reachable non-elevated viewer of THIS endpoint to withhold RateIncrease
        // or Note from. The absent-vs-null mechanism is proven via a note that was simply never given.
        //
        // The SOW surface maps no `GET /{sowId}` (list-only reads, per CompassSowEndpoints' own doc
        // comment) — asserting against that URL 404s and the "note" assertion below passes vacuously
        // against the 404 body. Exercise the real ADR-008 serialization through the list endpoint
        // instead, and assert the status code so a route mismatch fails loudly rather than silently.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var created = await (await client.PostAsJsonAsync(
            SowsUrl(assignmentId),
            InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31)),
            Token)).Content.ReadFromJsonAsync<SowRowDto>(Token);

        // Act
        var response = await client.GetAsync(SowsUrl(assignmentId), Token);
        var raw = await response.Content.ReadAsStringAsync(Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        raw.ShouldContain(
            $"\"id\":{created!.Id}", customMessage: "the created SOW must be present in the list");
        raw.ShouldNotContain(
            "\"note\"",
            customMessage: "note must be ABSENT, not present as null, when none was recorded");
    }

    // ------------------------------------------------------------------ Delete — issue #593

    [Fact]
    public async Task Delete_RemovesOnlyThatSow_LeavingItsSiblingIntact()
    {
        // Arrange — ekrumla's answer on #593: a single SOW may be deleted without touching the rest
        // of the assignment. Two sibling periods, so the assertion proves scope, not merely "a delete
        // happened".
        await ResetDatabaseAsync();
        var opsClient = _factory.AsCompassOps();
        var assignmentId = await SeedAssignmentAsync();
        var toDelete = await (await opsClient.PostAsJsonAsync(
            SowsUrl(assignmentId),
            InitialContract(new DateOnly(2024, 4, 1), new DateOnly(2024, 9, 30)),
            Token)).Content.ReadFromJsonAsync<SowRowDto>(Token);
        var sibling = await (await opsClient.PostAsJsonAsync(
            SowsUrl(assignmentId),
            Extension(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
            Token)).Content.ReadFromJsonAsync<SowRowDto>(Token);

        // Act
        var response = await _factory.AsCompassSuperAdmin()
            .DeleteAsync($"{SowsUrl(assignmentId)}/{toDelete!.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var remaining = await (await opsClient.GetAsync(SowsUrl(assignmentId), Token))
            .Content.ReadFromJsonAsync<List<SowRowDto>>(Token);
        remaining!.ShouldNotContain(r => r.Id == toDelete.Id);
        remaining!.ShouldContain(r => r.Id == sibling!.Id);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.DeleteAsync($"{SowsUrl(assignmentId)}/999999", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
