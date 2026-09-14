using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

public class AdminUserRoleEndpointsTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private const string BaseUrl = "/api/admin/user-roles";

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyList()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await Client.GetAsync(
            BaseUrl, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roles = await response.Content.ReadFromJsonAsync<List<UserRoleDto>>(
            TestContext.Current.CancellationToken);
        roles.ShouldNotBeNull();
        roles.ShouldBeEmpty();
    }

    [Fact]
    public async Task AssignRole_ValidRequest_ReturnsCreated()
    {
        // Arrange
        await ResetDatabaseAsync();
        var request = new UserRoleRequest
        {
            EdjeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Role = "EDJEr",
            Reason = "Integration test assignment"
        };

        // Act
        var response = await Client.PostAsJsonAsync(
            BaseUrl, request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location.ToString().ShouldContain("by-employee");
    }

    [Fact]
    public async Task GetByEmployee_AfterAssign_ReturnsRoles()
    {
        // Arrange
        await ResetDatabaseAsync();
        var edjeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var request = new UserRoleRequest
        {
            EdjeId = edjeId,
            Role = "Manager",
            Reason = "Test"
        };
        await Client.PostAsJsonAsync(BaseUrl, request, TestContext.Current.CancellationToken);

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}/by-employee/{edjeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roles = await response.Content.ReadFromJsonAsync<List<string>>(
            TestContext.Current.CancellationToken);
        roles.ShouldNotBeNull();
        roles.ShouldContain("Manager");
    }

    [Fact]
    public async Task GetByRole_AfterAssign_ReturnsUsersWithRole()
    {
        // Arrange
        await ResetDatabaseAsync();
        var edjeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var request = new UserRoleRequest
        {
            EdjeId = edjeId,
            Role = "HR",
            Reason = "Test"
        };
        await Client.PostAsJsonAsync(BaseUrl, request, TestContext.Current.CancellationToken);

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}/by-role/HR", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var users = await response.Content.ReadFromJsonAsync<List<UserRoleDto>>(
            TestContext.Current.CancellationToken);
        users.ShouldNotBeNull();
        users.Count.ShouldBeGreaterThan(0);
        users.ShouldAllBe(u => u.Role == "HR");
    }

    [Fact]
    public async Task RemoveRole_ExistingRole_Returns204()
    {
        // Arrange
        await ResetDatabaseAsync();
        var edjeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var request = new UserRoleRequest
        {
            EdjeId = edjeId,
            Role = "Accounting",
            Reason = "Test"
        };
        await Client.PostAsJsonAsync(BaseUrl, request, TestContext.Current.CancellationToken);

        // Get the role id
        var listResponse = await Client.GetAsync(BaseUrl, TestContext.Current.CancellationToken);
        var roles = await listResponse.Content.ReadFromJsonAsync<List<UserRoleDto>>(
            TestContext.Current.CancellationToken);
        var roleId = roles!.First(r => r.Role == "Accounting").Id;

        // Act
        var response = await Client.DeleteAsync(
            $"{BaseUrl}/{roleId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify removal
        var verifyResponse = await Client.GetAsync(BaseUrl, TestContext.Current.CancellationToken);
        var remaining = await verifyResponse.Content.ReadFromJsonAsync<List<UserRoleDto>>(
            TestContext.Current.CancellationToken);
        remaining!.ShouldNotContain(r => r.Role == "Accounting" && r.EdjeId == edjeId);
    }

    [Fact]
    public async Task RemoveRole_NonExistent_Returns404()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await Client.DeleteAsync(
            $"{BaseUrl}/99999", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssignRole_DuplicateRoleAssignment_ReturnsCreatedIdempotent()
    {
        // Arrange
        await ResetDatabaseAsync();
        var request = new UserRoleRequest
        {
            EdjeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Role = "EDJEr",
            Reason = "First assignment"
        };
        await Client.PostAsJsonAsync(BaseUrl, request, TestContext.Current.CancellationToken);

        // Act - assign same role again
        var request2 = new UserRoleRequest
        {
            EdjeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Role = "EDJEr",
            Reason = "Duplicate assignment"
        };
        var response = await Client.PostAsJsonAsync(
            BaseUrl, request2, TestContext.Current.CancellationToken);

        // Assert - should not error (idempotent)
        ((int)response.StatusCode).ShouldBeOneOf(200, 201);
    }

    [Fact]
    public async Task AssignRole_ConcurrentIdenticalRequests_AllSucceedAndInsertExactlyOneRow()
    {
        // Regression: the sequential duplicate case above passes because the
        // service's FindAsync sees the committed row. Concurrent callers all
        // miss that check, all INSERT, and every loser hits the UNIQUE index
        // ix_user_roles_edje_id_role -> SQLSTATE 23505 -> DbUpdateException ->
        // unhandled -> HTTP 500.
        //
        // Observed live: two Playwright workers ran the same `beforeAll` (which
        // calls this endpoint for the impersonation fixture) at the same time;
        // one POST returned 201 and the other 500, failing whichever test that
        // worker happened to be about to run.
        //
        // "Idempotent" has to hold under concurrency, not just in sequence.

        // Arrange
        await ResetDatabaseAsync();
        var request = new UserRoleRequest
        {
            EdjeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Role = "SuperAdmin",
            Reason = "Concurrent assignment race"
        };

        // Act - fire the identical assignment from many callers at once so the
        // check-then-act window is genuinely straddled.
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ =>
                Client.PostAsJsonAsync(BaseUrl, request, TestContext.Current.CancellationToken)));

        // Assert - every caller succeeds...
        foreach (var response in responses)
        {
            ((int)response.StatusCode).ShouldBeOneOf(200, 201);
        }

        // ...and the unique index still holds: exactly one row.
        var rolesResponse = await Client.GetAsync(
            $"{BaseUrl}/by-employee/{request.EdjeId}", TestContext.Current.CancellationToken);
        rolesResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roles = await rolesResponse.Content.ReadFromJsonAsync<List<string>>(
            TestContext.Current.CancellationToken);
        roles.ShouldNotBeNull();
        roles.Count(r => r == "SuperAdmin").ShouldBe(1);
    }
}
