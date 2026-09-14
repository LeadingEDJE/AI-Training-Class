using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// US1/#62 — the assignment write surface against real PostgreSQL: create, read, adjust and end an
/// engagement as Compass Ops.
/// </summary>
/// <remarks>
/// Authorization denials live in the unit project's <c>CompassAssignmentAuthorizationTests</c>, per the
/// module convention of keeping a denial failure from being mistaken for behaviour (research R-7).
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassAssignmentEndpointsTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassAssignmentEndpointsTests(IntegrationTestFactory factory) : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string AssignmentsUrl = "/api/compass/assignments";
    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Seeds one active EDJEr and returns its id.</summary>
    private async Task<int> SeedEmployeeAsync(string firstName = "Ada", string lastName = "Lovelace", bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (!await db.Set<EmployeeType>().AnyAsync(Token))
        {
            db.Set<EmployeeType>().Add(new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        }

        var employee = new Employee
        {
            FirstName = firstName,
            LastName = lastName,
            Email = $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = isActive,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        };
        db.Set<Employee>().Add(employee);
        await db.SaveChangesAsync(Token);
        return employee.Id;
    }

    /// <summary>Seeds one client and returns its id.</summary>
    /// <param name="name">A name prefix; a GUID suffix keeps it unique across tests.</param>
    /// <param name="isInternal">
    /// Whether this is an internal EDJE ("beach") client — the flag issue #518 denormalises onto the
    /// assignment row.
    /// </param>
    private async Task<int> SeedClientAsync(string name = "Acme", bool isInternal = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var client = new Client { ClientName = $"{name}-{Guid.NewGuid():N}", IsInternal = isInternal };
        db.Set<Client>().Add(client);
        await db.SaveChangesAsync(Token);
        return client.Id;
    }

    // ---------------------------------------------------------------- T029

    [Fact]
    public async Task Post_AsCompassOps_CreatesAnAssignment()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        var request = new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null);

        // Act
        var response = await client.PostAsJsonAsync(AssignmentsUrl, request, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();
        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto.ShouldNotBeNull();
        dto.EmployeeId.ShouldBe(employeeId);
        dto.ClientId.ShouldBe(clientId);
        dto.StartDate.ShouldBe(new DateOnly(2026, 1, 1));
        dto.EndDate.ShouldBeNull();
    }

    [Fact]
    public async Task Post_WithAnInactiveEdjer_IsRejected()
    {
        // Arrange — FR-003
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync(isActive: false);
        var clientId = await SeedClientAsync();
        var request = new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null);

        // Act
        var response = await client.PostAsJsonAsync(AssignmentsUrl, request, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_WithAnUnknownClientId_IsRejected()
    {
        // Arrange — an invalid ClientId must be rejected with a clean 400, not left to hit the
        // client_assignment FK constraint (23503) as an untranslated 500.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var request = new CreateAssignmentRequest(employeeId, -1, new DateOnly(2026, 1, 1), null, null);

        // Act
        var response = await client.PostAsJsonAsync(AssignmentsUrl, request, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- T030

    [Fact]
    public async Task Get_ReturnsEveryAssignment()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token);

        // Act
        var response = await client.GetAsync(AssignmentsUrl, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<AssignmentRowDto>>(Token);
        rows.ShouldNotBeNull();
        rows.ShouldContain(r => r.EmployeeId == employeeId && r.ClientId == clientId);
    }

    [Fact]
    public async Task GetById_Found_ReturnsTheAssignment()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        var created = await (await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token)).Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await client.GetAsync($"{AssignmentsUrl}/{created!.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto!.Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        // Arrange — FR-004
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();

        // Act
        var response = await client.GetAsync($"{AssignmentsUrl}/999999", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_NotFound_Returns404()
    {
        // Arrange — FR-004
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var request = new UpdateAssignmentRequest(new DateOnly(2026, 1, 1), null, null);

        // Act
        var response = await client.PutAsJsonAsync($"{AssignmentsUrl}/999999", request, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_AdjustingTheNote_Succeeds()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        var created = await (await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token)).Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await client.PutAsJsonAsync(
            $"{AssignmentsUrl}/{created!.Id}",
            new UpdateAssignmentRequest(created.StartDate, null, "Adjusted note"),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto!.Note.ShouldBe("Adjusted note");
    }

    // ---------------------------------------------------------------- T030a

    [Fact]
    public async Task OneEdjer_CanHoldTwoConcurrentAssignments()
    {
        // Arrange — AC-27, FR-002: zero-to-many, explicit rather than assumed from a single-assignment test.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var firstClientId = await SeedClientAsync("First");
        var secondClientId = await SeedClientAsync("Second");

        // Act
        var first = await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, firstClientId, new DateOnly(2026, 1, 1), null, null),
            Token);
        var second = await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, secondClientId, new DateOnly(2026, 1, 1), null, null),
            Token);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);

        var rows = await (await client.GetAsync(AssignmentsUrl, Token))
            .Content.ReadFromJsonAsync<List<AssignmentRowDto>>(Token);
        rows!.Count(r => r.EmployeeId == employeeId).ShouldBe(2);
    }

    // ---------------------------------------------------------------- T031

    [Fact]
    public async Task Put_EndDatingAnAssignment_PersistsTheEndDate()
    {
        // Arrange — FR-004
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        var created = await (await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token)).Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await client.PutAsJsonAsync(
            $"{AssignmentsUrl}/{created!.Id}",
            new UpdateAssignmentRequest(created.StartDate, new DateOnly(2026, 6, 30), null),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto!.EndDate.ShouldBe(new DateOnly(2026, 6, 30));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var stored = await db.Set<ClientAssignment>().AsNoTracking()
            .SingleAsync(a => a.Id == created.Id, Token);
        stored.EndDate.ShouldBe(new DateOnly(2026, 6, 30));
    }

    // ---------------------------------------------------------------- T050

    [Fact]
    public async Task Get_WithNoNoteRecorded_OmitsTheNoteKeyEntirely()
    {
        // Arrange — ADR-008 rule 2: withheld data must be ABSENT from the JSON, never merely null,
        // because a nullable DTO cannot distinguish "no note was given" from "note is hidden". This
        // resource's whole route group requires Compass Ops or the Compass root (research R-7), so
        // every reachable caller here is already elevated — there is no reachable non-elevated
        // viewer of THIS endpoint to withhold Note from. The absent-vs-null mechanism is proven here
        // via a note that was simply never provided, using the `GetRawJsonAsync` helper pattern `005`
        // uses for the same assertion shape.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        var created = await (await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token)).Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var raw = await client.GetRawJsonAsync($"{AssignmentsUrl}/{created!.Id}");

        // Assert
        raw.ShouldNotContain(
            "\"note\"",
            customMessage: "note must be ABSENT, not present as null, when none was recorded");
    }

    [Fact]
    public async Task Post_WithEndDateBeforeStartDate_IsRejected()
    {
        // Arrange — FR-008
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        var request = new CreateAssignmentRequest(
            employeeId, clientId, new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 1), null);

        // Act
        var response = await client.PostAsJsonAsync(AssignmentsUrl, request, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ----------------- Issue #518: the CLIENT's internal flag, denormalised onto the assignment row

    [Fact]
    public async Task GetById_UnderAnInternalClient_DeliversIsInternalTrue()
    {
        // Arrange — issue #518. The assignment screen hides the contract and invoicing surfaces from
        // this flag, so it has to arrive on the assignment row itself rather than costing a second
        // fetch of the client.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync("Beach", isInternal: true);
        var created = await (await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token)).Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await client.GetAsync($"{AssignmentsUrl}/{created!.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto!.IsInternal.ShouldBeTrue();
    }

    [Fact]
    public async Task GetById_UnderAnExternalClient_DeliversIsInternalFalse()
    {
        // Arrange — the control: a flag that is always true would satisfy the test above on its own.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync("Buckeye", isInternal: false);
        var created = await (await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token)).Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await client.GetAsync($"{AssignmentsUrl}/{created!.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto!.IsInternal.ShouldBeFalse();
    }

    // ------------------------------------------------------------------ Delete — issue #593

    /// <summary>
    /// The real proof this feature needs against Postgres: <c>ClientAssignmentConfiguration</c>
    /// declares <c>DeleteBehavior.Restrict</c> on the SOW→assignment FK, so a delete that did not
    /// remove the SOWs FIRST would fail 23503 here — not against the InMemory provider, which does
    /// not enforce the constraint at all — this pattern exists to catch exactly that class of
    /// bug.
    /// </summary>
    [Fact]
    public async Task Delete_WithSowsUnderIt_RemovesTheAssignmentAndEverySow()
    {
        // Arrange — ekrumla's answer on #593: deleting an assignment deletes all SOWs beneath it.
        await ResetDatabaseAsync();
        var opsClient = _factory.AsCompassOps();
        var employeeId = await SeedEmployeeAsync();
        var clientId = await SeedClientAsync();
        var created = await (await opsClient.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2024, 1, 1), null, null),
            Token)).Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        var sowsUrl = $"{AssignmentsUrl}/{created!.Id}/sows";
        await opsClient.PostAsJsonAsync(
            sowsUrl,
            new CreateSowRequest(
                SowType.InitialContract, RateIncrease: false, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), null),
            Token);

        // Act
        var response = await _factory.AsCompassSuperAdmin().DeleteAsync($"{AssignmentsUrl}/{created.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<ClientAssignment>().AnyAsync(a => a.Id == created.Id, Token)).ShouldBeFalse();
        (await db.Set<Sow>().AnyAsync(s => s.ClientAssignmentId == created.Id, Token)).ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.DeleteAsync($"{AssignmentsUrl}/999999", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
