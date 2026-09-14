using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// The Team Directory read surface — AC-5, AC-6, AC-7.
/// </summary>
/// <remarks>
/// <para>
/// Served from the Compass application read surface, not the published Directory boundary
/// (ADR-008). The distinction is why these assertions are about a session-authenticated, role-scoped
/// projection rather than a stable integration contract.
/// </para>
/// <para>
/// Assertions are on the response payload, not on a rendered page. FR-005 requires withheld
/// data to be absent from what the server sends, and a rendered assertion proves nothing about what
/// crossed the wire.
/// </para>
/// </remarks>
public class CompassTeamDirectoryEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Route = "/api/compass/team-directory";

    private readonly TestWebApplicationFactory _factory;

    public CompassTeamDirectoryEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        SeedDirectory(factory);
    }

    // ------------------------------------------------------------------ AC-5 — the listing

    [Fact]
    public async Task Get_AsAnyAuthenticatedUser_ReturnsOk()
    {
        // FR-001: both directories are reachable by ANY authenticated user; no Compass role required.
        // The default test identity carries only Timesheet roles, which is exactly tier Baseline.
        var response = await _factory.AsBaselineEdjer().GetAsync(Route, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_Unauthenticated_IsRejected()
    {
        var response = await _factory.AsAnonymous().GetAsync(Route, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_ReturnsEveryAc5Column()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer());

        var ada = rows.Single(r => r.LastName == "Active");
        ada.FirstName.ShouldBe("Ada");
        ada.HireDate.ShouldBe(new DateOnly(2020, 1, 15));
        ada.Email.ShouldBe("ada.active@example.test");
        ada.EmployeeType.ShouldBe("Full Time");
        ada.Coach.ShouldBe("Cody Coach");
        ada.State.ShouldBe("OH");
        ada.CurrentAssignments.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Get_OrdersByHireDateByDefault()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.Select(r => r.HireDate).ShouldBe(rows.Select(r => r.HireDate).OrderBy(d => d));
    }

    [Fact]
    public async Task Get_EdjerWithTwoConcurrentAssignments_ReturnsBoth()
    {
        // AC-5, FR-008: "all current assignments display when an EDJEr has more than one". A scalar
        // shape silently truncates this, and the seeded directory has several such EDJErs.
        var rows = await GetRows(_factory.AsBaselineEdjer());

        var multi = rows.Single(r => r.LastName == "Multi");
        multi.CurrentAssignments.Select(a => a.ClientName)
            .OrderBy(n => n)
            .ShouldBe(["Client One", "Client Two"]);
    }

    [Fact]
    public async Task Get_EdjerWithNoAssignment_KeepsTheRowWithAnEmptyCell()
    {
        // A new hire. The row must survive — dropping it would hide every internal, non-billable
        // EDJEr from the directory.
        var rows = await GetRows(_factory.AsBaselineEdjer());

        var newHire = rows.Single(r => r.LastName == "Newhire");
        newHire.CurrentAssignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_AssignmentEndingToday_IsStillCurrent()
    {
        // BR-7 / FR-009: "end date empty or NOT IN THE PAST" — inclusive of today. Writing this as
        // strictly-future shows an EDJEr as unassigned on their final working day, and it is the
        // exact boundary the derivation contract names.
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.Single(r => r.LastName == "Endstoday").CurrentAssignments.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Get_AssignmentThatEndedYesterday_IsNotCurrent()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.Single(r => r.LastName == "Endedyesterday").CurrentAssignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_EdjerWithoutACoach_ReturnsAnEmptyCoachRatherThanDroppingTheRow()
    {
        // The coach FK is nullable and coach is optional (AC-17). An inner join here silently hides
        // every coachless EDJEr — who are precisely the internal, non-billable staff.
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.Single(r => r.LastName == "Nocoach").Coach.ShouldBeNull();
    }

    [Fact]
    public async Task Get_RowsCarryTheCoachsOwnId_ForTheDrillIn()
    {
        // The Team Directory's coach cell is a link into the coach's own record (issue #245,
        // follow-up) — a name alone cannot be one without the id it links to.
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.Single(r => r.LastName == "Active").CoachId.ShouldBe(9);
    }

    [Fact]
    public async Task Get_EdjerWithoutACoach_CoachIdIsNullToo()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.Single(r => r.LastName == "Nocoach").CoachId.ShouldBeNull();
    }

    // ------------------------------------------------------------------ AC-9 / BR-1 — visibility

    [Fact]
    public async Task Get_AsBaseline_ExcludesInactiveEdjers()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.ShouldNotContain(r => r.LastName == "Inactive");
    }

    [Theory]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    [InlineData(RolePolicy.CompassSuperAdminRole)]
    public async Task Get_AsAnyElevatedRole_CanReachInactiveEdjers(string role)
    {
        // All three elevated strings, not just one: BR-1 treats them identically, so a check written
        // against a single role would miss a policy wired to the wrong one.
        var rows = await GetRows(_factory.AsRoles(role), "?status=all");

        rows.ShouldContain(r => r.LastName == "Inactive");
    }

    [Fact]
    public async Task Get_AsBaseline_OmitsTheActiveStatusField()
    {
        // A baseline result set is all-active by construction, so the field carries no information
        // and is not sent. Contract row 2.
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.ShouldAllBe(r => r.IsActive == null);
    }

    [Fact]
    public async Task Get_AsElevated_IncludesTheActiveStatusField()
    {
        // AC-15: where inactive EDJErs are reachable, their status must be distinguishable.
        var rows = await GetRows(_factory.AsRoles(RolePolicy.CompassAdminRole), "?status=all");

        rows.ShouldContain(r => r.IsActive == false);
        rows.ShouldContain(r => r.IsActive == true);
    }

    // ------------------------------------------------------------------ Q2 — the status filter

    [Fact]
    public async Task Get_StatusFilterDefaultsToActive_ForEveryTier()
    {
        // Q2: every role's DEFAULT view lists active EDJErs only, so an elevated viewer sees the same
        // thing a regular EDJEr does until they ask otherwise.
        var elevated = await GetRows(_factory.AsRoles(RolePolicy.CompassAdminRole));

        elevated.ShouldNotContain(r => r.LastName == "Inactive");
    }

    [Fact]
    public async Task Get_ElevatedCanWidenTheStatusFilter()
    {
        var inactiveOnly = await GetRows(_factory.AsRoles(RolePolicy.CompassAdminRole), "?status=inactive");

        inactiveOnly.ShouldNotBeEmpty();
        inactiveOnly.ShouldAllBe(r => r.IsActive == false);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("inactive")]
    [InlineData("ACTIVE")]
    [InlineData("nonsense")]
    public async Task Get_AsBaseline_NoStatusFilterValueEverYieldsAnInactiveEdjer(string status)
    {
        // FR-011a and research R-7: the entitlement predicate and the presentation filter are
        // SEPARATE clauses. Folding them into one is how a UI parameter silently becomes an
        // authorization input — and it is invisible, because tier B is never shown the control.
        var rows = await GetRows(_factory.AsBaselineEdjer(), $"?status={status}");

        rows.ShouldNotContain(r => r.LastName == "Inactive");
    }

    // ------------------------------------------------------------------ AC-6 — search, filter, sort

    [Fact]
    public async Task Get_SearchesByPartialLastName_CaseInsensitively()
    {
        var lower = await GetRows(_factory.AsBaselineEdjer(), "?search=act");
        var upper = await GetRows(_factory.AsBaselineEdjer(), "?search=ACT");

        lower.ShouldContain(r => r.LastName == "Active");
        upper.Select(r => r.LastName).ShouldBe(lower.Select(r => r.LastName));
    }

    [Fact]
    public async Task Get_SearchMatchesLastNameOnly_NotFirstNameOrEmail()
    {
        // AC-6 says "searches by last name". Widening it to any field looks helpful and makes the
        // result set unpredictable — an EDJEr named Grace would match every grace.* email.
        var rows = await GetRows(_factory.AsBaselineEdjer(), "?search=Ada");

        rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_FiltersByEmployeeType()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer(), "?employeeType=Contractor");

        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(r => r.EmployeeType == "Contractor");
    }

    [Fact]
    public async Task Get_FiltersByState()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer(), "?state=OH");

        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(r => r.State == "OH");
    }

    [Fact]
    public async Task Get_CombinesFiltersConjunctively()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer(), "?employeeType=Full Time&state=OH");

        rows.ShouldAllBe(r => r.EmployeeType == "Full Time" && r.State == "OH");
    }

    [Fact]
    public async Task Get_FiltersByCoach_ToJustThatCoachsTeam()
    {
        // Issue #655: narrows to one coach's direct reports. By id, since two coaches can share a
        // display name — the row already carries CoachId for exactly this reason.
        var rows = await GetRows(_factory.AsBaselineEdjer(), "?coachId=9");

        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(r => r.CoachId == 9);
        rows.ShouldNotContain(r => r.LastName == "Nocoach");

        // Cody Coach themselves has no coach, so they are excluded by their OWN filter too.
        rows.ShouldNotContain(r => r.LastName == "Coach");
    }

    [Fact]
    public async Task Get_AnUnusedCoachId_YieldsAnEmptyList_NotAnError()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer(), "?coachId=999999");

        rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("lastName")]
    [InlineData("firstName")]
    [InlineData("email")]
    [InlineData("state")]
    [InlineData("employeeType")]
    public async Task Get_SortsByTheChosenColumn(string column)
    {
        var rows = await GetRows(_factory.AsBaselineEdjer(), $"?sort={column}");

        var values = rows.Select(r => Column(r, column)).ToList();
        values.ShouldBe(values.OrderBy(v => v, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Get_SortDirectionIsReversible()
    {
        var ascending = await GetRows(_factory.AsBaselineEdjer(), "?sort=lastName");
        var descending = await GetRows(_factory.AsBaselineEdjer(), "?sort=lastName&desc=true");

        descending.Select(r => r.LastName).ShouldBe(ascending.Select(r => r.LastName).Reverse());
    }

    [Fact]
    public async Task Get_SortsByCoach_WithoutDroppingCoachlessEdjers()
    {
        // Sorting by a NULLABLE navigation is the arm most likely to break: ordering by the coach's
        // surname must not turn the LEFT join into an inner one and lose every coachless EDJEr —
        // who are exactly the internal, non-billable staff.
        var unsorted = await GetRows(_factory.AsBaselineEdjer());
        var sorted = await GetRows(_factory.AsBaselineEdjer(), "?sort=coach");

        sorted.Count.ShouldBe(unsorted.Count);
        sorted.ShouldContain(r => r.LastName == "Nocoach");

        // The named coaches still order among themselves.
        var named = sorted.Where(r => r.Coach is not null).Select(r => r.Coach!).ToList();
        named.ShouldBe(named.OrderBy(c => c, StringComparer.Ordinal), ignoreOrder: false);
    }

    [Fact]
    public async Task Get_SortsByCurrentClients_AlphabeticallyByTheClientTheCellShowsFirst()
    {
        // FR-010: `Current Client(s)` sorts like every other column. The key is the employee's
        // alphabetically-first CURRENT client, so the ordering matches what the cell displays — the
        // only ordering a viewer can predict from the screen.
        //
        // WHAT THIS TEST DOES NOT ESTABLISH, deliberately: that the arm translates to SQL. It is a
        // correlated subquery over a collection navigation, a shape the InMemory provider evaluates in
        // .NET where anything "works".
        // `CompassTeamDirectorySortTests.SortingByCurrentClients_*` are the real-PostgreSQL cases that
        // prove translatability; this one keeps the branch from being rewritten untested in the suite
        // that actually gates every commit.
        var rows = await GetRows(_factory.AsBaselineEdjer(), "?sort=currentClients");

        // Where the UNASSIGNED rows land is asserted only against PostgreSQL, because the two
        // providers genuinely disagree: Postgres orders NULLs last ascending, LINQ-to-objects orders
        // them first. Asserting either placement here would encode the provider, not the requirement,
        // so this asserts the half that is the same in both — the assigned rows order among themselves.
        var keys = rows
            .Where(r => r.CurrentAssignments.Count > 0)
            .Select(r => r.CurrentAssignments
                .Select(a => a.ClientName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .First())
            .ToList();

        keys.ShouldNotBeEmpty();
        keys.ShouldBe(keys.OrderBy(k => k, StringComparer.Ordinal), ignoreOrder: false);
    }

    [Fact]
    public async Task Get_SortsByCurrentClients_KeepsUnassignedEdjersInTheListing()
    {
        // The same trap as the coach arm: an ordering key derived from a navigation must not turn the
        // LEFT join into an inner one and drop every unassigned EDJEr, who are exactly the new hires
        // and internal staff. Their POSITION is PostgreSQL's business (see above); their PRESENCE is
        // not provider-specific.
        var unsorted = await GetRows(_factory.AsBaselineEdjer());
        var sorted = await GetRows(_factory.AsBaselineEdjer(), "?sort=currentClients");

        sorted.Count.ShouldBe(unsorted.Count);

        // Both flavours of unassigned: never assigned (Nina), and assigned-but-ended (Yvonne).
        sorted.ShouldContain(r => r.LastName == "Newhire");
        sorted.ShouldContain(r => r.LastName == "Endedyesterday");
        sorted.Single(r => r.LastName == "Newhire").CurrentAssignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_SortsByCurrentClients_IgnoresEndedAssignments()
    {
        // The cell shows CURRENT clients, so the key must come from the same set it renders. An ended
        // assignment influencing the order would sort a row by a client the viewer cannot see — the
        // order and the visible value disagreeing is worse than either being wrong alone.
        var sorted = await GetRows(_factory.AsBaselineEdjer(), "?sort=currentClients");

        // Yvonne's only assignment ended yesterday, so she sorts as unassigned with an empty cell.
        sorted.Single(r => r.LastName == "Endedyesterday").CurrentAssignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_SortsByCurrentClients_IsReversible()
    {
        var ascending = await GetRows(_factory.AsBaselineEdjer(), "?sort=currentClients");
        var descending = await GetRows(_factory.AsBaselineEdjer(), "?sort=currentClients&desc=true");

        descending.Select(r => r.Id).ShouldBe(ascending.Select(r => r.Id).Reverse());
    }

    [Fact]
    public async Task Get_UnknownSortColumn_FallsBackToTheDefaultRatherThanFailing()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer(), "?sort=notAColumn");

        rows.Select(r => r.HireDate).ShouldBe(rows.Select(r => r.HireDate).OrderBy(d => d));
    }

    [Fact]
    public async Task Get_NoMatches_ReturnsAnEmptyListNotAnError()
    {
        // FR-013 / J2 variant 2a: an explicit empty state, never an error or a 404.
        var response = await _factory.AsBaselineEdjer()
            .GetAsync($"{Route}?search=zzzznobody", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Rows(response)).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ Helpers

    private static async Task<List<TeamDirectoryRowDto>> GetRows(HttpClient client, string query = "")
    {
        var response = await client.GetAsync(Route + query, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await Rows(response);
    }

    private static async Task<List<TeamDirectoryRowDto>> Rows(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<List<TeamDirectoryRowDto>>(
            TestContext.Current.CancellationToken) ?? [];

    private static string Column(TeamDirectoryRowDto row, string column) => column switch
    {
        "firstName" => row.FirstName,
        "email" => row.Email,
        "state" => row.State,
        "employeeType" => row.EmployeeType,
        _ => row.LastName,
    };

    /// <summary>
    /// Seeds a small, hand-authored directory covering every AC-5/AC-6/AC-9 case this file asserts.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <c>CompassDirectorySeeder</c>: that fixture is anchored to relative dates and
    /// tuned for demos, so an assertion written against it would drift. These rows are pinned.
    /// </remarks>
    private static void SeedDirectory(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (context.Set<Employee>().Any())
        {
            return;
        }

        // Must be the SAME clock the endpoint derives "current" from. Seeding from UtcNow instead
        // made both boundary cases fail for the last four or five hours of every Eastern day: the
        // UTC date is already tomorrow, so `today.AddDays(-1)` is still today in the business zone
        // and an assignment seeded as "ended yesterday" reads as current. Reuse the production type
        // rather than restating the conversion, so the seed and the endpoint cannot drift.
        var today = new CompassBusinessDate(TimeProvider.System).Today();

        context.Set<EmployeeType>().AddRange(
            new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true },
            new EmployeeType { Id = 2, TypeName = "Contractor", IsActive = true });

        context.Set<Client>().AddRange(
            new Client { Id = 1, ClientName = "Client One" },
            new Client { Id = 2, ClientName = "Client Two" });

        context.Set<Employee>().AddRange(
            Person(9, "Cody", "Coach", "OH", 1, new DateOnly(2015, 3, 1)),
            Person(1, "Ada", "Active", "OH", 1, new DateOnly(2020, 1, 15), coachId: 9),
            Person(2, "Mo", "Multi", "OH", 1, new DateOnly(2021, 2, 1), coachId: 9),
            Person(3, "Nina", "Newhire", "TX", 1, new DateOnly(2026, 5, 1), coachId: 9),
            Person(4, "Eli", "Endstoday", "TX", 2, new DateOnly(2019, 6, 1), coachId: 9),
            Person(5, "Yvonne", "Endedyesterday", "CA", 2, new DateOnly(2018, 7, 1), coachId: 9),
            Person(6, "Ned", "Nocoach", "OH", 1, new DateOnly(2017, 8, 1)),
            Person(7, "Ivor", "Inactive", "CA", 1, new DateOnly(2016, 9, 1), coachId: 9, isActive: false));

        context.Set<ClientAssignment>().AddRange(
            Assignment(1, employeeId: 1, clientId: 1, endDate: null),
            Assignment(2, employeeId: 2, clientId: 1, endDate: null),
            Assignment(3, employeeId: 2, clientId: 2, endDate: today.AddDays(30)),
            Assignment(4, employeeId: 4, clientId: 1, endDate: today),
            Assignment(5, employeeId: 5, clientId: 1, endDate: today.AddDays(-1)),
            Assignment(6, employeeId: 6, clientId: 2, endDate: null),
            Assignment(7, employeeId: 9, clientId: 1, endDate: null),
            Assignment(8, employeeId: 7, clientId: 2, endDate: today.AddDays(-10)));

        context.SaveChanges();
    }

    private static Employee Person(
        int id,
        string first,
        string last,
        string state,
        int typeId,
        DateOnly hired,
        int? coachId = null,
        bool isActive = true) =>
        new()
        {
            Id = id,
            FirstName = first,
            LastName = last,
            Email = $"{first}.{last}".ToLowerInvariant() + "@example.test",
            StateOfResidence = state,
            EmployeeTypeId = typeId,
            CoachEmployeeId = coachId,
            HireDate = hired,
            IsActive = isActive,
        };

    private static ClientAssignment Assignment(int id, int employeeId, int clientId, DateOnly? endDate) =>
        new()
        {
            Id = id,
            EmployeeId = employeeId,
            ClientId = clientId,
            StartDate = new DateOnly(2022, 1, 1),
            EndDate = endDate,
        };
}
