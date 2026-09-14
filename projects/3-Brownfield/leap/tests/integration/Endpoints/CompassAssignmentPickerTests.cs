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
/// US2/#63 — the assignment picker surface against real PostgreSQL. `GET /pickers/clients` is
/// Plan v7's named regression test **O6**: a client with zero assignments derives Inactive (BR-11)
/// and MUST still appear here, selectable (FR-009-FR-012, SC-002). `004` A-5 handed this test to
/// this stream as an open obligation.
/// </summary>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassAssignmentPickerTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassAssignmentPickerTests(IntegrationTestFactory factory) : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string AssignmentsUrl = "/api/compass/assignments";
    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<int> SeedClientAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var client = new Client { ClientName = $"{name}-{Guid.NewGuid():N}", IsInternal = false };
        db.Set<Client>().Add(client);
        await db.SaveChangesAsync(Token);
        return client.Id;
    }

    private async Task<int> SeedEmployeeAsync(string firstName, bool isActive = true)
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
            LastName = "Test",
            Email = $"{firstName}.{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = isActive,
            StateOfResidence = "OH",
        };
        db.Set<Employee>().Add(employee);
        await db.SaveChangesAsync(Token);
        return employee.Id;
    }

    // ---------------------------------------------------------------- T053 — the O6 named regression test

    [Fact]
    public async Task GetClientPickers_AZeroAssignmentClient_IsPresentAndSelectable_TheO6NamedRegressionTest()
    {
        // Arrange — a client seeded with NO assignment at all. It derives Inactive (BR-11), and O6
        // requires it be listed and selectable anyway (FR-009-FR-012, SC-002).
        await ResetDatabaseAsync();
        var brandNewClientId = await SeedClientAsync("BrandNew");
        var employeeId = await SeedEmployeeAsync("Ada");
        var client = _factory.AsCompassOps();

        // Act
        var pickerResponse = await client.GetAsync($"{AssignmentsUrl}/pickers/clients", Token);
        var rows = await pickerResponse.Content.ReadFromJsonAsync<List<ClientPickerRowDto>>(Token);

        // Assert — present, and derived Inactive rather than omitted or disabled.
        pickerResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var row = rows!.Single(r => r.Id == brandNewClientId);
        row.DerivedStatus.ShouldBe("Inactive");

        // Assert — and genuinely assignable, not merely listed.
        var createResponse = await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, brandNewClientId, new DateOnly(2026, 1, 1), null, null),
            Token);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // ---------------------------------------------------------------- T054

    [Fact]
    public async Task GetClientPickers_RowCount_EqualsTotalClients_NotOnlyActiveOnes()
    {
        // Arrange — FR-009: the picker must not silently drop any client from the count.
        await ResetDatabaseAsync();
        var activeId = await SeedClientAsync("Active");
        var employeeId = await SeedEmployeeAsync("Ada");
        await _factory.AsCompassOps().PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, activeId, new DateOnly(2026, 1, 1), null, null),
            Token);
        var inactiveId = await SeedClientAsync("Inactive");

        using var scope = _factory.Services.CreateScope();
        var totalClients = await scope.ServiceProvider.GetRequiredService<LeapDbContext>()
            .Set<Client>().CountAsync(Token);

        // Act
        var response = await _factory.AsCompassOps().GetAsync($"{AssignmentsUrl}/pickers/clients", Token);
        var rows = await response.Content.ReadFromJsonAsync<List<ClientPickerRowDto>>(Token);

        // Assert
        rows!.Count.ShouldBe(totalClients);
        rows.ShouldContain(r => r.Id == activeId);
        rows.ShouldContain(r => r.Id == inactiveId);
    }

    // ---------------------------------------------------------------- T055

    [Fact]
    public async Task GetClientPickers_AClientWhoseAssignmentsHaveAllEnded_IsPresentAndSelectable()
    {
        // Arrange — FR-011: re-engagement is not blocked once every prior assignment has ended.
        await ResetDatabaseAsync();
        var clientId = await SeedClientAsync("PastEngagement");
        var employeeId = await SeedEmployeeAsync("Ada");
        var client = _factory.AsCompassOps();
        await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(
                employeeId, clientId, new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31), null),
            Token);

        // Act
        var pickerResponse = await client.GetAsync($"{AssignmentsUrl}/pickers/clients", Token);
        var rows = await pickerResponse.Content.ReadFromJsonAsync<List<ClientPickerRowDto>>(Token);

        // Assert
        rows!.ShouldContain(r => r.Id == clientId);
        var reEngage = await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token);
        reEngage.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // ---------------------------------------------------------------- T056

    [Fact]
    public async Task FirstAssignment_FlipsTheClientsDerivedStatusToActive_WithNoSeparateAction()
    {
        // Arrange — US2 scenario 2 (J9b): status is derived, not a stored flag someone must set.
        await ResetDatabaseAsync();
        var clientId = await SeedClientAsync("Fresh");
        var employeeId = await SeedEmployeeAsync("Ada");
        var client = _factory.AsCompassOps();

        var before = await (await client.GetAsync($"{AssignmentsUrl}/pickers/clients", Token))
            .Content.ReadFromJsonAsync<List<ClientPickerRowDto>>(Token);
        before!.Single(r => r.Id == clientId).DerivedStatus.ShouldBe("Inactive");

        // Act
        await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token);

        // Assert
        var after = await (await client.GetAsync($"{AssignmentsUrl}/pickers/clients", Token))
            .Content.ReadFromJsonAsync<List<ClientPickerRowDto>>(Token);
        after!.Single(r => r.Id == clientId).DerivedStatus.ShouldBe("Active");
    }

    // ---------------------------------------------------------------- FR-003 — EDJEr picker

    [Fact]
    public async Task GetEdjerPickers_ExcludesAnInactiveEdjer()
    {
        // Arrange
        await ResetDatabaseAsync();
        var inactiveId = await SeedEmployeeAsync("Ivor", isActive: false);
        var activeId = await SeedEmployeeAsync("Ada");

        // Act
        var response = await _factory.AsCompassOps().GetAsync($"{AssignmentsUrl}/pickers/edjers", Token);
        var rows = await response.Content.ReadFromJsonAsync<List<EdjerPickerRowDto>>(Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        rows!.ShouldContain(r => r.Id == activeId);
        rows!.ShouldNotContain(r => r.Id == inactiveId);
    }
}
