using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

public class AdminUserRoleEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetAllUserRoles_ResolvesEmployeeNameViaEmployeeDirectory()
    {
        // Arrange — the user-roles list resolves EmployeeName via IEmployeeDirectory
        // (PersonEmployeeDirectory, backed directly by the `people` table).
        var edjeId = Guid.Parse("00000000-0000-0000-0000-000000000044");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
            db.People.Add(new Person { Id = Guid.NewGuid(), EdjeId = edjeId, FirstName = "Rolelist", LastName = "Person", Email = "rolelist.person@example.com", IsActive = true });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await _client.PostAsJsonAsync(
            "/api/admin/user-roles",
            new UserRoleRequest { EdjeId = edjeId, Role = "Ops", Reason = "name-resolution test" },
            TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/admin/user-roles", TestContext.Current.CancellationToken);

        // Assert — the assigned row shows the employee name resolved from the DB reader.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.ShouldContain("Rolelist Person");
    }

    [Fact]
    public async Task GetAllUserRoles_ReturnsOk()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/admin/user-roles", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRolesByEmployee_ReturnsOk()
    {
        // Arrange
        var edjeId = Guid.NewGuid();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            $"/api/admin/user-roles/by-employee/{edjeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUsersByRole_ReturnsOk()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/admin/user-roles/by-role/Admin", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AssignRole_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var request = new UserRoleRequest
        {
            EdjeId = Guid.NewGuid(),
            Role = "Manager",
            Reason = "Promoted to manager"
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/admin/user-roles", request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AssignRole_CompassRole_IsRefusedAndNeverPersisted()
    {
        // Arrange -- FR-014: no Timesheet or OOTO role may grant authority in Compass. This
        // endpoint is gated by a TIMESHEET SuperAdmin policy, so without a guard it can mint
        // standing Compass authority for anyone -- bypassing Google Compass group membership and
        // SAML sign-in entirely. CompositeAuthorizationResolver's DB fallback means a persisted
        // row here is a live grant, not inert data (BR-12/FR-013).
        var edjeId = Guid.NewGuid();
        var request = new UserRoleRequest
        {
            EdjeId = edjeId,
            Role = RolePolicy.CompassSuperAdminRole,
            Reason = "should never be accepted",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/admin/user-roles", request, TestContext.Current.CancellationToken);

        // Assert -- refused, and nothing was persisted for this user.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        HttpResponseMessage rolesResponse = await _client.GetAsync(
            $"/api/admin/user-roles/by-employee/{edjeId}", TestContext.Current.CancellationToken);
        var roles = await rolesResponse.Content.ReadFromJsonAsync<List<string>>(
            cancellationToken: TestContext.Current.CancellationToken);
        roles.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveRole_NonExistentId_ReturnsNotFound()
    {
        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            "/api/admin/user-roles/999999", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssignThenRemoveRole_ReturnsNoContent()
    {
        // Arrange - assign a role first
        var edjeId = Guid.NewGuid();
        var assignRequest = new UserRoleRequest
        {
            EdjeId = edjeId,
            Role = "TimesheetProcessor",
            Reason = "Testing removal"
        };
        await _client.PostAsJsonAsync(
            "/api/admin/user-roles", assignRequest, TestContext.Current.CancellationToken);

        // Find the role ID by listing all
        var allResponse = await _client.GetAsync(
            "/api/admin/user-roles", TestContext.Current.CancellationToken);
        var content = await allResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // The newly assigned role should be in the list
        content.ShouldContain("TimesheetProcessor");
    }

    [Fact]
    public async Task GetUsersByRole_AfterAssignment_ReturnsAssignedUser()
    {
        // Arrange - assign role
        var edjeId = Guid.NewGuid();
        var request = new UserRoleRequest
        {
            EdjeId = edjeId,
            Role = "HR",
            Reason = "Testing by-role lookup"
        };
        await _client.PostAsJsonAsync(
            "/api/admin/user-roles", request, TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/admin/user-roles/by-role/HR", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.ShouldContain(edjeId.ToString());
    }

    [Fact]
    public async Task RemoveRole_ExistingRole_ReturnsNoContent()
    {
        // Arrange - assign a role, then find its ID
        var edjeId = Guid.NewGuid();
        var assignRequest = new UserRoleRequest
        {
            EdjeId = edjeId,
            Role = "Accounting",
            Reason = "Testing removal"
        };
        await _client.PostAsJsonAsync(
            "/api/admin/user-roles", assignRequest, TestContext.Current.CancellationToken);

        // Get all roles and find the one we just assigned
        var allResponse = await _client.GetAsync(
            "/api/admin/user-roles", TestContext.Current.CancellationToken);
        var content = await allResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var roles = JsonSerializer.Deserialize<JsonElement>(content);
        int roleId = 0;
        foreach (var role in roles.EnumerateArray())
        {
            if (role.GetProperty("edjeId").GetString() == edjeId.ToString()
                && role.GetProperty("role").GetString() == "Accounting")
            {
                roleId = role.GetProperty("id").GetInt32();
                break;
            }
        }
        roleId.ShouldBeGreaterThan(0, "Should find the assigned role");

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            $"/api/admin/user-roles/{roleId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
