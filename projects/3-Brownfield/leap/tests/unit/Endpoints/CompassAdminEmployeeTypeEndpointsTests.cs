using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Endpoint tests for employee-type administration over the real request pipeline.
/// </summary>
/// <remarks>
/// Every request acts as the Compass root. Refusal of lesser roles is
/// <see cref="CompassAdminLookupAuthorizationTests"/>'s subject, kept separate so a failure there
/// cannot be mistaken for a behaviour failure here.
/// </remarks>
public class CompassAdminEmployeeTypeEndpointsTests
{
    private const string Route = "/api/compass/v1/admin/employee-types";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task GetAll_ReturnsTheLookupCollection()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var types = await response.Content.ReadFromJsonAsync<List<EmployeeTypeDto>>(Token);
        types.ShouldNotBeNull();
    }

    [Fact]
    public async Task Post_WithANewName_CreatesItActiveAndReturnsItsLocation()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var name = UniqueName("Contract");

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            new CreateCompassLookupRequest(name),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();
        var created = await response.Content.ReadFromJsonAsync<EmployeeTypeDto>(Token);
        created.ShouldNotBeNull();
        created!.TypeName.ShouldBe(name);
        created.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Post_WithADuplicateName_Returns409()
    {
        // Arrange — FR-004.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var name = UniqueName("Contract");
        await client.PostAsJsonAsync(Route, new CreateCompassLookupRequest(name), Token);

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            new CreateCompassLookupRequest(name),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Post_WithABlankName_Returns400()
    {
        // Arrange — malformed, not a collision.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            new CreateCompassLookupRequest("   "),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_RenamesTheValue()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateAsync(client, UniqueName("Contract"));
        var renamed = UniqueName("Contractor");

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            new UpdateCompassLookupRequest(renamed, IsActive: true),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<EmployeeTypeDto>(Token);
        updated!.TypeName.ShouldBe(renamed);
    }

    [Fact]
    public async Task Put_TogglesTheValueInactive()
    {
        // Arrange — retiring is an edit; there is deliberately no separate deactivate route (AC-25).
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateAsync(client, UniqueName("Contract"));

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            new UpdateCompassLookupRequest(created.TypeName, IsActive: false),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<EmployeeTypeDto>(Token);
        updated!.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Put_ForAnUnknownId_Returns404()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/424242",
            new UpdateCompassLookupRequest("Contract", IsActive: true),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAll_WithActiveOnly_OmitsRetiredValues()
    {
        // Arrange — the query shape the EDJEr and client configuration forms consume (AC-25, AC-26).
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateAsync(client, UniqueName("Retired"));
        await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            new UpdateCompassLookupRequest(created.TypeName, IsActive: false),
            Token
        );

        // Act
        var response = await client.GetAsync($"{Route}?activeOnly=true", Token);

        // Assert
        var types = await response.Content.ReadFromJsonAsync<List<EmployeeTypeDto>>(Token);
        types.ShouldNotBeNull();
        types!.Select(type => type.TypeName).ShouldNotContain(created.TypeName);
        types.ShouldAllBe(type => type.IsActive);
    }

    [Fact]
    public async Task Delete_IsNotOffered()
    {
        // Arrange — lookups are deactivated, never removed: other records reference them by id
        // (Principle VIII). The absence of the verb is the requirement.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();
        var created = await CreateAsync(client, UniqueName("Contract"));

        // Act
        var response = await client.DeleteAsync($"{Route}/{created.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    private static async Task<EmployeeTypeDto> CreateAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            Route,
            new CreateCompassLookupRequest(name),
            Token
        );
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            "the test's own arrangement must succeed before it asserts anything"
        );
        return (await response.Content.ReadFromJsonAsync<EmployeeTypeDto>(Token))!;
    }
}
