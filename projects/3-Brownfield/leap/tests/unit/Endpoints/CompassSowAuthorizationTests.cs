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
/// US3/#66, FR-006/FR-007 — server-side authorization for the SOW write surface, kept in its own file
/// so a denial failure is not mistaken for behaviour (matching <c>CompassAssignmentAuthorizationTests</c>).
/// </summary>
/// <remarks>
/// Per contracts/sow-write-surface.md §1, every verb (<c>GET</c>/<c>POST</c>/<c>PUT</c>) is declared once
/// on the SAME <c>CompassWriteRouteGroup</c> as the assignment surface, under <see cref="RolePolicy.CompassOps"/>
/// — Compass Ops OR the Compass root, NEVER Compass Admin. Unlike the assignment surface's <c>GetById</c>,
/// there is no widened read exception here (contract §1 states this plainly): the whole SOW group stays
/// Ops-or-root.
/// </remarks>
public class CompassSowAuthorizationTests
{
    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string SowsUrl(int assignmentId) => $"/api/compass/assignments/{assignmentId}/sows";

    private static async Task<int> SeedAssignmentAsync(TestWebApplicationFactory factory)
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

    private static CreateSowRequest AnyCreateRequest() =>
        new(SowType.InitialContract, RateIncrease: false, new DateOnly(2024, 4, 1), new DateOnly(2024, 12, 31), null);

    [Fact]
    public async Task Post_AsCompassOps_IsPermitted()
    {
        // Arrange — the positive control (FR-001, FR-014).
        await using var factory = new TestWebApplicationFactory();
        var assignmentId = await SeedAssignmentAsync(factory);
        var client = factory.AsCompassOps();

        // Act
        var response = await client.PostAsJsonAsync(SowsUrl(assignmentId), AnyCreateRequest(), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Post_AsCompassSuperAdmin_IsPermitted()
    {
        // Arrange — FR-007, AC-45: no function is reserved against the Compass root.
        await using var factory = new TestWebApplicationFactory();
        var assignmentId = await SeedAssignmentAsync(factory);
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(SowsUrl(assignmentId), AnyCreateRequest(), Token);

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
        var response = await client.PostAsJsonAsync(SowsUrl(999_999), AnyCreateRequest(), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_AsCompassAdmin_IsRefused()
    {
        // Arrange — contract §1: no widened read exception for SOWs, unlike the assignment surface.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.GetAsync(SowsUrl(999_999), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_AsCompassOps_IsPermitted()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var assignmentId = await SeedAssignmentAsync(factory);
        var client = factory.AsCompassOps();

        // Act
        var response = await client.GetAsync(SowsUrl(assignmentId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_AsBaselineEdjer_IsRefused()
    {
        // Arrange — the floor every authenticated EDJEr holds and no more; below the Elevated tier.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsBaselineEdjer();

        // Act
        var response = await client.GetAsync(SowsUrl(999_999), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_Unauthenticated_IsChallenged()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsAnonymous();

        // Act
        var response = await client.GetAsync(SowsUrl(999_999), Token);

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
        var response = await client.PostAsJsonAsync(SowsUrl(999_999), AnyCreateRequest(), Token);

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
        var response = await client.PostAsJsonAsync(SowsUrl(999_999), AnyCreateRequest(), Token);

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
        var response = await client.PostAsJsonAsync(SowsUrl(999_999), AnyCreateRequest(), Token);

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
            $"{SowsUrl(999_999)}/1",
            new UpdateSowRequest(SowType.InitialContract, RateIncrease: false, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), null),
            Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ Delete — issue #593

    [Fact]
    public async Task Delete_AsCompassSuperAdmin_IsPermitted()
    {
        // Arrange — the owner's explicit answer on #593: "Only Compass-SuperAdmin should be able to
        // perform this function." A real row, so the positive control proves something either way.
        await using var factory = new TestWebApplicationFactory();
        var assignmentId = await SeedAssignmentAsync(factory);
        var ops = factory.AsCompassOps();
        var created = await (await ops.PostAsJsonAsync(SowsUrl(assignmentId), AnyCreateRequest(), Token))
            .Content.ReadFromJsonAsync<SowRowDto>(Token);

        // Act
        var response = await factory.AsCompassSuperAdmin()
            .DeleteAsync($"{SowsUrl(assignmentId)}/{created!.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_AsCompassOps_IsRefused()
    {
        // Arrange — Ops may create/edit a SOW, but the true delete is Super-Admin-only.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassOps();

        // Act
        var response = await client.DeleteAsync($"{SowsUrl(999_999)}/1", Token);

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
        var response = await client.DeleteAsync($"{SowsUrl(999_999)}/1", Token);

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
        var response = await client.DeleteAsync($"{SowsUrl(999_999)}/1", Token);

        // Assert — 401 for "who are you", distinct from 403 for "not you".
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
