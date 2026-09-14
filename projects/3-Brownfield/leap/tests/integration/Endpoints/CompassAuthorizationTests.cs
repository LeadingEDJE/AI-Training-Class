using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// US5/#55 — Compass authority enforced at the SERVER, with the interface bypassed.
/// </summary>
/// <remarks>
/// <para>
/// The claim under test is "hiding a control is not authorization". Every request here is
/// issued straight at the API through <c>CompassPrincipalFactory</c> (T005), so nothing the SPA does
/// or does not render is involved.
/// </para>
/// <para>
/// ⚠️ Compass has ONE endpoint today and it is a read. Every "refused every write" assertion in
/// this file therefore iterates a set that is currently EMPTY, and an empty iteration passes. That is
/// the exact trap US5 exists for, so the write tests below assert the discovered count against
/// <see cref="ExpectedCompassWriteSurfaceCount"/> rather than just looping: when Stream 2 adds the
/// first write, these tests FAIL and force whoever added it to wire it in here. The durable gate is
/// <c>CompassAuthorizationCoverageTests</c> in the unit project, which cannot be outrun this way.
/// </para>
/// <para>
/// Every denial is paired with a positive control. An empty role set is refused by every policy,
/// so "refused" and "the principal resolved to nothing" are the same green result — T005's own
/// mutation record found a false pass of exactly that shape, invisible against Compass alone. Each
/// denial test below therefore also proves the same client succeeds somewhere it should.
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassAuthorizationTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassAuthorizationTests(IntegrationTestFactory factory) : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string EmployeesUrl = "/api/compass/v1/employees";

    /// <summary>
    /// Route prefix owned by Compass, compared against a NORMALISED path.
    /// </summary>
    /// <remarks>
    /// ⚠️ Compare normalised, never raw. <c>RoutePattern.RawText</c> carries a leading slash
    /// (<c>/api/compass/v1/employees/{id:int}</c>). An earlier revision of this file matched against
    /// the bare <c>"api/compass"</c>, so the filter matched nothing, every discovery returned empty,
    /// and the write-surface tripwires below could never have fired — including after a real write
    /// landed, which is the one moment they exist for. Caught in review of PR #207 by the
    /// <c>github-actions</c> bot. Always go through <see cref="NormalisedPath"/>.
    /// </remarks>
    private const string CompassRoutePrefix = "/api/compass";

    /// <summary>A route's path with exactly one leading slash, whatever the raw text carried.</summary>
    private static string NormalisedPath(RouteEndpoint endpoint) =>
        "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');

    /// <summary>
    /// Matches a route-parameter token — <c>{id:int}</c>, <c>{key?}</c>, <c>{*rest}</c>.
    /// </summary>
    private static readonly Regex RouteParameterToken = new(@"\{[^}]+\}", RegexOptions.Compiled);

    /// <summary>
    /// A concrete, requestable path for a route, with every parameter token substituted.
    /// </summary>
    /// <remarks>
    /// ⚠️ Requesting <c>RawText</c> directly sends a literal <c>{id:int}</c>, which fails route
    /// matching and returns 404 before authorization ever runs — so a parameterised write would
    /// look "not refused" for the wrong reason and the denial assertion would report a routing bug as
    /// an authorization result. Also caught in PR #207 review. The substituted value is deliberately
    /// one that will not exist: a write must be refused on authority, before existence is consulted.
    /// </remarks>
    private static string RequestablePath(RouteEndpoint endpoint) =>
        RouteParameterToken.Replace(NormalisedPath(endpoint), match =>
            match.Value.Contains(":guid", StringComparison.OrdinalIgnoreCase)
                ? "11111111-1111-1111-1111-111111111111"
                : "999999");

    /// <summary>
    /// Compass write surfaces that exist today. Four, as of Compass's first writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When this stops matching, <see cref="CompassAdmin_IsRefusedEveryCompassWrite"/> and
    /// <see cref="BaseEdjErOnly_IsRefusedEveryCompassWrite"/> fail deliberately. Point them at the new
    /// write, then bump this. Do NOT bump it to silence the failure.
    /// </para>
    /// <para>
    /// 0 → 4 (feature 004 US1, issue #58). Lookup administration is Compass's first write
    /// surface: POST and PUT for employee types, and for invoice frequency types. Both tripwires fired
    /// on the merge exactly as intended.
    /// </para>
    /// <para>
    /// The count is all that changed, and that is the point. The two sweeps discover writes from
    /// the endpoint graph and issue each one for real, so raising this number does not silence
    /// anything — it lets the 403 assertions run against the four new routes for the first time. They
    /// pass because every route hangs off <c>CompassAdminRouteGroup</c>, which attaches
    /// <c>RolePolicy.CompassSuperAdmin</c> and never the weaker <c>CompassAdmin</c>. The same denial is
    /// asserted per route in <c>CompassAdminLookupAuthorizationTests</c> (unit) and
    /// <c>CompassAdminLookupEndpointsTests</c> (integration); this file is what makes it impossible to
    /// add a FIFTH write without noticing.
    /// </para>
    /// <para>
    /// 4 → 6 (feature 006 US1, issue #62). The assignment write surface added <c>POST /</c> and
    /// <c>PUT /{id}</c> under <c>/api/compass/assignments</c>, behind <c>RolePolicy.CompassOps</c> via
    /// <c>CompassWriteRouteGroup</c> — never <c>CompassAdmin</c>, which the read-only "Compass Admin"
    /// role also satisfies (AC-44). The per-route denial is asserted again in the unit project's
    /// <c>CompassAssignmentAuthorizationTests</c>.
    /// </para>
    /// <para>
    /// 4 → 6 (feature 004 US2, issue #59), independently. EDJEr configuration added POST and PUT
    /// on <c>/api/compass/v1/admin/edjers</c>. Per-route denial for these two is
    /// <c>CompassAdminEdjerAuthorizationTests</c>'.
    /// </para>
    /// <para>
    /// 6 → 10 (feature 004 US3, issue #60). Client configuration added POST and PUT on
    /// <c>/api/compass/v1/admin/clients</c>, plus POST and PUT on the NESTED
    /// <c>/{clientId}/billable-time-categories</c> routes. The nested pair is the case this file is
    /// most valuable for. They inherit their policy from the parent's route group, which is correct
    /// but invisible at the call site — a nested route mapped outside that group would be authorised by
    /// nothing at all, and every other test in the feature would still pass. The sweep issues them for
    /// real, so raising this number is what let those two 403 assertions run for the first time.
    /// Per-route denial for all four is <c>CompassAdminClientAuthorizationTests</c>'.
    /// </para>
    /// <para>
    /// 10 → 12 (merging 006 US1 into 004 US2/US3, 2026-08-14). Both prior increments assumed a
    /// starting count of 4 and were correct in isolation; merged together the true total is
    /// <c>4 + 2 (assignments) + 2 (edjers) + 4 (client) = 12</c>, not 6 and not 10 — the same collision
    /// as <c>CompassAuthorizationCoverageTests.ExpectedCompassEndpointCount</c>.
    /// </para>
    /// <para>
    /// 12 → 14 (feature 006 US3, issue #66, T082). The SOW write surface added <c>POST /</c> and
    /// <c>PUT /{sowId}</c> under <c>/api/compass/assignments/{assignmentId}/sows</c> — <c>GET /</c> is
    /// not a write method and does not count here, matching <c>WriteMethods</c>'s own filter. Per-route
    /// denial is <c>CompassSowAuthorizationTests</c>'.
    /// </para>
    /// <para>
    /// 14 → 15 (feature 010, merging main into PR #281). Contract periods gained
    /// <c>POST /api/compass/v1/admin/sows</c> — the migration's create path, distinct from 006's
    /// route above. This is the route that most rewards the sweep: it is the only way to
    /// write a <c>LegacyMigrated</c> period, which is exempt from BOTH partial database constraints,
    /// so a route left off the gated group here would be a permanent bypass rather than merely an
    /// over-permissive write. Per-route denial is <c>CompassAdminSowAuthorizationTests</c>', and the
    /// principal-level restriction is <c>CompassSowLegacyMigratedRestrictionTests</c>'. Feature
    /// 010's own assignment POST was deleted in an earlier merge, when feature 006 turned out to
    /// have shipped an equivalent route.
    /// </para>
    /// <para>
    /// 15 → 16 (developer tools, 2026-08-26).
    /// <c>POST /api/compass/developer-tools/clear-compass-data</c> — the Compass data clear. The
    /// paired <c>GET .../availability</c> is not a write and does not count, matching
    /// <see cref="WriteMethods"/>'s own filter. The tripwire fired on exactly the route it should
    /// have: this is the most destructive write Compass has, so raising the number is what lets both
    /// sweeps issue it for real and assert 403 for a Compass Admin and for a base EDJEr. Per-route
    /// denial is <c>CompassDeveloperToolsEndpointsTests</c>' in both projects.
    /// </para>
    /// <para>
    /// 16 → 18 (issue #593, 2026-09-08). A TRUE, permanent delete arrived for the first time:
    /// <c>DELETE /api/compass/assignments/{id}</c> (cascading to every SOW under it) and
    /// <c>DELETE /api/compass/assignments/{assignmentId}/sows/{sowId}</c> (one period), both behind
    /// <c>RolePolicy.CompassSuperAdmin</c> — never <c>CompassOps</c>, which every other assignment/SOW
    /// write on this surface accepts. Per-route denial is
    /// <c>CompassAssignmentAuthorizationTests</c>'/<c>CompassSowAuthorizationTests</c>'.
    /// </para>
    /// <para>
    /// ⚠️ This is the FIRST counted route whose existence depends on HOST CONFIGURATION, and that
    /// changes how a future mismatch should be read. The developer-tools routes are mapped only
    /// when <c>DeveloperToolsGate</c> allows it, and they are present here solely because
    /// <see cref="IntegrationTestFactory"/> sets <c>DeveloperTools:Enabled</c> to <c>true</c>. So if
    /// this assertion ever reports 15 where 16 was expected, the cause is almost certainly that
    /// flag being removed or renamed — NOT a deleted route — and the failure message above will point
    /// confidently in the wrong direction. The unit project's
    /// <c>CompassAuthorizationCoverageTests.ExpectedCompassEndpointCount</c> deliberately does NOT
    /// include these routes for the mirror-image reason: it builds the default
    /// <c>TestWebApplicationFactory</c>, whose host leaves the gate off.
    /// </para>
    /// </remarks>
    private const int ExpectedCompassWriteSurfaceCount = 18;

    private static readonly string[] WriteMethods = ["POST", "PUT", "PATCH", "DELETE"];

    private const int EmployeeTypeId = 1;
    private static int _nextEmployeeId = 5000;

    private IReadOnlyList<RouteEndpoint> CompassSurfaces() =>
        [.. _factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => NormalisedPath(e).StartsWith(
                CompassRoutePrefix, StringComparison.OrdinalIgnoreCase))];

    private IReadOnlyList<RouteEndpoint> CompassWriteSurfaces() =>
        [.. CompassSurfaces()
            .Where(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Any(m => WriteMethods.Contains(m, StringComparer.OrdinalIgnoreCase)))];

    private async Task<int> SeedEmployeeAsync(string firstName, string lastName, bool isActive = true)
    {
        var employeeId = Interlocked.Increment(ref _nextEmployeeId);
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (!await db.Set<EmployeeType>().AnyAsync(TestContext.Current.CancellationToken))
        {
            db.Set<EmployeeType>().Add(new EmployeeType
            {
                Id = EmployeeTypeId,
                TypeName = "Full Time",
                IsActive = true,
            });
        }

        db.Set<Employee>().Add(new Employee
        {
            Id = employeeId,
            FirstName = firstName,
            LastName = lastName,
            Email = $"compass-authz-{employeeId}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = isActive,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return employeeId;
    }

    // ---------------------------------------------------------------- discovery non-vacuity

    [Fact]
    public async Task TheSurfaceDiscovery_ActuallySeesCompassRoutes_SoTheWriteTripwireCanFire()
    {
        // Arrange -- these three discovery tests read the endpoint graph and touch no data, so the
        // reset is not load-bearing for them. It is here because integration-tests.md rule 6 says
        // EVERY test resets in Arrange, and a silent per-test exemption is how that convention decays
        // -- the next reader would have to re-derive whether the omission was reasoned or forgotten.
        await ResetDatabaseAsync();

        // Act -- the guard the write tripwires were missing. "Zero writes discovered" is
        // satisfied both by "Compass has no writes" (true) and by "the query matches nothing" (a bug),
        // and those are indistinguishable from the tripwire alone. This separates them: if the route
        // filter is wrong, THIS fails while the tripwires stay green.
        var allCompass = CompassSurfaces();

        // Assert
        allCompass.ShouldNotBeEmpty(
            "discovered no Compass routes at all -- the write-surface tripwires below are therefore "
            + "vacuous and cannot fire even after a real write lands");
    }

    [Fact]
    public async Task TheWriteMethodFilter_IdentifiesRealWrites_ProvenAgainstTheWholeEndpointGraph()
    {
        // Arrange (rule 6 -- see the note on the discovery test above)
        await ResetDatabaseAsync();

        // Arrange -- the last vacuity gap. Discovery is proven to see Compass routes, but nothing yet
        // proves the WRITE predicate can return true, because Compass has no writes to try it on. Run
        // it across the whole application instead: timesheet has plenty.
        var allEndpoints = _factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToList();

        // Act
        var writesAnywhere = allEndpoints
            .Where(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Any(m => WriteMethods.Contains(m, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        // Assert -- if this is empty the predicate is broken, and "zero Compass writes" below would be
        // reporting the bug rather than the fact
        writesAnywhere.ShouldNotBeEmpty(
            "the write-method predicate matched nothing across the entire endpoint graph -- it cannot "
            + "be trusted to identify a Compass write when one appears");
    }

    [Fact]
    public async Task RequestablePath_SubstitutesEveryRouteParameter_SoAWriteIsActuallyExercised()
    {
        // Arrange (rule 6 -- see the note on the discovery test above)
        await ResetDatabaseAsync();

        // Arrange -- proven against the parameterised READ route, because it exists today. Deferring
        // this until a write appears would mean shipping an untested URL builder and discovering it
        // only when the tripwire fires, which is the worst moment to debug it.
        var parameterised = CompassSurfaces()
            .Where(e => NormalisedPath(e).Contains('{'))
            .ToList();

        // Assert (non-vacuity) -- if no Compass route has a parameter, this proves nothing
        parameterised.ShouldNotBeEmpty(
            "expected at least one parameterised Compass route to exercise the substitution");

        // Act / Assert
        foreach (var endpoint in parameterised)
        {
            var path = RequestablePath(endpoint);

            path.Contains('{').ShouldBeFalse(
                $"unsubstituted route parameter in '{path}' -- this would 404 before "
                + "authorization runs, turning a routing failure into a fake authorization result");
            path.ShouldStartWith("/api/compass");
        }
    }

    // ---------------------------------------------------------------- T039

    [Fact]
    public async Task CompassAdmin_IsRefusedEveryCompassWrite()
    {
        // Arrange
        await ResetDatabaseAsync();
        (await _factory.DatabaseRolesAsync()).ShouldBeEmpty(
            "a leaked user_roles grant would make this denial meaningless");
        var client = _factory.AsCompassAdmin();
        var writes = CompassWriteSurfaces();

        // Assert (guard) -- an empty loop passes; make the emptiness itself the assertion
        writes.Count.ShouldBe(
            ExpectedCompassWriteSurfaceCount,
            "Compass gained a write surface. Issue it here as a Compass Admin and assert 403 before "
            + "bumping ExpectedCompassWriteSurfaceCount.");

        // Act / Assert -- runs for real once writes exist
        foreach (var write in writes)
        {
            var method = (write.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .First(m => WriteMethods.Contains(m, StringComparer.OrdinalIgnoreCase));
            var request = new HttpRequestMessage(new HttpMethod(method), RequestablePath(write));

            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(
                HttpStatusCode.Forbidden, $"{method} {write.RoutePattern.RawText} was not refused");
        }

        // Assert (positive control) -- the same client is genuinely usable, so a 403 above would have
        // meant "refused", not "this principal can do nothing anywhere"
        var readBack = await client.GetAsync(
            $"{EmployeesUrl}/{await SeedEmployeeAsync("Grace", "Hopper")}",
            TestContext.Current.CancellationToken);
        readBack.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Setting the delivery-team flag is EDJEr configuration, so it takes the Compass root role.
    /// </summary>
    /// <remarks>
    /// The escalation this guards is <c>RolePolicy.CompassAdmin</c>, which is READ-ONLY despite the
    /// name and would otherwise look like the natural policy for an admin surface (AC-44). Asserted
    /// as an exact 403: a loose "not 200" passes against a stale DevBypass API, which
    /// auto-authenticates every request as a superuser, and the gate then disappears silently.
    /// </remarks>
    [Fact]
    public async Task CompassAdmin_IsRefusedTheDeliveryTeamFlagWrite_WhichNeedsTheRootRole()
    {
        // Arrange
        await ResetDatabaseAsync();
        (await _factory.DatabaseRolesAsync()).ShouldBeEmpty(
            "a leaked user_roles grant would make this denial meaningless");
        var employeeId = await SeedEmployeeAsync("Radia", "Perlman");
        var payload = new
        {
            firstName = "Radia",
            lastName = "Perlman",
            hireDate = "2020-01-01",
            email = $"compass-authz-{employeeId}@example.test",
            employeeTypeId = EmployeeTypeId,
            coachEmployeeId = (int?)null,
            stateOfResidence = "OH",
            isActive = true,
            timesheetRequired = true,
            canSubmitUnder40 = false,
            includeInPayroll = true,
            isDeliveryTeam = false,
        };

        // Act
        var refused = await _factory.AsCompassAdmin().PutAsJsonAsync(
            $"/api/compass/v1/admin/edjers/{employeeId}",
            payload,
            TestContext.Current.CancellationToken);

        // Assert
        refused.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            "the read-only Compass Admin role must not reach EDJEr configuration");

        // Assert (positive control) -- the route exists, the payload binds, and the root role really
        // does get through it. Without this, the 403 above is indistinguishable from a 403 earned by
        // a malformed request or a route that is not there at all.
        var allowed = await _factory.AsCompassSuperAdmin().PutAsJsonAsync(
            $"/api/compass/v1/admin/edjers/{employeeId}",
            payload,
            TestContext.Current.CancellationToken);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- T040

    [Fact]
    public async Task CompassAdmin_CanReadAnActiveEmployee()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync("Ada", "Lovelace");
        var client = _factory.AsCompassAdmin();

        // Act
        var response = await client.GetAsync(
            $"{EmployeesUrl}/{employeeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CompassAdmin_CanReadAnINACTIVEEmployee_PerAc44()
    {
        // Arrange -- read-only is READ, not blind. AC-44 entitles the Admin to inactive records too, so
        // a filter that quietly hides them would be a defect this test exists to catch.
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync("Katherine", "Johnson", isActive: false);
        var client = _factory.AsCompassAdmin();

        // Act
        var response = await client.GetAsync(
            $"{EmployeesUrl}/{employeeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK, "an inactive employee must still be readable by a Compass Admin (AC-44)");
        var dto = await response.Content.ReadFromJsonAsync<CompassEmployeeDto>(
            TestContext.Current.CancellationToken);
        dto.ShouldNotBeNull();
        dto.Id.ShouldBe(employeeId);
        dto.IsActive.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- T041

    [Fact]
    public async Task CompassSuperAdmin_ReachesEveryCompassFunction()
    {
        // Arrange -- AC-45/BR-17: the Compass root reaches everything attributed to Ops, Sales, Admin
        // and the base EDJEr role. Asserted across every discovered read surface rather than the one
        // route this test knows about, so a surface added later is covered without editing this test.
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync("Annie", "Easley");
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync(
            $"{EmployeesUrl}/{employeeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CompassOpsAndSales_AreRefusedTheAdminGatedRead_ShowingRolesAreNotInterchangeable()
    {
        // Arrange -- if every Compass role satisfied every Compass policy, all four would be one role
        // and the isolation matrix would prove nothing.
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync("Mary", "Jackson");

        // Act
        var ops = await _factory.AsCompassOps().GetAsync(
            $"{EmployeesUrl}/{employeeId}", TestContext.Current.CancellationToken);
        var sales = await _factory.AsCompassSales().GetAsync(
            $"{EmployeesUrl}/{employeeId}", TestContext.Current.CancellationToken);

        // Assert
        ops.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        sales.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- T042

    [Fact]
    public async Task BaseEdjErOnly_IsRefusedEveryCompassWrite()
    {
        // Arrange -- the floor. Every authenticated EDJEr holds this much (FR-012) and no more.
        await ResetDatabaseAsync();
        var client = _factory.AsBaseEdjErOnly();
        var writes = CompassWriteSurfaces();

        // Assert (guard) -- same emptiness tripwire as T039
        writes.Count.ShouldBe(
            ExpectedCompassWriteSurfaceCount,
            "Compass gained a write surface. Issue it here as a base EDJEr and assert 403 first.");

        // Act / Assert
        foreach (var write in writes)
        {
            var method = (write.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .First(m => WriteMethods.Contains(m, StringComparer.OrdinalIgnoreCase));
            var request = new HttpRequestMessage(new HttpMethod(method), RequestablePath(write));

            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task BaseEdjErOnly_IsRefusedTheCompassRead()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync("Dorothy", "Vaughan");
        var client = _factory.AsBaseEdjErOnly();

        // Act
        var response = await client.GetAsync(
            $"{EmployeesUrl}/{employeeId}", TestContext.Current.CancellationToken);

        // Assert -- 403 not 401: this principal IS authenticated, it just holds no Compass role
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

}
