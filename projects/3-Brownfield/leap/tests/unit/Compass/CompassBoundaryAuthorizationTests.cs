using System.Net;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// Server-side authorization on the published Compass Directory boundary (spec 009 US2; FR-016,
/// SC-003) — the unit-level counterpart to <c>tests/integration/Endpoints/CompassAuthorizationTests</c>,
/// which already covers the 403 case at the integration layer. Neither layer had a 401 assertion for
/// this specific route before this file.
/// </summary>
/// <remarks>
/// Exact status codes only (standing rule 2): a negated assertion like <c>ShouldNotBe(200)</c> would
/// still pass under a stale DevBypass API and silently stop testing anything.
/// </remarks>
public class CompassBoundaryAuthorizationTests : IClassFixture<TestWebApplicationFactory>
{
    private const int EmployeeId = 1;

    private readonly TestWebApplicationFactory _factory;

    public CompassBoundaryAuthorizationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        Seed(factory);
    }

    private const int ClientId = 1;

    private const int AssignmentId = 1;

    private const string InvoiceFrequenciesRoute = "/api/compass/v1/invoice-frequencies";

    private static string Route(int id) => $"/api/compass/v1/employees/{id}";

    private static string ClientRoute(int id) => $"/api/compass/v1/clients/{id}";

    private static string BillableCategoriesRoute(int id) => $"/api/compass/v1/clients/{id}/billable-categories";

    private static string AssignmentByIdRoute(int id) => $"/api/compass/v1/assignments/{id}";

    private static string AssignmentsByEmployeeRoute(int id) => $"/api/compass/v1/employees/{id}/assignments";

    private static string AssignmentsByClientRoute(int id) => $"/api/compass/v1/clients/{id}/assignments";

    private static string SowsByAssignmentRoute(int id) => $"/api/compass/v1/assignments/{id}/sows";

    /// <summary>
    /// Every published route with a human-readable name and its concrete (real, seeded) URL. The
    /// natural home for T068/T069's "every route" sweeps.
    /// </summary>
    private static (string Name, string Path)[] AllRouteDescriptions() =>
    [
        ("GET /employees/{id}", Route(EmployeeId)),
        ("GET /employees/{id}/assignments", AssignmentsByEmployeeRoute(EmployeeId)),
        ("GET /clients/{id}", ClientRoute(ClientId)),
        ("GET /clients/{id}/billable-categories", BillableCategoriesRoute(ClientId)),
        ("GET /clients/{id}/assignments", AssignmentsByClientRoute(ClientId)),
        ("GET /assignments/{id}", AssignmentByIdRoute(AssignmentId)),
        ("GET /assignments/{id}/sows", SowsByAssignmentRoute(AssignmentId)),
        ("GET /invoice-frequencies", InvoiceFrequenciesRoute),
    ];

    /// <summary>
    /// The seven routes that take an identifier, each paired with its route-builder and the real,
    /// seeded id it should be called with. <c>invoice-frequencies</c> is deliberately excluded — it
    /// has no path parameter, so T070/T071's "same id, different existence" pairing does not apply to
    /// it (per the task text).
    /// </summary>
    private static (string Name, Func<int, string> Build, int RealId)[] IdBasedRouteBuilders() =>
    [
        ("employees/{id}", Route, EmployeeId),
        ("employees/{id}/assignments", AssignmentsByEmployeeRoute, EmployeeId),
        ("clients/{id}", ClientRoute, ClientId),
        ("clients/{id}/billable-categories", BillableCategoriesRoute, ClientId),
        ("clients/{id}/assignments", AssignmentsByClientRoute, ClientId),
        ("assignments/{id}", AssignmentByIdRoute, AssignmentId),
        ("assignments/{id}/sows", SowsByAssignmentRoute, AssignmentId),
    ];

    /// <summary>
    /// T068 (FR-020, Constitution Principle IV). A session holding ALL NINE timesheet role strings —
    /// including the timesheet root — and no Compass group must be refused every published route.
    /// </summary>
    /// <remarks>
    /// This is a DIFFERENT, STRONGER probe than <see cref="Get_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403"/>'s
    /// <c>AsTimesheetRootOnly()</c>, which holds exactly one role (<c>SuperAdmin</c>). T068 asks
    /// specifically about holding all nine at once — <see cref="TestAuthHandler.DefaultPrivileges"/> —
    /// which is what a bare <c>_factory.CreateClient()</c> (no privilege-override header) resolves to.
    /// This is "the one direction that fails OPEN" (Principle IV): a Compass policy that ever
    /// (mis)accepted a timesheet role string would pass under either probe, but only this one proves it
    /// against the full role set a real timesheet SuperAdmin actually carries, not a synthetic
    /// single-role stand-in.
    /// </remarks>
    [Fact]
    public async Task Get_AsSessionHoldingAllNineTimesheetRolesAndNoCompassGroup_Returns403OnEveryRoute()
    {
        // Arrange -- non-vacuity guard: an empty or short route list would let this pass having proven
        // nothing about the boundary.
        var routes = AllRouteDescriptions();
        routes.Length.ShouldBe(
            8, "the published boundary has exactly eight routes; a shorter list here silently stops "
            + "proving anything about the missing ones");

        // Act -- a bare client carries TestAuthHandler.DefaultPrivileges: all nine timesheet role
        // strings, including SuperAdmin, and no Compass group.
        var client = _factory.CreateClient();
        var failures = new List<string>();
        foreach (var (name, path) in routes)
        {
            var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                failures.Add($"{name} -> {(int)response.StatusCode}");
            }
        }

        // Assert
        failures.ShouldBeEmpty(
            "routes not refused with 403 for a session holding all nine timesheet roles and no "
            + $"Compass group: {string.Join(", ", failures)}");
    }

    /// <summary>
    /// T069 (FR-019; AC-45, BR-17). The Compass Super Admin must succeed on every published route —
    /// structurally already true because <c>AddCompassAuthorization</c> makes the Compass root satisfy
    /// <see cref="RolePolicy.CompassAdmin"/> (and the other two subordinate policies). Proven per-route
    /// rather than only assumed, so a future surface that accidentally required a stricter/different
    /// policy would be caught here rather than only in the structural coverage suite.
    /// </summary>
    [Fact]
    public async Task Get_AsCompassSuperAdmin_SucceedsOnEveryRoute()
    {
        // Arrange -- non-vacuity guard, matching the sweep above.
        var routes = AllRouteDescriptions();
        routes.Length.ShouldBe(8, "non-vacuity guard -- see the all-nine-timesheet-roles sweep above");

        // Act
        var client = _factory.AsCompassSuperAdmin();
        var failures = new List<string>();
        foreach (var (name, path) in routes)
        {
            var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                failures.Add($"{name} -> {(int)response.StatusCode}");
            }
        }

        // Assert -- exact code, not a negation (rule: never assert "not 401/403"; a stale-bypass API
        // would still pass a negated check while proving nothing). Every route's seed data is a real,
        // existing record, so 200 is the correct code for every one of the eight.
        failures.ShouldBeEmpty($"routes that did not return 200 for the Compass Super Admin: {string.Join(", ", failures)}");
    }

    /// <summary>
    /// T070 + T071 (FR-021, FR-018). A caller with no Compass authority gets the exact same refusal —
    /// 403 — whether the id names a real, seeded record or a clearly nonexistent one. Combining the two
    /// tasks: the SAME assertion proves both (a) a denial is indistinguishable from a not-found (T070)
    /// and (b) a low, easily-guessable, real id earns no special treatment over an id that does not
    /// exist at all (T071) — the policy gate runs before any lookup, so the id is never inspected either
    /// way.
    /// </summary>
    [Fact]
    public async Task Get_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403IdenticallyForRealAndNonexistentIds()
    {
        // Arrange -- non-vacuity guard. invoice-frequencies is deliberately excluded (no id segment),
        // so seven, not eight.
        const int nonexistentId = 999_999;
        var routes = IdBasedRouteBuilders();
        routes.Length.ShouldBe(
            7, "seven of the eight routes take an id; invoice-frequencies has none and is excluded "
            + "on purpose (see IdBasedRouteBuilders' remarks)");

        // Act
        var client = _factory.AsTimesheetRootOnly();
        var mismatches = new List<string>();
        foreach (var (name, build, realId) in routes)
        {
            var existing = await client.GetAsync(build(realId), TestContext.Current.CancellationToken);
            var missing = await client.GetAsync(build(nonexistentId), TestContext.Current.CancellationToken);

            if (existing.StatusCode != HttpStatusCode.Forbidden
                || missing.StatusCode != HttpStatusCode.Forbidden)
            {
                mismatches.Add(
                    $"{name} -> existing id={(int)existing.StatusCode}, nonexistent id={(int)missing.StatusCode}");
            }
        }

        // Assert
        mismatches.ShouldBeEmpty(
            "routes where a real and a nonexistent id did not both refuse with 403: "
            + string.Join(", ", mismatches));
    }

    [Fact]
    public async Task Get_WithNoCredential_Returns401()
    {
        // Act
        var response = await _factory.AsAnonymous()
            .GetAsync(Route(EmployeeId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403()
    {
        // Arrange -- authenticated, holding the timesheet root and nothing else. Compass inherits
        // nothing, not even root (Principle IV) -- the direction that fails OPEN if this were ever
        // satisfied by mistake.
        var response = await _factory.AsTimesheetRootOnly()
            .GetAsync(Route(EmployeeId), TestContext.Current.CancellationToken);

        // Assert -- 403, not 401: this principal IS authenticated, it just holds no Compass role.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetInvoiceFrequencies_WithNoCredential_Returns401()
    {
        // Act
        var response = await _factory.AsAnonymous()
            .GetAsync(InvoiceFrequenciesRoute, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetInvoiceFrequencies_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403()
    {
        // Arrange -- authenticated, holding the timesheet root and nothing else. Compass inherits
        // nothing, not even root (Principle IV) -- the direction that fails OPEN if this were ever
        // satisfied by mistake.
        var response = await _factory.AsTimesheetRootOnly()
            .GetAsync(InvoiceFrequenciesRoute, TestContext.Current.CancellationToken);

        // Assert -- 403, not 401: this principal IS authenticated, it just holds no Compass role.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetClient_WithNoCredential_Returns401()
    {
        // Act
        var response = await _factory.AsAnonymous()
            .GetAsync(ClientRoute(ClientId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetClient_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403()
    {
        // Arrange -- authenticated, holding the timesheet root and nothing else. Compass inherits
        // nothing, not even root (Principle IV) -- the direction that fails OPEN if this were ever
        // satisfied by mistake.
        var response = await _factory.AsTimesheetRootOnly()
            .GetAsync(ClientRoute(ClientId), TestContext.Current.CancellationToken);

        // Assert -- 403, not 401: this principal IS authenticated, it just holds no Compass role.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBillableCategories_WithNoCredential_Returns401()
    {
        // Act
        var response = await _factory.AsAnonymous()
            .GetAsync(BillableCategoriesRoute(ClientId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBillableCategories_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403()
    {
        // Arrange -- authenticated, holding the timesheet root and nothing else. Compass inherits
        // nothing, not even root (Principle IV) -- the direction that fails OPEN if this were ever
        // satisfied by mistake.
        var response = await _factory.AsTimesheetRootOnly()
            .GetAsync(BillableCategoriesRoute(ClientId), TestContext.Current.CancellationToken);

        // Assert -- 403, not 401: this principal IS authenticated, it just holds no Compass role.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAssignmentById_WithNoCredential_Returns401()
    {
        // Act
        var response = await _factory.AsAnonymous()
            .GetAsync(AssignmentByIdRoute(AssignmentId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAssignmentById_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403()
    {
        // Arrange -- authenticated, holding the timesheet root and nothing else. Compass inherits
        // nothing, not even root (Principle IV) -- the direction that fails OPEN if this were ever
        // satisfied by mistake.
        var response = await _factory.AsTimesheetRootOnly()
            .GetAsync(AssignmentByIdRoute(AssignmentId), TestContext.Current.CancellationToken);

        // Assert -- 403, not 401: this principal IS authenticated, it just holds no Compass role.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAssignmentsByEmployee_WithNoCredential_Returns401()
    {
        // Act
        var response = await _factory.AsAnonymous()
            .GetAsync(AssignmentsByEmployeeRoute(EmployeeId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAssignmentsByEmployee_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403()
    {
        // Arrange -- authenticated, holding the timesheet root and nothing else. Compass inherits
        // nothing, not even root (Principle IV) -- the direction that fails OPEN if this were ever
        // satisfied by mistake.
        var response = await _factory.AsTimesheetRootOnly()
            .GetAsync(AssignmentsByEmployeeRoute(EmployeeId), TestContext.Current.CancellationToken);

        // Assert -- 403, not 401: this principal IS authenticated, it just holds no Compass role.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAssignmentsByClient_WithNoCredential_Returns401()
    {
        // Act
        var response = await _factory.AsAnonymous()
            .GetAsync(AssignmentsByClientRoute(ClientId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAssignmentsByClient_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403()
    {
        // Arrange -- authenticated, holding the timesheet root and nothing else. Compass inherits
        // nothing, not even root (Principle IV) -- the direction that fails OPEN if this were ever
        // satisfied by mistake.
        var response = await _factory.AsTimesheetRootOnly()
            .GetAsync(AssignmentsByClientRoute(ClientId), TestContext.Current.CancellationToken);

        // Assert -- 403, not 401: this principal IS authenticated, it just holds no Compass role.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSowsByAssignment_WithNoCredential_Returns401()
    {
        // Act
        var response = await _factory.AsAnonymous()
            .GetAsync(SowsByAssignmentRoute(AssignmentId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSowsByAssignment_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403()
    {
        // Arrange -- authenticated, holding the timesheet root and nothing else. Compass inherits
        // nothing, not even root (Principle IV) -- the direction that fails OPEN if this were ever
        // satisfied by mistake.
        var response = await _factory.AsTimesheetRootOnly()
            .GetAsync(SowsByAssignmentRoute(AssignmentId), TestContext.Current.CancellationToken);

        // Assert -- 403, not 401: this principal IS authenticated, it just holds no Compass role.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static void Seed(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (!context.Set<Employee>().Any())
        {
            context.Set<EmployeeType>().Add(new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });
            context.Set<Employee>().Add(new Employee
            {
                Id = EmployeeId,
                FirstName = "Grace",
                LastName = "Hopper",
                Email = "grace.hopper@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = 1,
                HireDate = new DateOnly(2020, 1, 1),
                IsActive = true,
            });

            context.SaveChanges();
        }

        if (!context.Set<InvoiceFrequencyType>().Any())
        {
            context.Set<InvoiceFrequencyType>().Add(
                new InvoiceFrequencyType { Id = 1, TypeName = "Monthly", IsActive = true });

            context.SaveChanges();
        }

        if (!context.Set<Client>().Any())
        {
            context.Set<Client>().Add(new Client
            {
                Id = ClientId,
                ClientName = "Acme Corp",
                IsInternal = false,
            });

            context.SaveChanges();
        }

        if (!context.Set<ClientAssignment>().Any())
        {
            context.Set<ClientAssignment>().Add(new ClientAssignment
            {
                Id = AssignmentId,
                EmployeeId = EmployeeId,
                ClientId = ClientId,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = null,
            });

            context.SaveChanges();
        }
    }
}
