using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// The three Compass report routes at the HTTP boundary — feature 007, AC-38 through AC-40.
/// </summary>
/// <remarks>
/// <para>
/// Companion to <c>CompassDashboardEndpointsTests</c>, for the same reason: the data these routes
/// return is asserted against real PostgreSQL in the integration suite, while the route templates,
/// the policy attachment and the status contract had no endpoint-level coverage at all.
/// </para>
/// <para>
/// The Assignment Start range guard is the substance here. Its handler documents two traps
/// that a status-only test would miss, and both are pinned below: the comparison is <c>&gt;</c> and
/// not <c>&gt;=</c>, so a single-day lookup is a legitimate question rather than a rejected one; and
/// a missing or unparseable date is a BINDING failure that answers 400 only because
/// <c>RouteHandlerOptions.ThrowOnBadRequest</c> is pinned false application-wide (PR #259). On the
/// framework default that same request is an unhandled exception and a 500 — against a local
/// development API, while a suite that never sends a malformed date stays green.
/// </para>
/// </remarks>
public class CompassReportEndpointsTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private const string AvailabilityRoute = "/api/compass/reports/availability";
    private const string DurationRoute = "/api/compass/reports/assignment-duration";
    private const string AvailabilityExportRoute = "/api/compass/reports/availability/export";
    private const string DurationExportRoute = "/api/compass/reports/assignment-duration/export";
    private const string StartExportRoute = "/api/compass/reports/assignment-start/export";
    private const string SowExtensionExportRoute = "/api/compass/reports/sow-extension/export";

    private static string StartRoute(string from, string to) =>
        $"/api/compass/reports/assignment-start?from={from}&to={to}";

    private static string SowExtensionRoute(string from, string to) =>
        $"/api/compass/reports/sow-extension?from={from}&to={to}";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> PermittedRoles =>
        [RolePolicy.CompassSalesRole, RolePolicy.CompassOpsRole, RolePolicy.CompassSuperAdminRole];

    public static TheoryData<string> AllFourRoutes =>
    [
        AvailabilityRoute,
        DurationRoute,
        "/api/compass/reports/assignment-start?from=2026-01-01&to=2026-12-31",
        "/api/compass/reports/sow-extension?from=2026-01-01&to=2026-12-31",
    ];

    public static TheoryData<string> AllFiveExportRoutes =>
    [
        AvailabilityExportRoute,
        $"{AvailabilityExportRoute}?section=currently-available",
        DurationExportRoute,
        $"{StartExportRoute}?from=2026-01-01&to=2026-12-31",
        $"{SowExtensionExportRoute}?from=2026-01-01&to=2026-12-31",
    ];

    // ---------------------------------------------------------------- who may read them

    [Theory]
    [MemberData(nameof(PermittedRoles))]
    public async Task EveryReport_AdmitsEachOfTheThreeReportingRoles(string role)
    {
        var client = factory.AsRoles(role);

        foreach (var route in new[]
        {
            AvailabilityRoute,
            DurationRoute,
            StartRoute("2026-01-01", "2026-12-31"),
            SowExtensionRoute("2026-01-01", "2026-12-31"),
        })
        {
            var response = await client.GetAsync(route, Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{role} should be able to read {route}");
        }
    }

    [Theory]
    [MemberData(nameof(AllFourRoutes))]
    public async Task EveryReport_RefusesACompassAdmin_AtTheServer(string route)
    {
        // FR-019 again, and per ROUTE rather than once: a policy is attached to a group, but a route
        // added later can carry its own and this is where that would show.
        var response = await factory.AsRoles(RolePolicy.CompassAdminRole).GetAsync(route, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- the two simple reports

    [Fact]
    public async Task GetAvailability_ReturnsTheReportShape()
    {
        var response = await factory.AsRoles(RolePolicy.CompassSalesRole).GetAsync(AvailabilityRoute, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AvailabilityReportDto>(Token)).ShouldNotBeNull();
    }

    [Fact]
    public async Task GetAssignmentDuration_ReturnsAList()
    {
        var response = await factory.AsRoles(RolePolicy.CompassSalesRole).GetAsync(DurationRoute, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<List<AssignmentDurationRowDto>>(Token)).ShouldNotBeNull();
    }

    // ---------------------------------------------------------------- the Assignment Start range

    [Fact]
    public async Task GetAssignmentStart_WithTheRangeInverted_Is400WithAMessage()
    {
        // US4 scenario 2: the user has made an error and must be told. An empty list would be
        // indistinguishable from "nothing started in that range", which is a different answer and a
        // wrong one; a silent swap answers a question nobody asked.
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync(StartRoute("2026-12-31", "2026-01-01"), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadAsStringAsync(Token);
        problem.ShouldContain("from", Case.Insensitive, "the failing parameter must be named");
    }

    [Fact]
    public async Task GetAssignmentStart_ForASingleDay_IsAcceptedRatherThanRejected()
    {
        // The guard is `>` and not `>=`. "Which assignments start today" is a legitimate question,
        // and rejecting it is the most likely off-by-one in this validation — which is exactly why it
        // is worth a test rather than a comment.
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync(StartRoute("2026-06-01", "2026-06-01"), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("not-a-date", "2026-12-31")]
    [InlineData("2026-01-01", "not-a-date")]
    public async Task GetAssignmentStart_WithAnUnparseableDate_Is400_NotAnUnhandled500(
        string from,
        string to
    )
    {
        // Both parameters are non-nullable DateOnly, so this never reaches the handler — it is a
        // binding failure. It answers 400 only because RouteHandlerOptions.ThrowOnBadRequest is
        // pinned false application-wide; on the framework default under Development the same request
        // is an unhandled BadHttpRequestException and a 500. This test is what notices if that pin
        // is ever removed.
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync(StartRoute(from, to), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAssignmentStart_WithAMissingParameter_Is400()
    {
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync("/api/compass/reports/assignment-start?from=2026-01-01", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- the SOW Extension range (issue #534)

    [Fact]
    public async Task GetSowExtension_WithTheRangeInverted_Is400WithAMessage()
    {
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync(SowExtensionRoute("2026-12-31", "2026-01-01"), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadAsStringAsync(Token);
        problem.ShouldContain("from", Case.Insensitive, "the failing parameter must be named");
    }

    [Fact]
    public async Task GetSowExtension_ForASingleDay_IsAcceptedRatherThanRejected()
    {
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync(SowExtensionRoute("2026-06-01", "2026-06-01"), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("not-a-date", "2026-12-31")]
    [InlineData("2026-01-01", "not-a-date")]
    public async Task GetSowExtension_WithAnUnparseableDate_Is400_NotAnUnhandled500(string from, string to)
    {
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync(SowExtensionRoute(from, to), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSowExtension_WithAMissingParameter_Is400()
    {
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync("/api/compass/reports/sow-extension?from=2026-01-01", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- CSV export (issue #337)
    //
    // Same division of labour as above: the CONTENT is asserted against real PostgreSQL in
    // CompassReportExportEndpointsTests, and the per-cell rendering in
    // CompassReportExportServiceTests. What only this level can pin is the route template, the
    // inherited policy, and the status contract of the two rejection branches.

    [Theory]
    [MemberData(nameof(AllFiveExportRoutes))]
    public async Task EveryExportRoute_IsReachableByAReportingRole(string route)
    {
        var response = await factory.AsRoles(RolePolicy.CompassSalesRole).GetAsync(route, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, route);
    }

    [Theory]
    [MemberData(nameof(AllFiveExportRoutes))]
    public async Task EveryExportRoute_RefusesACompassAdmin(string route)
    {
        // The routes are mapped INSIDE the report group, so they inherit RolePolicy.CompassReporting
        // rather than declaring it — this is the test that notices if one is ever mapped outside.
        // An export hands over the whole population in one request, so the exact status is asserted:
        // "not 200" would pass under a stale DevBypass.
        var response = await factory.AsRoles(RolePolicy.CompassAdminRole).GetAsync(route, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, route);
    }

    [Fact]
    public async Task ExportAvailability_WithoutASection_SendsTheArchive()
    {
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync(AvailabilityExportRoute, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/zip");
    }

    [Theory]
    [InlineData("currently-available")]
    [InlineData("confirmed-rollouts")]
    [InlineData("unconfirmed-sows")]
    public async Task ExportAvailability_WithAKnownSection_SendsACsv(string section)
    {
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync($"{AvailabilityExportRoute}?section={section}", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/csv");
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("CurrentlyAvailable")]
    [InlineData("Confirmed-Rollouts")]
    public async Task ExportAvailability_WithAnUnknownSection_Is400_NotAnEmptyFile(string section)
    {
        // An empty CSV is indistinguishable from "that section has no rows", a different and wrong
        // answer — the same reasoning the range guard above applies. "CurrentlyAvailable" is the
        // enum MEMBER name, which is what minimal-API enum binding would have accepted; the slug
        // table deliberately does not, so one spelling names each section in both the URL and the
        // downloaded file.
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync($"{AvailabilityExportRoute}?section={section}", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExportAssignmentStart_RepeatsTheInvertedRangeGuard()
    {
        // Duplicated from the JSON route rather than shared, because the two handlers return
        // different types. An export answering 200 with an empty file where the screen's own route
        // answers 400 would be the more permissive door onto the same query.
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync($"{StartExportRoute}?from=2026-12-31&to=2026-01-01", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExportAssignmentStart_WithAnUnparseableDate_Is400_NotAnUnhandled500()
    {
        // The binding-failure trap the JSON route documents, on the export twin.
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync($"{StartExportRoute}?from=not-a-date&to=2026-12-31", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExportSowExtension_RepeatsTheInvertedRangeGuard()
    {
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync($"{SowExtensionExportRoute}?from=2026-12-31&to=2026-01-01", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExportSowExtension_WithAnUnparseableDate_Is400_NotAnUnhandled500()
    {
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync($"{SowExtensionExportRoute}?from=not-a-date&to=2026-12-31", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EveryExport_SendsAnAttachmentFilename()
    {
        // Without a filename the browser renders the CSV as text in a tab instead of saving it.
        var response = await factory
            .AsRoles(RolePolicy.CompassSalesRole)
            .GetAsync(DurationExportRoute, Token);

        var disposition = response.Content.Headers.ContentDisposition;
        disposition.ShouldNotBeNull();
        disposition.DispositionType.ShouldBe("attachment");
    }
}
