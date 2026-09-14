using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Endpoint tests for the assignment write surface over the real request pipeline (InMemory
/// provider), covering the success paths <see cref="CompassAssignmentAuthorizationTests"/>
/// deliberately does not — that file's denials are refused by the policy before any handler runs,
/// so it never reaches <c>GetAll</c>/<c>GetById</c>/<c>Update</c>. The business-rule variants
/// (FR-003/FR-008 rejections, the audit trail, two concurrent assignments) are asserted against real
/// PostgreSQL in <c>CompassAssignmentEndpointsTests</c> (integration project); this file exists so
/// those same handlers are exercised here too.
/// </summary>
public class CompassAssignmentEndpointsTests
{
    private const string AssignmentsUrl = "/api/compass/assignments";
    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(int EmployeeId, int ClientId)> SeedAsync(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        db.Set<EmployeeType>().Add(new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"ada-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
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
        return (employee.Id, client.Id);
    }

    [Fact]
    public async Task GetAll_ReturnsEveryAssignment()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var (employeeId, clientId) = await SeedAsync(factory);
        var client = factory.AsCompassOps();
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
        await using var factory = new TestWebApplicationFactory();
        var (employeeId, clientId) = await SeedAsync(factory);
        var client = factory.AsCompassOps();
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
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassOps();

        // Act
        var response = await client.GetAsync($"{AssignmentsUrl}/999999", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_AdjustingTheNote_Succeeds()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var (employeeId, clientId) = await SeedAsync(factory);
        var client = factory.AsCompassOps();
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

    [Fact]
    public async Task Put_EndDatingAnAssignment_SetsIsCurrentFalse()
    {
        // Arrange — the first NULL-to-value EndDate transition (J14 step 5).
        await using var factory = new TestWebApplicationFactory();
        var (employeeId, clientId) = await SeedAsync(factory);
        var client = factory.AsCompassOps();
        var created = await (await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2020, 1, 1), null, null),
            Token)).Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await client.PutAsJsonAsync(
            $"{AssignmentsUrl}/{created!.Id}",
            new UpdateAssignmentRequest(created.StartDate, new DateOnly(2020, 6, 30), null),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(Token);
        dto!.EndDate.ShouldBe(new DateOnly(2020, 6, 30));
        dto.IsCurrent.ShouldBeFalse();
    }

    // ------------------------------------------------------------------ US2 (#63) — pickers

    [Fact]
    public async Task GetClientPickers_ReturnsAZeroAssignmentClient_TheO6NamedRegressionTest()
    {
        // Arrange — Plan v7's named regression test O6: a client with zero assignments derives
        // Inactive (BR-11) and MUST still appear here, selectable (FR-009-FR-012).
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var brandNew = new Client { ClientName = $"BrandNew-{Guid.NewGuid():N}", IsInternal = false };
        db.Set<Client>().Add(brandNew);
        await db.SaveChangesAsync(Token);
        var client = factory.AsCompassOps();

        // Act
        var response = await client.GetAsync($"{AssignmentsUrl}/pickers/clients", Token);
        var rows = await response.Content.ReadFromJsonAsync<List<ClientPickerRowDto>>(Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        rows!.ShouldContain(r => r.Id == brandNew.Id && r.DerivedStatus == "Inactive");
    }

    [Fact]
    public async Task GetEdjerPickers_ExcludesAnInactiveEdjer()
    {
        // Arrange — FR-003: only an active EDJEr may receive a new assignment.
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        db.Set<EmployeeType>().Add(new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        var inactive = new Employee
        {
            FirstName = "Inactive",
            LastName = "Edjer",
            Email = $"inactive-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = false,
            StateOfResidence = "OH",
        };
        db.Set<Employee>().Add(inactive);
        await db.SaveChangesAsync(Token);
        var client = factory.AsCompassOps();

        // Act
        var response = await client.GetAsync($"{AssignmentsUrl}/pickers/edjers", Token);
        var rows = await response.Content.ReadFromJsonAsync<List<EdjerPickerRowDto>>(Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        rows!.ShouldNotContain(r => r.Id == inactive.Id);
    }

    // ------------------------------------------------------------------ Delete — issue #593

    [Fact]
    public async Task Delete_ThenGetById_Returns404()
    {
        // Arrange — authorization is covered by CompassAssignmentAuthorizationTests; this file's job
        // is the handler's own behaviour once the policy admits the caller.
        await using var factory = new TestWebApplicationFactory();
        var (employeeId, clientId) = await SeedAsync(factory);
        var client = factory.AsCompassOps();
        var created = await (await client.PostAsJsonAsync(
            AssignmentsUrl,
            new CreateAssignmentRequest(employeeId, clientId, new DateOnly(2026, 1, 1), null, null),
            Token)).Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await factory.AsCompassSuperAdmin().DeleteAsync($"{AssignmentsUrl}/{created!.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var getResponse = await client.GetAsync($"{AssignmentsUrl}/{created.Id}", Token);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.DeleteAsync($"{AssignmentsUrl}/999999", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
