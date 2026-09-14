using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// The developer-tools surface: who may reach it, and where it exists at all.
/// </summary>
/// <remarks>
/// <para>
/// Three independent gates, and the Konami code is not one of them. The environment must opt
/// in (asserted here by the OFF host answering 404), the caller must hold "Compass Super Admin"
/// (asserted role by role), and the clear must be a POST. The launcher's hidden button is
/// discoverability, not authorization — anyone can press the code, and anyone can skip it and call
/// these routes directly, which is exactly what these tests do.
/// </para>
/// <para>
/// The role that matters most is Compass Admin. <c>RolePolicy.CompassAdmin</c> resolves to
/// "Compass Admin" OR "Compass Super Admin", so gating this route with that policy by mistake would
/// hand a READ-ONLY role (AC-44) the ability to empty every Compass table — and would pass every
/// other test here. The second is the timesheet root: Compass inherits nothing, not even root, so a
/// timesheet SuperAdmin must be refused.
/// </para>
/// </remarks>
public class CompassDeveloperToolsEndpointsTests
{
    private const string Availability = "/api/compass/developer-tools/availability";
    private const string Clear = "/api/compass/developer-tools/clear-compass-data";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- the environment gate

    [Fact]
    public async Task Availability_WhenTheGateIsOff_IsNotFound_EvenForTheCompassRoot()
    {
        // Arrange — the default unit host runs with DeveloperTools:Enabled absent/false.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync(Availability, Token);

        // Assert — 404, not 403. The routes are never MAPPED when the gate refuses, so the surface is
        // indistinguishable from one that was never built. A 403 here would mean the routes exist in
        // production and are merely guarded, which is a weaker thing than what was built.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Clear_WhenTheGateIsOff_IsNotFound_EvenForTheCompassRoot()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsync(Clear, content: null, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- authorization, gate ON

    [Fact]
    public async Task Availability_AsCompassSuperAdmin_IsOk()
    {
        // Arrange
        await using var factory = new DeveloperToolsTestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync(Availability, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<DeveloperToolsAvailabilityResponse>(Token);
        body.ShouldNotBeNull();
        body.Available.ShouldBeTrue();
        body.Tools.ShouldContain("clear-compass-data");
    }

    [Fact]
    public async Task Availability_AsCompassAdmin_IsForbidden()
    {
        // Arrange — the load-bearing refusal. "Compass Admin" is READ-ONLY (AC-44) however
        // administrative it sounds, and the launcher renders a DISABLED button off the back of this 403.
        await using var factory = new DeveloperToolsTestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.GetAsync(Availability, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Availability_AsTimesheetRootOnly_IsForbidden()
    {
        // Arrange — Compass inherits nothing, not even root (Principle IV). A timesheet SuperAdmin is
        // not a Compass Super Admin, and this feature does not become the exception to that.
        await using var factory = new DeveloperToolsTestWebApplicationFactory();
        var client = factory.AsTimesheetRootOnly();

        // Act
        var response = await client.GetAsync(Availability, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Availability_AsBaselineEdjer_IsForbidden()
    {
        // Arrange
        await using var factory = new DeveloperToolsTestWebApplicationFactory();
        var client = factory.AsBaselineEdjer();

        // Act
        var response = await client.GetAsync(Availability, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Availability_Anonymous_IsUnauthorized()
    {
        // Arrange
        await using var factory = new DeveloperToolsTestWebApplicationFactory();
        var client = factory.AsAnonymous();

        // Act
        var response = await client.GetAsync(Availability, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Compass Admin")]
    [InlineData("Compass Ops")]
    [InlineData("Compass Sales")]
    [InlineData("SuperAdmin")]
    [InlineData("Admin")]
    public async Task Clear_AsAnyRoleOtherThanTheCompassRoot_IsForbidden(string role)
    {
        // Arrange — every role that could plausibly be mistaken for "admin enough", refused
        // individually. "Compass Ops" is in the list because it holds Compass's other WRITE surface
        // (feature 006) and is the most natural wrong answer.
        await using var factory = new DeveloperToolsTestWebApplicationFactory();
        var client = factory.AsRoles(role);

        // Act
        var response = await client.PostAsync(Clear, content: null, Token);

        // Assert — 403 before the handler runs, so nothing was cleared.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Clear_AsCompassSuperAdmin_ReachesTheHandlerAndReturnsTheCounts()
    {
        // Arrange
        await using var factory = new DeveloperToolsTestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsync(Clear, content: null, Token);

        // Assert — this host stubs the clear itself (the InMemory provider has no TRUNCATE), so what is
        // proven here is that an authorised POST reaches the handler and that the response serialises
        // the whole DTO. That it empties real tables is asserted against PostgreSQL in
        // tests/integration.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CompassDataClearedResponse>(Token);
        body.ShouldBe(DeveloperToolsTestWebApplicationFactory.StubResult);
    }

    [Fact]
    public async Task Clear_ViaGet_IsMethodNotAllowed()
    {
        // Arrange — a destructive action must not be reachable by anything that follows a link: a
        // prefetch, an <img src>, or a browser restoring a tab. POST-only is what makes that true, and
        // nothing else in this suite would notice if the route were mapped with MapGet.
        await using var factory = new DeveloperToolsTestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync(Clear, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }
}
