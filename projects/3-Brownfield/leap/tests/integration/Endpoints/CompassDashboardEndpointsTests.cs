using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.SeedData;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// The Sales Dashboard read surface against real PostgreSQL — feature 007, T018-T020, T024, T024a.
/// </summary>
/// <remarks>
/// Seeded with the full <see cref="CompassDirectorySeeder"/> rather than a bespoke fixture: it already
/// carries every case these tiles need (split allocation, no coach, two internal clients, a
/// zero-assignment EDJEr, expiring SOWs at the 45/89/90/91-day boundaries), and using the real seed
/// proves these endpoints against the same data <c>make dev-all</c> serves.
/// </remarks>
public class CompassDashboardEndpointsTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/compass/dashboard";

    private readonly IntegrationTestFactory _factory = factory;

    [Fact]
    public async Task GetDashboard_ReturnsFourCountsPlusAsOfDate()
    {
        // Arrange
        await ResetDatabaseAsync();
        var today = await SeedAsync();

        // Act
        var dto = await GetDashboardAsync();

        // Assert
        dto.AsOfDate.ShouldBe(today);
        dto.ActiveSowCount.ShouldBeGreaterThan(0);
        dto.ExpiringSowCount.ShouldBeGreaterThan(0);
        dto.ConfirmedRolloutCount.ShouldBeGreaterThan(0);
        dto.BeachCount.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("active-sows")]
    [InlineData("expiring-sows")]
    [InlineData("confirmed-rollouts")]
    [InlineData("beach")]
    public async Task GetBreakdown_EachDocumentedCategory_ReturnsRows(string category)
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync($"{Route}/breakdown/{category}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<DashboardBreakdownRowDto>>(
            TestContext.Current.CancellationToken);
        rows.ShouldNotBeNull();
        rows.ShouldNotBeEmpty($"category '{category}' must have at least one row against the full seed");
    }

    /// <summary>
    /// Issue #330 — every breakdown carries the coach's id beside their name, and that id resolves to
    /// a real record, so the dashboard's coach link cannot point at nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The integration suite is the only layer that can see this. The four breakdown queries
    /// resolve the coach through four separate projections; the unit suite runs them on EF Core
    /// InMemory, which evaluates the expression tree as ordinary LINQ and cannot tell a query that
    /// becomes SQL from one that merely compiles.
    /// </para>
    /// <para>
    /// The coached-row count is asserted non-zero BEFORE the per-row loop: a loop over zero rows
    /// passes while proving nothing, which is the fail-open shape this repository keeps re-learning.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task GetBreakdown_CarriesTheCoachsId_AndItResolvesToARealRecord()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();
        var client = _factory.AsCompassSales();
        string[] categories =
            ["active-sows", "expiring-sows", "confirmed-rollouts", "beach"];

        // Act
        List<DashboardBreakdownRowDto> allRows = [];
        foreach (var category in categories)
        {
            var rows = await client.GetFromJsonAsync<List<DashboardBreakdownRowDto>>(
                $"{Route}/breakdown/{category}", TestContext.Current.CancellationToken);
            rows.ShouldNotBeNull(category);

            // The pair is null or present together — a name without an id renders a link to nowhere,
            // an id without a name renders a link with no text.
            foreach (var row in rows)
            {
                (row.CoachId is null).ShouldBe(
                    row.CoachName is null,
                    $"{category}: employee {row.EmployeeId} has CoachName='{row.CoachName}' "
                        + $"and CoachId={row.CoachId} — the two must be null together");
            }

            allRows.AddRange(rows);
        }

        // Assert
        var coached = allRows.Where(row => row.CoachId is not null).ToList();
        coached.ShouldNotBeEmpty("the full seed must include at least one coached EDJEr, or the "
            + "per-row check above proves nothing");

        // The id is a real, reachable record — the destination the dashboard's link actually opens.
        var detail = await client.GetAsync(
            $"/api/compass/team-directory/{coached[0].CoachId}", TestContext.Current.CancellationToken);
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var coach = await detail.Content.ReadFromJsonAsync<EmployeeDetailDto>(
            TestContext.Current.CancellationToken);
        coach.ShouldNotBeNull();
        $"{coach.FirstName} {coach.LastName}".ShouldBe(
            coached[0].CoachName, "the linked record must be the coach the row named");
    }

    [Fact]
    public async Task GetBreakdown_UnrecognisedCategory_Returns404_NotASilentFallback()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync($"{Route}/breakdown/rpt-2", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CompassAdmin_IsRefused_OnBothDashboardRoutes()
    {
        // Arrange — FR-019's testable core: a Compass Admin holding no OTHER Compass role.
        await ResetDatabaseAsync();
        await SeedAsync();
        var client = _factory.AsCompassAdmin();

        // Act
        var dashboard = await client.GetAsync(Route, TestContext.Current.CancellationToken);
        var breakdown = await client.GetAsync(
            $"{Route}/breakdown/beach", TestContext.Current.CancellationToken);

        // Assert — exact status, never "not 200".
        dashboard.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        breakdown.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ T098 — role × route sweep

    /// <summary>
    /// T098 — every dashboard route (the dashboard itself and all four breakdown categories) answers
    /// 200 for each of the three permitted reporting roles (Compass Sales, Ops, Super Admin) and 403
    /// for the denied Compass Admin.
    /// </summary>
    /// <remarks>
    /// <c>CompassAdmin_IsRefused_OnBothDashboardRoutes</c> above (T020) proved the denial on two routes
    /// for one role; this sweeps all four roles across the five dashboard routes, and the report file's
    /// twin covers the other three. Together they complete SC-001 (the endpoints admit every permitted
    /// role) and SC-002 (the SERVER, not the hidden nav link, refuses Compass Admin) on every surface.
    /// The EXACT status is asserted, never "not 200": under a stale DevBypass a loose assertion passes
    /// and the gate is silently gone.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RoleRouteMatrix))]
    public async Task EveryDashboardRoute_AdmitsPermittedRoles_AndRefusesCompassAdmin(
        string roleKey, string routeSuffix, HttpStatusCode expected)
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await ClientForRole(roleKey)
            .GetAsync($"{Route}{routeSuffix}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(expected, $"{roleKey} on {Route}{routeSuffix}");
    }

    public static TheoryData<string, string, HttpStatusCode> RoleRouteMatrix()
    {
        string[] routeSuffixes =
        [
            "",
            "/breakdown/active-sows",
            "/breakdown/expiring-sows",
            "/breakdown/confirmed-rollouts",
            "/breakdown/beach",
        ];
        (string Role, HttpStatusCode Expected)[] roles =
        [
            ("Sales", HttpStatusCode.OK),
            ("Ops", HttpStatusCode.OK),
            ("SuperAdmin", HttpStatusCode.OK),
            ("Admin", HttpStatusCode.Forbidden),
        ];

        var data = new TheoryData<string, string, HttpStatusCode>();
        foreach (var suffix in routeSuffixes)
        {
            foreach (var (role, expected) in roles)
            {
                data.Add(role, suffix, expected);
            }
        }

        return data;
    }

    // ------------------------------------------------------------------ T097 — no read-audit row

    /// <summary>
    /// T097 — opening the dashboard and a breakdown writes NO audit row (FR-025, Principle VIII). A
    /// read is not an auditable event; the write surfaces that create <see cref="AuditLog"/> rows are
    /// covered elsewhere (e.g. <c>CompassAdminClientEndpointsTests</c>).
    /// </summary>
    [Fact]
    public async Task OpeningTheDashboard_WritesNoAuditRow()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();
        var before = await CountAuditLogsAsync();

        // Act — a permitted role opens both dashboard surfaces.
        (await _factory.AsCompassSales().GetAsync(Route, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _factory.AsCompassSales()
            .GetAsync($"{Route}/breakdown/beach", TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert
        var after = await CountAuditLogsAsync();
        after.ShouldBe(before, "opening a read surface must write no audit row (FR-025, Principle VIII)");
    }

    // ------------------------------------------------------------------ T024 — tile count == breakdown count

    [Theory]
    [InlineData("active-sows", nameof(SalesDashboardDto.ActiveSowCount))]
    [InlineData("expiring-sows", nameof(SalesDashboardDto.ExpiringSowCount))]
    [InlineData("confirmed-rollouts", nameof(SalesDashboardDto.ConfirmedRolloutCount))]
    [InlineData("beach", nameof(SalesDashboardDto.BeachCount))]
    public async Task EachTilesCount_EqualsItsOwnBreakdownsRowCount(string category, string countProperty)
    {
        // Arrange — SC-003's first clause, on the SAME as-of date so this is not a timing artefact.
        await ResetDatabaseAsync();
        await SeedAsync();

        var dashboard = await GetDashboardAsync();
        var breakdown = await GetBreakdownRowsAsync(category);

        // Assert
        var expected = (int)typeof(SalesDashboardDto).GetProperty(countProperty)!.GetValue(dashboard)!;
        breakdown.Count.ShouldBe(
            expected, $"the {category} tile shows {expected} but its breakdown returned {breakdown.Count} rows");
    }

    // ------------------------------------------------------------------ Sort order (soonest first)

    [Theory]
    [InlineData("expiring-sows")]
    [InlineData("confirmed-rollouts")]
    public async Task GetBreakdown_ExpiringSowsAndConfirmedRollouts_AreSortedByDaysUntilAscending(
        string category)
    {
        // Arrange — the real seed's boundary fixtures give this more than one distinct DaysUntil
        // value to actually order, and proves the ORDER BY translates against real Postgres, not just
        // LINQ-to-Objects over the InMemory provider.
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var rows = await GetBreakdownRowsAsync(category);

        // Assert
        var daysUntil = rows.Select(r => r.DaysUntil).ToList();
        daysUntil.ShouldBe(
            [.. daysUntil.OrderBy(d => d)],
            $"the {category} breakdown must list the smallest DaysUntil first");
    }

    // ------------------------------------------------------------------ Issue #329 — days available

    [Fact]
    public async Task GetBreakdown_Beach_DaysAvailable_IsTodayMinusStartDate()
    {
        // Arrange — proves the calculation translates against real Postgres, not just the InMemory
        // provider (the "project into a named type LAST" trap).
        await ResetDatabaseAsync();
        var today = await SeedAsync();

        // Act
        var rows = await GetBreakdownRowsAsync("beach");

        // Assert
        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(r => r.Date != null && r.DaysAvailable != null);
        rows.ShouldAllBe(r => r.DaysAvailable == today.DayNumber - r.Date!.Value.DayNumber);
    }

    [Theory]
    [InlineData("active-sows")]
    [InlineData("expiring-sows")]
    [InlineData("confirmed-rollouts")]
    public async Task GetBreakdown_NonBeachCategories_LeaveDaysAvailableNull(string category)
    {
        // Arrange — DaysAvailable is the beach category's own field (issue #329).
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var rows = await GetBreakdownRowsAsync(category);

        // Assert
        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(r => r.DaysAvailable == null);
    }

    // ------------------------------------------------------------------ Issue #332 — employee type

    [Theory]
    [InlineData("active-sows")]
    [InlineData("expiring-sows")]
    [InlineData("confirmed-rollouts")]
    [InlineData("beach")]
    public async Task GetBreakdown_EveryRow_CarriesTheEmployeesRealEmployeeType(string category)
    {
        // Arrange — proves the join to compass.employee_type translates against real Postgres, not
        // just the InMemory provider.
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var rows = await GetBreakdownRowsAsync(category);

        // Assert
        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(r => !string.IsNullOrWhiteSpace(r.EmployeeType));
        rows.ShouldAllBe(
            r => r.EmployeeType == "Full Time" || r.EmployeeType == "Part Time" || r.EmployeeType == "1099");
    }

    [Fact]
    public async Task GetBreakdown_ActiveSows_NamesThePartTimeAndContractorEmployeeTypesVerbatim()
    {
        // Arrange — seeded employee 6 (Willa Fontaine) is Part Time and employee 7 (Grant Ashby) is a
        // 1099 contractor, and both hold an open-ended assignment to a non-internal client (assignments
        // 6 and 7), so both land on this breakdown regardless of the SOW sitting on each (issue #633).
        // Pins the exact system-defined wording rather than just "non-empty" (owner request: "use the
        // employee types as defined in the system").
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var rows = await GetBreakdownRowsAsync("active-sows");

        // Assert
        rows.ShouldContain(r => r.EmployeeId == 6 && r.EmployeeType == "Part Time");
        rows.ShouldContain(r => r.EmployeeId == 7 && r.EmployeeType == "1099");
    }

    // ------------------------------------------------------------------ T024a — set-wise, not per-row

    [Fact]
    public async Task GetCounts_IsSetWise_SoCommandCountDoesNotGrowWithSowCount()
    {
        // Arrange — Plan v7 names the expiring-SOW-without-follow-on check as one of the two named p95
        // hotspots. Counting commands at one data size proves nothing; the invariance across two sizes
        // is the property (contract date-predicates.md).
        await ResetDatabaseAsync();
        var today = await SeedAsync();

        var baseline = await CountCommandsForGetCountsAsync(today);
        await SeedAdditionalSowsAsync(today, count: 40);
        var grown = await CountCommandsForGetCountsAsync(today);

        // Assert
        grown.Rows.ShouldBeGreaterThan(baseline.Rows, "the second measurement must really have more SOWs");
        grown.Commands.ShouldBe(
            baseline.Commands,
            $"GetCountsAsync issued {baseline.Commands} command(s) before adding SOWs and "
                + $"{grown.Commands} after. Every tile count must be derived SET-WISE — a command count "
                + "that grows with the row count is the per-row pattern FR-021 forbids.");
    }

    // ------------------------------------------------------------------ Helpers

    // ------------------------------------------------------------------ T057c — SC-011, FR-032

    [Fact]
    public async Task FutureStartAssignments_AppearOnNoTileAndInNoBreakdown()
    {
        // Arrange — seeded assignments 21 and 22 start TOMORROW (T057b): 21 to a non-internal client
        // for employee 3, 22 to the internal client for employee 5. Neither has begun, so neither is
        // an active assignment (FR-032). Asserted against real PostgreSQL because HasStarted has to
        // TRANSLATE, not merely evaluate — the InMemory provider proves nothing about that.
        await ResetDatabaseAsync();
        var today = await SeedAsync();

        // Act
        var active = await GetBreakdownRowsAsync("active-sows");
        var beach = await GetBreakdownRowsAsync("beach");
        var rollouts = await GetBreakdownRowsAsync("confirmed-rollouts");

        // Assert — employee 3's only future-dated assignment is to client 5; employee 5's is internal.
        active.ShouldNotContain(
            r => r.EmployeeId == 3 && r.Clients.Any(c => c.Id == 5),
            "assignment 21 to client 5 starts tomorrow, so it is not yet an active assignment");
        beach.ShouldNotContain(
            r => r.EmployeeId == 5,
            "assignment 22 moves employee 5 to internal allocation TOMORROW — not beach time today");
        rollouts.ShouldNotContain(
            r => r.EmployeeId == 3 && r.Clients.Any(c => c.Id == 5));
        today.ShouldBe(today);
    }

    // ------------------------------------------------------- issue #379 — internal clients ignored

    /// <summary>
    /// Issue #379 — an EDJEr whose only current assignment is to the INTERNAL client is left out of
    /// Confirmed Rollouts entirely, even though that assignment carries an end date.
    /// </summary>
    /// <remarks>
    /// Asserted against real PostgreSQL because <c>!a.Client!.IsInternal</c> has to TRANSLATE inside a
    /// <c>GroupBy</c>'s pre-filter, not merely evaluate: the InMemory provider runs the same expression
    /// tree as LINQ-to-Objects and so proves nothing about the SQL.
    /// The unit twin is
    /// <c>EmployeeWhoseOnlyEndingAssignmentIsInternal_IsNotCountedAsAConfirmedRollout</c>.
    /// </remarks>
    [Fact]
    public async Task ConfirmedRollouts_OmitAnEmployeeWhoseOnlyEndingAssignmentIsInternal()
    {
        // Arrange — seeded assignment 11 is employee 10 (Ruth Delacroix) on the internal client,
        // beginning a month ago and ending in 21 days. Every current assignment she holds carries an
        // end date, so the pre-#379 reading of FR-004's universal condition counted them; now the
        // internal assignment is ignored, they hold zero current non-internal assignments, and they
        // form no group at all.
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var dto = await GetDashboardAsync();
        var rollouts = await GetBreakdownRowsAsync("confirmed-rollouts");

        // Assert
        rollouts.ShouldNotContain(
            r => r.EmployeeId == 10,
            "assignment 11 is internal bench time, so it cannot make employee 10 a confirmed rollout");
        rollouts.ShouldNotContain(
            r => r.Clients.Any(c => c.Id == CompassDirectorySeeder.InternalClientId),
            "no confirmed-rollout row may name the internal client, whatever else the EDJEr holds");
        dto.ConfirmedRolloutCount.ShouldBe(
            rollouts.Count, "the tile's count and its breakdown's row count must stay equal (SC-003)");
    }

    /// <summary>
    /// Issue #379 — the correlated <c>MAX</c> that picks the displayed date and client excludes
    /// internal clients too, so an internal assignment ending LATER than a real one can neither set
    /// the rollout date nor become the row shown.
    /// </summary>
    /// <remarks>
    /// The subquery is the half of the fix a translation failure would hit hardest — a correlated
    /// aggregate with an added join-and-filter on the outer query's alias. Real PostgreSQL is the only
    /// thing that establishes it renders. Unit twin:
    /// <c>InternalAssignmentWithEndDate_NeverSetsTheRolloutDate_EvenWhenItsLatest</c>.
    /// </remarks>
    [Fact]
    public async Task ConfirmedRollouts_TheCorrelatedMax_IgnoresAnInternalAssignmentsLaterEndDate()
    {
        // Arrange — seeded assignment 16 is employee 15 (Lorenzo Batista) on client 6, ending in 60
        // days: a confirmed rollout. Add a CURRENT internal assignment for the same EDJEr ending in 90
        // days. Both carry end dates, so they qualify either way — what the fixture discriminates is
        // which assignment the MAX picks. Left unfiltered, the internal one wins on date and the row
        // would name the internal client 90 days out.
        await ResetDatabaseAsync();
        var today = await SeedAsync();
        await AddInternalAssignmentAsync(employeeId: 15, today.AddMonths(-1), today.AddDays(90));

        // Act
        var rollouts = await GetBreakdownRowsAsync("confirmed-rollouts");

        // Assert
        var row = rollouts.Where(r => r.EmployeeId == 15).ToList().ShouldHaveSingleItem();
        row.Date.ShouldBe(
            today.AddDays(60), "the internal assignment's later end date must never win the MAX");
        row.Clients.ShouldHaveSingleItem().Id.ShouldBe(
            6, "the row shows the real client the EDJEr is rolling off, not the internal one");
    }

    private async Task<SalesDashboardDto> GetDashboardAsync()
    {
        var response = await _factory.AsCompassSales()
            .GetAsync(Route, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"GET {Route} must be translatable to SQL");

        return await response.Content.ReadFromJsonAsync<SalesDashboardDto>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("no dashboard body");
    }

    private async Task<List<DashboardBreakdownRowDto>> GetBreakdownRowsAsync(string category)
    {
        var response = await _factory.AsCompassSales()
            .GetAsync($"{Route}/breakdown/{category}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<List<DashboardBreakdownRowDto>>(
            TestContext.Current.CancellationToken) ?? [];
    }

    private HttpClient ClientForRole(string roleKey) => roleKey switch
    {
        "Sales" => _factory.AsCompassSales(),
        "Ops" => _factory.AsCompassOps(),
        "SuperAdmin" => _factory.AsCompassSuperAdmin(),
        "Admin" => _factory.AsCompassAdmin(),
        _ => throw new ArgumentOutOfRangeException(nameof(roleKey), roleKey, "unknown Compass role key"),
    };

    private async Task<int> CountAuditLogsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        return await db.Set<AuditLog>().CountAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Seeds the full Compass directory and returns the business date it was anchored to.</summary>
    private async Task<DateOnly> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<ICompassBusinessDate>().Today();

        await CompassDirectorySeeder.SeedAsync(context, today, TestContext.Current.CancellationToken);

        return today;
    }

    /// <summary>
    /// Adds ONE current assignment to the internal client for an already-seeded EDJEr (issue #379).
    /// </summary>
    /// <remarks>
    /// A high id, well clear of every seeded range, so it disturbs no boundary fixture. No SOW: an
    /// internal assignment is contract-free, exactly as the seeder's own internal assignments are.
    /// </remarks>
    private async Task AddInternalAssignmentAsync(int employeeId, DateOnly start, DateOnly end)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<ClientAssignment>().Add(new ClientAssignment
        {
            Id = 6000,
            EmployeeId = employeeId,
            ClientId = CompassDirectorySeeder.InternalClientId,
            StartDate = start,
            EndDate = end,
            Note = "Internal allocation alongside a client engagement.",
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Adds SOWs on brand-new assignments so the total row count grows without disturbing any of the
    /// seeder's own boundary fixtures.
    /// </summary>
    private async Task SeedAdditionalSowsAsync(DateOnly today, int count)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        for (var i = 0; i < count; i++)
        {
            var assignmentId = 5000 + i;
            context.Set<ClientAssignment>().Add(new ClientAssignment
            {
                Id = assignmentId,
                EmployeeId = 2,
                ClientId = 2,
                StartDate = today.AddYears(-1),
                EndDate = null,
            });
            context.Set<Sow>().Add(new Sow
            {
                Id = 5000 + i,
                ClientAssignmentId = assignmentId,
                SowStartDate = today.AddYears(-1),
                SowEndDate = today.AddDays(30 + i),
                HasPassedApplicationValidation = true,
            });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(int Commands, int Rows)> CountCommandsForGetCountsAsync(DateOnly today)
    {
        using var scope = _factory.Services.CreateScope();
        var hostContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var counter = new CommandCountingInterceptor();
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseNpgsql(hostContext.Database.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(counter)
            .Options;

        await using var context = new LeapDbContext(options);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        var dto = await repository.GetCountsAsync(today, TestContext.Current.CancellationToken);

        return (counter.Count, dto.ExpiringSowCount);
    }

    /// <summary>Counts the SQL commands a context executes.</summary>
    private sealed class CommandCountingInterceptor : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
