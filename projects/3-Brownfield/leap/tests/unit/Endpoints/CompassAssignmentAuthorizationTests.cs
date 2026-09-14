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
/// US1/#62, FR-006/FR-007 — server-side authorization for the assignment write surface, kept in its own
/// file so a denial failure is not mistaken for behaviour (contract §6, matching
/// <c>CompassAdminLookupAuthorizationTests</c>).
/// </summary>
/// <remarks>
/// Writes (<c>POST</c>/<c>PUT</c>) and <c>GetAll</c> are declared once on <c>CompassWriteRouteGroup</c>
/// under the <see cref="RolePolicy.CompassOps"/> policy (Ops OR the Compass root); nothing here is
/// gated in a handler. <see cref="RolePolicy.CompassAdmin"/> must be refused for those despite the
/// name — Compass Admin is READ-ONLY (AC-44), and that policy is satisfied by "Compass Admin" OR
/// "Compass Super Admin", so a route mistakenly gated with it would admit the very role this feature
/// must exclude for writes. <c>GetById</c> is the one exception, gated by the broader
/// <see cref="RolePolicy.CompassElevated"/> instead (AC-16/FR-025) — see
/// <c>GetById_As*_IsPermitted</c> below, and the remark on <c>CompassAssignmentEndpoints</c> for why.
/// </remarks>
public class CompassAssignmentAuthorizationTests
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
            FirstName = "Grace",
            LastName = "Hopper",
            Email = $"grace-{Guid.NewGuid():N}@example.test",
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

    private static CreateAssignmentRequest AnyCreateRequest(int employeeId, int clientId) =>
        new(employeeId, clientId, new DateOnly(2026, 1, 1), null, null);

    [Fact]
    public async Task Post_AsCompassOps_IsPermitted()
    {
        // Arrange — the positive control (FR-001).
        await using var factory = new TestWebApplicationFactory();
        var (employeeId, clientId) = await SeedAsync(factory);
        var client = factory.AsCompassOps();

        // Act
        var response = await client.PostAsJsonAsync(
            AssignmentsUrl, AnyCreateRequest(employeeId, clientId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Post_AsCompassSuperAdmin_IsPermitted()
    {
        // Arrange — FR-007, AC-45: no function is reserved against the Compass root.
        await using var factory = new TestWebApplicationFactory();
        var (employeeId, clientId) = await SeedAsync(factory);
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            AssignmentsUrl, AnyCreateRequest(employeeId, clientId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Post_AsCompassAdmin_IsRefused()
    {
        // Arrange — AC-44: Compass Admin is read-only, despite the name.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            AssignmentsUrl, AnyCreateRequest(999_999, 999_999), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_AsCompassAdmin_IsRefused()
    {
        // Arrange — GetAll stays on the Ops-or-root write group (research R-7); only GetById widens.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.GetAsync(AssignmentsUrl, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ GetById — AC-16/FR-025

    [Fact]
    public async Task GetById_AsCompassOps_IsPermitted()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var (employeeId, clientId) = await SeedAsync(factory);
        var ops = factory.AsCompassOps();
        var created = await (await ops.PostAsJsonAsync(
            AssignmentsUrl, AnyCreateRequest(employeeId, clientId), Token))
            .Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await ops.GetAsync($"{AssignmentsUrl}/{created!.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_AsCompassAdmin_IsPermitted()
    {
        // Arrange — AC-16/FR-025: the read-only elevated role can VIEW, even though it cannot write.
        await using var factory = new TestWebApplicationFactory();
        var (employeeId, clientId) = await SeedAsync(factory);
        var created = await (await factory.AsCompassOps().PostAsJsonAsync(
            AssignmentsUrl, AnyCreateRequest(employeeId, clientId), Token))
            .Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await factory.AsCompassAdmin().GetAsync($"{AssignmentsUrl}/{created!.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_AsCompassSales_IsPermitted()
    {
        // Arrange — AC-16/FR-025 names Sales explicitly, alongside Admin and Ops.
        await using var factory = new TestWebApplicationFactory();
        var (employeeId, clientId) = await SeedAsync(factory);
        var created = await (await factory.AsCompassOps().PostAsJsonAsync(
            AssignmentsUrl, AnyCreateRequest(employeeId, clientId), Token))
            .Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await factory.AsCompassSales().GetAsync($"{AssignmentsUrl}/{created!.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_AsCompassSuperAdmin_IsPermitted()
    {
        // Arrange — AC-45: no assignment function is reserved against the Compass root.
        await using var factory = new TestWebApplicationFactory();
        var (employeeId, clientId) = await SeedAsync(factory);
        var created = await (await factory.AsCompassOps().PostAsJsonAsync(
            AssignmentsUrl, AnyCreateRequest(employeeId, clientId), Token))
            .Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await factory.AsCompassSuperAdmin()
            .GetAsync($"{AssignmentsUrl}/{created!.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_AsBaselineEdjer_IsRefused()
    {
        // Arrange — the floor every authenticated EDJEr holds and no more; below the Elevated tier.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsBaselineEdjer();

        // Act
        var response = await client.GetAsync($"{AssignmentsUrl}/999999", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetById_Unauthenticated_IsChallenged()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsAnonymous();

        // Act
        var response = await client.GetAsync($"{AssignmentsUrl}/999999", Token);

        // Assert — 401 for "who are you", distinct from 403 for "not you".
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_AsABaseEdjEr_IsRefused()
    {
        // Arrange — the floor every authenticated EDJEr holds and no more.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsBaselineEdjer();

        // Act
        var response = await client.PostAsJsonAsync(
            AssignmentsUrl, AnyCreateRequest(999_999, 999_999), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_AsTheTimesheetRoot_IsRefused()
    {
        // Arrange — Compass inherits NOTHING, not even root (Principle IV). A forgotten
        // X-Test-Privileges header would stamp all nine timesheet roles and read as a real denial for
        // the wrong reason, so this is asserted explicitly rather than relying on the default client.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsTimesheetRootOnly();

        // Act
        var response = await client.PostAsJsonAsync(
            AssignmentsUrl, AnyCreateRequest(999_999, 999_999), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_Unauthenticated_IsChallenged()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsAnonymous();

        // Act
        var response = await client.PostAsJsonAsync(
            AssignmentsUrl, AnyCreateRequest(999_999, 999_999), Token);

        // Assert — 401 for "who are you", distinct from 403 for "not you".
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Put_AsCompassAdmin_IsRefused()
    {
        // Arrange — 403 before the handler runs, so a non-existent id must NOT surface as 404 here.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.PutAsJsonAsync(
            $"{AssignmentsUrl}/1",
            new UpdateAssignmentRequest(new DateOnly(2026, 1, 1), null, null),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ Delete — issue #593

    [Fact]
    public async Task Delete_AsCompassSuperAdmin_IsPermitted()
    {
        // Arrange — the owner's explicit answer on #593: "Only Compass-SuperAdmin should be able to
        // perform this function." 403 would run before the handler, so a non-existent id proves
        // nothing either way here — the positive control needs a real row.
        await using var factory = new TestWebApplicationFactory();
        var (employeeId, clientId) = await SeedAsync(factory);
        var ops = factory.AsCompassOps();
        var created = await (await ops.PostAsJsonAsync(
            AssignmentsUrl, AnyCreateRequest(employeeId, clientId), Token))
            .Content.ReadFromJsonAsync<AssignmentRowDto>(Token);

        // Act
        var response = await factory.AsCompassSuperAdmin()
            .DeleteAsync($"{AssignmentsUrl}/{created!.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_AsCompassOps_IsRefused()
    {
        // Arrange — Ops may create/edit/end an assignment, but the true delete is Super-Admin-only.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassOps();

        // Act
        var response = await client.DeleteAsync($"{AssignmentsUrl}/999999", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_AsCompassAdmin_IsRefused()
    {
        // Arrange — AC-44: Compass Admin is read-only, despite the name.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.DeleteAsync($"{AssignmentsUrl}/999999", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_AsTheTimesheetRoot_IsRefused()
    {
        // Arrange — Compass inherits NOTHING, not even root (Principle IV).
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsTimesheetRootOnly();

        // Act
        var response = await client.DeleteAsync($"{AssignmentsUrl}/999999", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_Unauthenticated_IsChallenged()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsAnonymous();

        // Act
        var response = await client.DeleteAsync($"{AssignmentsUrl}/999999", Token);

        // Assert — 401 for "who are you", distinct from 403 for "not you".
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
