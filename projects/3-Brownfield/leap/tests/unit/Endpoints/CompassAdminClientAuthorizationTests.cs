using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Server-side authorization for client configuration — every route, every insufficient role.
/// </summary>
/// <remarks>
/// <para>
/// SC-006 is not satisfied by hiding a control. FR-028 and AC-44 require read-only to be enforced
/// server-side, and Principle IV says "read-only means read-only". Every route below is called DIRECTLY,
/// with the interface bypassed entirely.
/// </para>
/// <para>
/// The role that matters most is Compass Admin. <c>RolePolicy.CompassAdmin</c> resolves to
/// "Compass Admin" OR "Compass Super Admin", so a route gated with that policy by mistake would admit
/// exactly the role FR-028 excludes — and would pass every other test in this feature.
/// </para>
/// <para>
/// The category routes are tested separately from the client routes on purpose. They are the ones
/// most easily left off a group by accident, because they hang off a nested path — and a nested route
/// mapped outside the gated group is authorised by nothing at all.
/// </para>
/// </remarks>
public class CompassAdminClientAuthorizationTests
{
    private const string Clients = "/api/compass/v1/admin/clients";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompassClientRequest Smuggled =>
        new(
            "Smuggled Client",
            MsaSignedDate: null,
            NdaSignedDate: null,
            IsInternal: false,
            InvoiceFrequencyTypeId: null
        );

    private static CreateBillableTimeCategoryRequest SmuggledCategory => new("Smuggled Category");

    [Fact]
    public async Task Post_AsCompassAdmin_IsRefused()
    {
        // Arrange — the load-bearing case (FR-028, AC-44, SC-006).
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.PostAsJsonAsync(Clients, Smuggled, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_AsCompassAdmin_IsRefused_BeforeTheHandlerRuns()
    {
        // Arrange — the id is deliberately one that does not exist. A 404 here would mean the policy ran
        // AFTER the handler looked the record up, which leaks whether a given client exists to a role
        // that must not be writing at all.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.PutAsJsonAsync($"{Clients}/999999", Smuggled, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.StatusCode.ShouldNotBe(
            HttpStatusCode.NotFound,
            "authorization must be decided before the handler reads anything"
        );
    }

    [Fact]
    public async Task PostCategory_AsCompassAdmin_IsRefused()
    {
        // Arrange — FR-028 covers "every client AND category write". A nested route is the easiest one
        // to map outside the gated group by accident.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            $"{Clients}/1/billable-time-categories",
            SmuggledCategory,
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutCategory_AsCompassAdmin_IsRefused_BeforeTheHandlerRuns()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Clients}/999999/billable-time-categories/999999",
            new UpdateBillableTimeCategoryRequest("Smuggled", IsActive: false),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.StatusCode.ShouldNotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_AsTheTimesheetRoot_IsRefused()
    {
        // Arrange — Compass inherits nothing, not even root (Principle IV). A timesheet SuperAdmin is
        // not a Compass administrator.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsTimesheetRootOnly();

        // Act
        var response = await client.PostAsJsonAsync(Clients, Smuggled, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Compass Ops")]
    [InlineData("Compass Sales")]
    public async Task Post_AsAnotherCompassRole_IsRefused(string role)
    {
        // Arrange — spec A-1: only the Compass Super Admin writes anything in this stream.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsRoles(role);

        // Act
        var response = await client.PostAsJsonAsync(Clients, Smuggled, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostCategory_AsAnotherCompassRole_IsRefused()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsRoles("Compass Ops");

        // Act
        var response = await client.PostAsJsonAsync(
            $"{Clients}/1/billable-time-categories",
            SmuggledCategory,
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_WithNoPrivilegesAtAll_IsRefused()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.NoPrivilegesHeader, "true");

        // Act
        var response = await client.PostAsJsonAsync(Clients, Smuggled, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_Unauthenticated_IsChallenged()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");

        // Act
        var response = await client.PostAsJsonAsync(Clients, Smuggled, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The positive control.
    /// </summary>
    /// <remarks>
    /// Without it, every refusal above could pass because the routes reject everyone, or because they do
    /// not exist and the 403 comes from somewhere else entirely. A gate that matches nothing PASSES
    /// rather than fails, which this repository has been bitten by eight times across Phases 48-49.
    /// </remarks>
    [Fact]
    public async Task Post_AsTheCompassRoot_IsPermitted()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(Clients, Smuggled, Token);

        // Assert — decidedly NOT a refusal: the policy admitted the caller and the handler answered.
        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostCategory_AsTheCompassRoot_IsPermitted()
    {
        // Arrange — the nested route's positive control, for the same reason.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            $"{Clients}/999999/billable-time-categories",
            SmuggledCategory,
            Token
        );

        // Assert — a 404 is the right answer for a client that does not exist, and proves the policy
        // admitted the caller and the handler ran.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
