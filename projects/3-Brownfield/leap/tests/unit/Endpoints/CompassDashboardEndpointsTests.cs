using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// The Sales Dashboard routes at the HTTP boundary — feature 007, AC-36/AC-37.
/// </summary>
/// <remarks>
/// <para>
/// These routes had integration coverage and no endpoint-level coverage.
/// <c>CompassReportingPolicyTests</c> proves the policy resolves correctly and says in as many words
/// that "the endpoint-level assertions (every route, every role) come once the endpoints exist"; the
/// endpoints now exist. <c>tests/integration/Endpoints/CompassDashboardEndpointsTests</c> proves the
/// data is right against real PostgreSQL. What neither covers is the wiring in between — the route
/// template, the policy actually being attached to these two routes, and the status contract — which
/// is what a <see cref="TestWebApplicationFactory"/> test is for and what the unit suite measures.
/// </para>
/// <para>
/// An empty database is the right fixture here, not a limitation. Every assertion below is
/// about a status code and a route, and seeding rows would make the tests depend on read-model
/// behaviour that the integration suite already owns. The one payload assertion is that a breakdown
/// deserialises as a list at all.
/// </para>
/// </remarks>
public class CompassDashboardEndpointsTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private const string DashboardRoute = "/api/compass/dashboard";

    private static string BreakdownRoute(string category) =>
        $"/api/compass/dashboard/breakdown/{category}";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The four documented route segments, exactly as the parser spells them.</summary>
    public static TheoryData<string> Categories =>
        ["active-sows", "expiring-sows", "confirmed-rollouts", "beach"];

    public static TheoryData<string> PermittedRoles =>
        [RolePolicy.CompassSalesRole, RolePolicy.CompassOpsRole, RolePolicy.CompassSuperAdminRole];

    // ---------------------------------------------------------------- the dashboard itself

    [Theory]
    [MemberData(nameof(PermittedRoles))]
    public async Task GetDashboard_AdmitsEachOfTheThreeReportingRoles(string role)
    {
        // Act
        var response = await factory.AsRoles(role).GetAsync(DashboardRoute, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDashboard_RefusesACompassAdmin_AtTheServer()
    {
        // FR-019, AC-44. The name reads as administrative but the role is READ-ONLY in Compass, and
        // the dashboard is not among the things it administers. Asserted as an exact status: a loose
        // "not 200" would also pass if the route stopped existing.
        var response = await factory
            .AsRoles(RolePolicy.CompassAdminRole)
            .GetAsync(DashboardRoute, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetDashboard_RefusesAnEdjerHoldingNoCompassRole()
    {
        var response = await factory.AsBaselineEdjer().GetAsync(DashboardRoute, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- the four breakdowns

    [Theory]
    [MemberData(nameof(Categories))]
    public async Task GetBreakdown_ServesEachOfTheFourCategories(string category)
    {
        // One handler serves four routes, so a parser that silently dropped a case would show up
        // here as a 404 on that category alone rather than as a broken route.
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync(BreakdownRoute(category), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rows = await response.Content.ReadFromJsonAsync<List<DashboardBreakdownRowDto>>(Token);
        rows.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetBreakdown_ForAnUnrecognisedCategory_Is404_NotASilentFallback()
    {
        // The contract is explicit that this is 404 and never a default category: showing one
        // category's rows under another's heading is worse than an error, because it looks right.
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync(BreakdownRoute("not-a-category"), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Active-Assignments")]
    [InlineData("ACTIVE-ASSIGNMENTS")]
    [InlineData("activeassignments")]
    public async Task GetBreakdown_MatchesTheSegmentExactly_AndIsCaseSensitive(string category)
    {
        // The parser documents itself as case-sensitive. Pinning that is worth more than it looks:
        // relaxing it later would be a one-word change nobody would think to call a contract change,
        // and the four segments are what a client is told to send.
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync(BreakdownRoute(category), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetBreakdown_RefusesACompassAdmin_OnTheDrillDownToo()
    {
        // The tile and its drill-down are two routes; a policy attached to only one of them would
        // leak the detail while hiding the count.
        var response = await factory
            .AsRoles(RolePolicy.CompassAdminRole)
            .GetAsync(BreakdownRoute("active-sows"), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
