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
/// The Compass report read surface against real PostgreSQL — feature 007 US2, T042, T043, T051a.
/// </summary>
/// <remarks>
/// Seeded with the full <see cref="CompassDirectorySeeder"/>, matching
/// <c>CompassDashboardEndpointsTests</c>: it already carries the no-coach EDJEr, the split allocation,
/// two internal clients and SOWs at the 45/89/90/91-day boundaries, and using the real seed proves
/// this endpoint against the same data <c>make dev-all</c> serves.
/// </remarks>
public class CompassReportEndpointsTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    private const string AvailabilityRoute = "/api/compass/reports/availability";
    private const string DashboardRoute = "/api/compass/dashboard";
    private const string DurationRoute = "/api/compass/reports/assignment-duration";
    private const string AssignmentStartRoute = "/api/compass/reports/assignment-start";
    private const string SowExtensionRoute = "/api/compass/reports/sow-extension";
    private const int AssignmentStartEmployeeTypeId = 1;

    private readonly IntegrationTestFactory _factory = factory;

    /// <summary>T042 — three sections, in order, each sorted earliest-date-first.</summary>
    /// <remarks>
    /// The DIRECTION is asserted, not merely that a sort happened. FR-009 originally said only
    /// "chronologically sorted", which two readers resolved oppositely; it now fixes earliest-first and
    /// this is the assertion that holds it. Section order is structural — three named properties on the
    /// DTO, not a list a caller could reorder — so what needs asserting is that each is populated and
    /// internally ordered.
    /// </remarks>
    [Fact]
    public async Task GetAvailability_ReturnsThreeSections_EachSortedEarliestFirst()
    {
        // Arrange
        await ResetDatabaseAsync();
        var today = await SeedAsync();

        // Act
        var report = await GetAvailabilityAsync();

        // Assert
        report.AsOfDate.ShouldBe(today);

        report.CurrentlyAvailable.ShouldNotBeEmpty("the seed puts EDJErs on the beach");
        report.ConfirmedRollouts.ShouldNotBeEmpty("the seed has end-dated assignments");
        report.UnconfirmedSows.ShouldNotBeEmpty("the seed has SOWs expiring inside 90 days");

        ShouldBeAscendingNullsLast(
            report.CurrentlyAvailable.Select(r => r.InternalAssignmentStartDate),
            "§1 orders on the internal-assignment start date, earliest first (FR-009)");

        ShouldBeAscendingNullsLast(
            report.ConfirmedRollouts.Select(r => r.DaysUntilRollout),
            "§2 orders on the assignment end date, earliest first (FR-009)");

        ShouldBeAscendingNullsLast(
            report.UnconfirmedSows.Select(r => r.DaysUntilExpiration),
            "§3 orders on the SOW end date, earliest first (FR-009)");
    }

    /// <summary>
    /// T057c — the US2 inheritance proof, and the reason Phase 4a touches no US2 production code.
    /// </summary>
    /// <remarks>
    /// <c>CompassReportReadService</c> issues NO assignment query: all three sections come from three
    /// <c>GetBreakdownAsync</c> calls, so fixing the repository fixed the sections by construction. If
    /// this test ever needs an edit to that service to pass, the projection has started querying
    /// assignments itself — and that is the defect T049's one-derivation instruction exists to prevent.
    /// Seeded assignment 22 moves employee 5 to internal allocation TOMORROW (T057b); FR-032 says that
    /// is not an active assignment, so they are not currently available today.
    /// </remarks>
    [Fact]
    public async Task Section1_ExcludesAnEdjerWhoseInternalAssignmentStartsTomorrow()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var report = await GetAvailabilityAsync();

        // Assert
        report.CurrentlyAvailable.ShouldNotContain(
            row => row.EmployeeId == 5,
            "employee 5's internal assignment starts tomorrow — a future start is not an active "
                + "assignment (FR-032), so it does not put them on the beach today");
        report.CurrentlyAvailable.ShouldNotBeEmpty(
            "the genuinely-on-the-beach EDJErs must still be there — this is not an empty-set pass");
    }

    /// <summary>T043 — §1's row count equals the beach tile; §2's equals the confirmed-rollouts tile.</summary>
    /// <remarks>
    /// SC-003's second and third clauses. Both are one population rendered two ways (FR-005/FR-010,
    /// FR-004/FR-011), so a discrepancy is a defect rather than a timing artefact — which is why both
    /// are read for the SAME as-of date and that date is asserted equal across the two responses first.
    /// The report projects the dashboard's own breakdowns, so this holds by construction today; the test
    /// exists to fail loudly if a later change gives the report queries of its own.
    /// </remarks>
    [Fact]
    public async Task AvailabilitySections_RowCounts_EqualTheirDashboardTiles()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var report = await GetAvailabilityAsync();
        var dashboard = await GetDashboardAsync();

        // Assert
        report.AsOfDate.ShouldBe(dashboard.AsOfDate, "both surfaces must be read against one business date");

        report.CurrentlyAvailable.Count.ShouldBe(
            dashboard.BeachCount,
            "§1 and the beach tile are one population (FR-005, FR-010) — the tile shows its count, the "
                + "report its detail");

        report.ConfirmedRollouts.Count.ShouldBe(
            dashboard.ConfirmedRolloutCount,
            "§2 and the confirmed-rollouts tile are one population (FR-004, FR-011); §2 is one row per "
                + "EDJEr and the tile counts distinct EDJErs");
    }

    /// <summary>T051a — a Compass Admin with no other Compass role is refused BY THE SERVER.</summary>
    /// <remarks>
    /// Asserts the EXACT status, never "not 200": under a stale DevBypass a loose assertion passes and
    /// the gate is silently gone (US1's T039 hit this for real).
    /// T020 covers the two dashboard routes; this covers the report route, and T098 sweeps all five.
    /// </remarks>
    [Fact]
    public async Task CompassAdmin_IsRefused_OnTheAvailabilityRoute()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(AvailabilityRoute, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            "hiding the nav link is not enforcement (FR-019) — Compass Admin is not a reporting role");
    }

    /// <summary>A permitted role reaches it, so the test above proves a ROLE check and not a 404.</summary>
    [Fact]
    public async Task CompassSales_ReachesTheAvailabilityRoute()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync(AvailabilityRoute, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>T042 — an unseeded database answers 200 with three empty sections, not an error.</summary>
    /// <remarks>
    /// Every new endpoint group requires "GET all (empty + with data)" coverage, and empty is not a
    /// cosmetic edge on this endpoint: it is the only state that
    /// exercises the three empty projections at once, and the first thing a fresh preview namespace or a
    /// tenant with no assignments sees. Every other test here seeds the full directory.
    /// </remarks>
    [Fact]
    public async Task GetAvailability_AgainstAnEmptyDatabase_Returns200WithThreeEmptySections()
    {
        // Arrange — deliberately NO SeedAsync().
        await ResetDatabaseAsync();

        // Act
        var report = await GetAvailabilityAsync();

        // Assert
        report.CurrentlyAvailable.ShouldBeEmpty();
        report.ConfirmedRollouts.ShouldBeEmpty();
        report.UnconfirmedSows.ShouldBeEmpty();
        report.AsOfDate.ShouldNotBe(default, "the as-of date is resolved even with nothing to report");
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>
    /// Asserts a projected sort key is ascending with any nulls LAST — Postgres's own default for
    /// <c>ORDER BY … ASC</c>.
    /// </summary>
    /// <remarks>
    /// Not <c>.Order()</c>. LINQ's default comparer sorts null FIRST for a nullable type, so an
    /// oracle built from <c>.Order()</c> contradicts the database for exactly the nullable columns these
    /// DTOs declare (§1's date, §2's and §3's day counts). Today none of them is ever null, so such a
    /// test passes — and the moment one is, it fails while the server is behaving correctly, sending the
    /// reader to debug the query instead of the assertion.
    /// </remarks>
    private static void ShouldBeAscendingNullsLast<T>(IEnumerable<T?> keys, string because)
        where T : struct, IComparable<T>
    {
        var ordered = keys.ToList();
        var present = ordered.Where(key => key.HasValue).Select(key => key!.Value).ToList();

        // Pairwise rather than ShouldBe(present.Order()): Shouldly cannot resolve its enumerable
        // overload against an open generic struct list, and the pairwise form states the property
        // ("never decreases") without building a second sequence to compare against.
        present.Zip(present.Skip(1)).ShouldAllBe(
            pair => pair.First.CompareTo(pair.Second) <= 0, because);

        var firstNull = ordered.FindIndex(key => !key.HasValue);
        if (firstNull >= 0)
        {
            ordered.Skip(firstNull).ShouldAllBe(
                key => !key.HasValue,
                $"{because} — and Postgres puts NULLs last, so no value may follow one");
        }
    }

    // ------------------------------------------------------------------ T058 / T061a — US3 duration

    /// <summary>
    /// T058 — the duration report against real PostgreSQL, ordered longest-first.
    /// </summary>
    /// <remarks>
    /// The point of this test is that the query TRANSLATES. The span arithmetic is a conditional
    /// over two dates plus day-number subtraction, and the InMemory provider evaluates all of that in
    /// .NET — <c>CompassReportRepositoryTests</c> would stay green while this route answered HTTP 500.
    /// That is the one class of repository defect the
    /// integration suite is not redundant coverage for.
    /// </remarks>
    [Fact]
    public async Task GetAssignmentDuration_ReturnsRowsOrderedByTotalDaysDescending()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var rows = await GetAssignmentDurationAsync();

        // Assert
        rows.ShouldNotBeEmpty("the seed holds active assignments");
        rows.Select(r => r.TotalDays).ShouldBeInOrder(SortDirection.Descending);
        rows.ShouldAllBe(r => r.TotalDays > 0);
        rows.ShouldAllBe(r => r.DurationDisplay.Length > 0);
    }

    /// <summary>T059 — the seeded left-and-returned pair is ONE row carrying the combined tenure.</summary>
    [Fact]
    public async Task GetAssignmentDuration_TheLeftAndReturnedPair_IsOneRowSummingBothAssignments()
    {
        // Arrange — seeded assignments 13 and 20 are both employee 12 to client 4 (T057b/T016): an
        // earlier closed engagement and the current one, with a four-month gap.
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var rows = await GetAssignmentDurationAsync();

        // Assert
        var pair = rows.Where(r => r.EmployeeId == 12 && r.ClientId == 4).ToList();
        var row = pair.ShouldHaveSingleItem();
        row.TotalDays.ShouldBeGreaterThan(
            420,
            "the combined tenure spans roughly six months closed plus fourteen months running; the "
                + "CURRENT assignment alone is about 425 days, so a sum that ignored the earlier leg "
                + "would land near it — this bound is above the current-only figure");
    }

    /// <summary>T059a — a future-dated start does not qualify a pair (FR-032, SC-011).</summary>
    [Fact]
    public async Task GetAssignmentDuration_ExcludesAPairWhoseAssignmentStartsTomorrow()
    {
        // Arrange — seeded assignment 21 is employee 3 to client 5, starting tomorrow.
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var rows = await GetAssignmentDurationAsync();

        // Assert
        rows.ShouldNotContain(
            r => r.EmployeeId == 3 && r.ClientId == 5,
            "a future start is not an active assignment, so the pair does not qualify (FR-032)");
    }

    /// <summary>
    /// Issue #335 — the report is scoped to external client assignments only. The seed holds several
    /// active assignments at <see cref="CompassDirectorySeeder.InternalClientId"/> (employees 1, 2, 9
    /// and 10); none of them may surface here.
    /// </summary>
    [Fact]
    public async Task GetAssignmentDuration_ExcludesInternalClientPairs()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var rows = await GetAssignmentDurationAsync();

        // Assert
        rows.ShouldNotBeEmpty("the seed holds active EXTERNAL assignments too");
        rows.ShouldNotContain(
            r => r.ClientId == CompassDirectorySeeder.InternalClientId,
            "internal (beach) clients are out of scope for this report (issue #335)");
    }

    /// <summary>
    /// Issue #335 — every row carries the EDJEr's employee type, for the client to render next to
    /// their name. Every seeded EDJEr has one, so a missing value here would be a translation defect,
    /// not a legitimately-absent one.
    /// </summary>
    [Fact]
    public async Task GetAssignmentDuration_PopulatesEmployeeTypeOnEveryRow()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var rows = await GetAssignmentDurationAsync();

        // Assert
        rows.ShouldNotBeEmpty("the seed holds active external assignments");
        rows.ShouldAllBe(r => !string.IsNullOrEmpty(r.EmployeeType));
    }

    /// <summary>
    /// T061a — the duration aggregation is set-wise: the command count is INVARIANT across data sizes.
    /// </summary>
    /// <remarks>
    /// Counting at one size proves nothing — the invariance is the property (FR-021). This is the
    /// second of Plan v7's two named p95 hotspots; T024a is the first.
    /// </remarks>
    [Fact]
    public async Task GetAssignmentDuration_IssuesTheSameNumberOfCommands_AtTwoDataSizes()
    {
        // Arrange
        await ResetDatabaseAsync();
        var today = await SeedAsync();
        var small = await CountCommandsForDurationAsync(today);

        await SeedAdditionalAssignmentsAsync(today, count: 40);
        var large = await CountCommandsForDurationAsync(today);

        // Assert
        large.Rows.ShouldBeGreaterThan(
            small.Rows, "the second run must actually see more rows, or invariance is vacuous");
        large.Commands.ShouldBe(
            small.Commands,
            $"the duration aggregation must be set-based: {small.Commands} commands over "
                + $"{small.Rows} rows but {large.Commands} over {large.Rows} means a per-row pattern");
    }

    private async Task<List<AssignmentDurationRowDto>> GetAssignmentDurationAsync()
    {
        // AsCompassSales(), not the base Client: that one carries EDJEr,Manager — timesheet roles,
        // which do NOT satisfy CompassReporting (Principle IV: no module inherits another's roles).
        // Using it here returned 403 and read as a translation failure until the status was inspected.
        var response = await _factory.AsCompassSales()
            .GetAsync(DurationRoute, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK, $"GET {DurationRoute} must be translatable to SQL");

        return await response.Content.ReadFromJsonAsync<List<AssignmentDurationRowDto>>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("no duration body");
    }

    /// <summary>
    /// Adds assignments on NEW EDJEr–client pairs so the duration query sees more groups without
    /// disturbing any seeded boundary fixture.
    /// </summary>
    private async Task SeedAdditionalAssignmentsAsync(DateOnly today, int count)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employeeIds = await context.Set<Employee>()
            .AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.Id)
            .Select(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        var clientIds = await context.Set<Client>()
            .AsNoTracking()
            .Where(c => !c.IsInternal)
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        var nextId = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .MaxAsync(a => a.Id, TestContext.Current.CancellationToken) + 1;

        for (var i = 0; i < count; i++)
        {
            context.Set<ClientAssignment>().Add(new ClientAssignment
            {
                Id = nextId + i,
                EmployeeId = employeeIds[i % employeeIds.Count],
                ClientId = clientIds[(i / employeeIds.Count) % clientIds.Count],
                StartDate = today.AddDays(-300 - i),
                EndDate = null,
            });
        }

        // Explicit ids, so no sequence resync is needed — the same shape as the dashboard class's
        // SeedAdditionalSowsAsync.
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(int Commands, int Rows)> CountCommandsForDurationAsync(DateOnly today)
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
        var repository = new CompassReportRepository(context, new ClientStatusDerivation());

        var rows = await repository.GetAssignmentDurationAsync(
            today, TestContext.Current.CancellationToken);

        return (counter.Count, rows.Count);
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

    private async Task<AvailabilityReportDto> GetAvailabilityAsync()
    {
        var response = await _factory.AsCompassSales()
            .GetAsync(AvailabilityRoute, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK, $"GET {AvailabilityRoute} must be translatable to SQL");

        return await response.Content.ReadFromJsonAsync<AvailabilityReportDto>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("no availability body");
    }

    private async Task<SalesDashboardDto> GetDashboardAsync()
    {
        var response = await _factory.AsCompassSales()
            .GetAsync(DashboardRoute, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<SalesDashboardDto>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("no dashboard body");
    }


    // ---------------------------------------------------------------- US4 / AC-40 — Assignment Start lookup

    /// <summary>T071 — a range covering a known start date returns that assignment, INCLUSIVE at both ends.</summary>
    /// <remarks>
    /// The two boundary rows are the point. A half-open range is the easy mistake here: <c>startDate &gt;= from
    /// &amp;&amp; startDate &lt; to</c> reads naturally and silently drops every assignment that started on the
    /// last day of the range the user asked for. Contract <c>reports-read-surface.md</c> fixes it as
    /// inclusive at both ends, "consistent with every other boundary in this feature", so both edges are
    /// asserted rather than a comfortable midpoint.
    /// </remarks>
    [Fact]
    public async Task GetAssignmentStart_ReturnsAssignmentsStartingInRange_InclusiveAtBothEnds()
    {
        // Arrange
        await ResetDatabaseAsync();
        var onFrom = await SeedAssignmentStartingAsync("OnFrom", "Acme", new DateOnly(2026, 3, 1));
        var inside = await SeedAssignmentStartingAsync("Inside", "Globex", new DateOnly(2026, 3, 15));
        var onTo = await SeedAssignmentStartingAsync("OnTo", "Initech", new DateOnly(2026, 3, 31));
        await SeedAssignmentStartingAsync("DayBefore", "TooEarly", new DateOnly(2026, 2, 28));
        await SeedAssignmentStartingAsync("DayAfter", "TooLate", new DateOnly(2026, 4, 1));

        // Act
        var rows = await GetAssignmentStartsAsync("2026-03-01", "2026-03-31");

        // Assert
        var names = rows.Select(r => r.EmployeeName).ToList();
        names.ShouldContain(onFrom.EmployeeName);
        names.ShouldContain(inside.EmployeeName);
        names.ShouldContain(onTo.EmployeeName);
        names.ShouldNotContain(n => n.StartsWith("DayBefore", StringComparison.Ordinal));
        names.ShouldNotContain(n => n.StartsWith("DayAfter", StringComparison.Ordinal));

        var row = rows.Single(r => r.EmployeeName == inside.EmployeeName);
        row.ClientName.ShouldBe(inside.ClientName);
        row.StartDate.ShouldBe(new DateOnly(2026, 3, 15));
    }

    /// <summary>T071 — the row carries all four contract columns, populated (issue #386 added employee type).</summary>
    [Fact]
    public async Task GetAssignmentStart_EachRow_CarriesEdjerEmployeeTypeClientAndStartDate()
    {
        // Arrange
        await ResetDatabaseAsync();
        var seeded = await SeedAssignmentStartingAsync("Ada", "BuckeyeMutual", new DateOnly(2026, 5, 4));

        // Act
        var rows = await GetAssignmentStartsAsync("2026-05-01", "2026-05-31");

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.EmployeeName.ShouldBe(seeded.EmployeeName);
        // SeedAssignmentStartingAsync seeds the one EmployeeType as "Full Time".
        row.EmployeeType.ShouldBe("Full Time");
        row.ClientName.ShouldBe(seeded.ClientName);
        row.StartDate.ShouldBe(new DateOnly(2026, 5, 4));
    }

    /// <summary>T072 — <c>from</c> after <c>to</c> is a 400 with a message, not a silent swap and not empty.</summary>
    /// <remarks>
    /// Spec US4 scenario 2: the user has made an error and should be told. An empty list would be
    /// indistinguishable from "nothing started in that range", which is the wrong thing to tell them, and
    /// swapping the bounds silently answers a question they did not ask.
    /// </remarks>
    [Fact]
    public async Task GetAssignmentStart_FromAfterTo_IsRefusedWithAMessage()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAssignmentStartingAsync("Ada", "BuckeyeMutual", new DateOnly(2026, 3, 15));

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync($"{AssignmentStartRoute}?from=2026-03-31&to=2026-03-01", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotBeNullOrWhiteSpace("the refusal has to say what is wrong, not merely fail");
    }

    /// <summary>T072 — an equal <c>from</c>/<c>to</c> is a VALID single-day range, not the refusal above.</summary>
    /// <remarks>
    /// The guard is <c>from &gt; to</c>, never <c>&gt;=</c>. A one-day lookup is a legitimate question and
    /// is the most likely off-by-one in the validation itself.
    /// </remarks>
    [Fact]
    public async Task GetAssignmentStart_FromEqualsTo_IsAValidSingleDayRange()
    {
        // Arrange
        await ResetDatabaseAsync();
        var seeded = await SeedAssignmentStartingAsync("Ada", "BuckeyeMutual", new DateOnly(2026, 3, 15));

        // Act
        var rows = await GetAssignmentStartsAsync("2026-03-15", "2026-03-15");

        // Assert
        rows.ShouldHaveSingleItem().EmployeeName.ShouldBe(seeded.EmployeeName);
    }

    /// <summary>T072 — a missing parameter is a 400.</summary>
    /// <remarks>
    /// This is a BINDING failure, and the task warns it does not answer the same way everywhere:
    /// <c>RouteHandlerOptions.ThrowOnBadRequest</c> defaults true under Development and false elsewhere, so
    /// an unpinned app answers 500 against a local dev API while this very test passes. PR #259 pinned it
    /// false application-wide, which is what makes the 400 asserted here the answer a browser also gets.
    /// </remarks>
    [Theory]
    [InlineData("?to=2026-03-31")]
    [InlineData("?from=2026-03-01")]
    [InlineData("")]
    public async Task GetAssignmentStart_MissingAParameter_IsRefused(string query)
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync($"{AssignmentStartRoute}{query}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>T073 — a range matching nothing is 200 with an empty list, not a 404 and not an error.</summary>
    [Fact]
    public async Task GetAssignmentStart_RangeMatchingNothing_IsAnEmptyListNotAnError()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAssignmentStartingAsync("Ada", "BuckeyeMutual", new DateOnly(2026, 3, 15));

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync($"{AssignmentStartRoute}?from=2030-01-01&to=2030-12-31", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<AssignmentStartRowDto>>(
            TestContext.Current.CancellationToken);
        rows.ShouldNotBeNull();
        rows.ShouldBeEmpty();
    }

    /// <summary>FR-018/FR-019 — a Compass Admin holding no other Compass role is refused AT THE SERVER.</summary>
    /// <remarks>
    /// The EXACT status is asserted rather than "not 200": under a stale DevBypass a loose assertion passes
    /// and the gate is silently gone. Hiding the tab is not enforcement.
    /// </remarks>
    [Fact]
    public async Task GetAssignmentStart_CompassAdmin_IsRefused()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync($"{AssignmentStartRoute}?from=2026-03-01&to=2026-03-31", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>FR-018 — Compass Ops reaches it, so the policy is not merely refusing everyone.</summary>
    [Fact]
    public async Task GetAssignmentStart_CompassOps_IsAllowed()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await _factory.AsCompassOps()
            .GetAsync($"{AssignmentStartRoute}?from=2026-03-01&to=2026-03-31", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<List<AssignmentStartRowDto>> GetAssignmentStartsAsync(string from, string to)
    {
        var response = await _factory.AsCompassSales()
            .GetAsync($"{AssignmentStartRoute}?from={from}&to={to}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<List<AssignmentStartRowDto>>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("no assignment-start body");
    }

    // ------------------------------------------------------------------ SOW Extension Report (issue #534)

    /// <summary>A range covering a known extension start date returns it, INCLUSIVE at both ends.</summary>
    [Fact]
    public async Task GetSowExtension_ReturnsExtensionsStartingInRange_InclusiveAtBothEnds()
    {
        // Arrange
        await ResetDatabaseAsync();
        var onFrom = await SeedSowExtensionAsync("OnFrom", "Acme", new DateOnly(2026, 3, 1));
        var inside = await SeedSowExtensionAsync("Inside", "Globex", new DateOnly(2026, 3, 15));
        var onTo = await SeedSowExtensionAsync("OnTo", "Initech", new DateOnly(2026, 3, 31));
        await SeedSowExtensionAsync("DayBefore", "TooEarly", new DateOnly(2026, 2, 28));
        await SeedSowExtensionAsync("DayAfter", "TooLate", new DateOnly(2026, 4, 1));

        // Act
        var rows = await GetSowExtensionsAsync("2026-03-01", "2026-03-31");

        // Assert
        var names = rows.Select(r => r.EmployeeName).ToList();
        names.ShouldContain(onFrom.EmployeeName);
        names.ShouldContain(inside.EmployeeName);
        names.ShouldContain(onTo.EmployeeName);
        names.ShouldNotContain(n => n.StartsWith("DayBefore", StringComparison.Ordinal));
        names.ShouldNotContain(n => n.StartsWith("DayAfter", StringComparison.Ordinal));

        var row = rows.Single(r => r.EmployeeName == inside.EmployeeName);
        row.ClientName.ShouldBe(inside.ClientName);
        row.ExtensionStartDate.ShouldBe(new DateOnly(2026, 3, 15));
    }

    /// <summary>Only extension SOWs qualify — an initial-contract period in the same range is excluded.</summary>
    [Fact]
    public async Task GetSowExtension_ExcludesInitialContractPeriods()
    {
        // Arrange
        await ResetDatabaseAsync();
        var extension = await SeedSowExtensionAsync("Ada", "BuckeyeMutual", new DateOnly(2026, 3, 15));
        await SeedSowAsync(
            "Zoe", "Initech", new DateOnly(2026, 3, 20), SowType.InitialContract);

        // Act
        var rows = await GetSowExtensionsAsync("2026-01-01", "2026-12-31");

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.EmployeeName.ShouldBe(extension.EmployeeName);
    }

    /// <summary>The row carries all four contract columns, populated.</summary>
    [Fact]
    public async Task GetSowExtension_EachRow_CarriesEdjerEmployeeTypeClientAndExtensionStartDate()
    {
        // Arrange
        await ResetDatabaseAsync();
        var seeded = await SeedSowExtensionAsync("Ada", "BuckeyeMutual", new DateOnly(2026, 5, 4));

        // Act
        var rows = await GetSowExtensionsAsync("2026-05-01", "2026-05-31");

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.EmployeeName.ShouldBe(seeded.EmployeeName);
        row.EmployeeType.ShouldBe("Full Time");
        row.ClientName.ShouldBe(seeded.ClientName);
        row.ExtensionStartDate.ShouldBe(new DateOnly(2026, 5, 4));
    }

    /// <summary><c>from</c> after <c>to</c> is a 400 with a message, not a silent swap and not empty.</summary>
    [Fact]
    public async Task GetSowExtension_FromAfterTo_IsRefusedWithAMessage()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedSowExtensionAsync("Ada", "BuckeyeMutual", new DateOnly(2026, 3, 15));

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync($"{SowExtensionRoute}?from=2026-03-31&to=2026-03-01", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotBeNullOrWhiteSpace("the refusal has to say what is wrong, not merely fail");
    }

    /// <summary>An equal <c>from</c>/<c>to</c> is a VALID single-day range, not the refusal above.</summary>
    [Fact]
    public async Task GetSowExtension_FromEqualsTo_IsAValidSingleDayRange()
    {
        // Arrange
        await ResetDatabaseAsync();
        var seeded = await SeedSowExtensionAsync("Ada", "BuckeyeMutual", new DateOnly(2026, 3, 15));

        // Act
        var rows = await GetSowExtensionsAsync("2026-03-15", "2026-03-15");

        // Assert
        rows.ShouldHaveSingleItem().EmployeeName.ShouldBe(seeded.EmployeeName);
    }

    /// <summary>A missing parameter is a 400.</summary>
    [Theory]
    [InlineData("?to=2026-03-31")]
    [InlineData("?from=2026-03-01")]
    [InlineData("")]
    public async Task GetSowExtension_MissingAParameter_IsRefused(string query)
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync($"{SowExtensionRoute}{query}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>A range matching nothing is 200 with an empty list, not a 404 and not an error.</summary>
    [Fact]
    public async Task GetSowExtension_RangeMatchingNothing_IsAnEmptyListNotAnError()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedSowExtensionAsync("Ada", "BuckeyeMutual", new DateOnly(2026, 3, 15));

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync($"{SowExtensionRoute}?from=2030-01-01&to=2030-12-31", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<SowExtensionRowDto>>(
            TestContext.Current.CancellationToken);
        rows.ShouldNotBeNull();
        rows.ShouldBeEmpty();
    }

    /// <summary>A Compass Admin holding no other Compass role is refused AT THE SERVER.</summary>
    [Fact]
    public async Task GetSowExtension_CompassAdmin_IsRefused()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync($"{SowExtensionRoute}?from=2026-03-01&to=2026-03-31", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Compass Ops reaches it, so the policy is not merely refusing everyone.</summary>
    [Fact]
    public async Task GetSowExtension_CompassOps_IsAllowed()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await _factory.AsCompassOps()
            .GetAsync($"{SowExtensionRoute}?from=2026-03-01&to=2026-03-31", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<List<SowExtensionRowDto>> GetSowExtensionsAsync(string from, string to)
    {
        var response = await _factory.AsCompassSales()
            .GetAsync($"{SowExtensionRoute}?from={from}&to={to}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<List<SowExtensionRowDto>>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("no sow-extension body");
    }

    /// <summary>
    /// Seeds one EDJEr, one client, one assignment and one EXTENSION SOW starting on
    /// <paramref name="startDate"/>, returning the names to assert against.
    /// </summary>
    private Task<(string EmployeeName, string ClientName)> SeedSowExtensionAsync(
        string firstName, string clientName, DateOnly startDate) =>
        SeedSowAsync(firstName, clientName, startDate, SowType.SowExtension);

    /// <summary>
    /// Seeds one EDJEr, one client, one assignment and one SOW of the given <paramref name="sowType"/>
    /// starting on <paramref name="startDate"/>. Deliberately NOT the full <see cref="CompassDirectorySeeder"/>
    /// the availability tests use — that seeder anchors its dates to the business date, so a boundary
    /// assertion written against it would assert arithmetic rather than the range filter (matching
    /// <c>SeedAssignmentStartingAsync</c>'s own reasoning).
    /// </summary>
    private async Task<(string EmployeeName, string ClientName)> SeedSowAsync(
        string firstName, string clientName, DateOnly startDate, SowType sowType)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var token = TestContext.Current.CancellationToken;

        if (!await db.Set<EmployeeType>().AnyAsync(token))
        {
            db.Set<EmployeeType>().Add(new EmployeeType
            {
                Id = AssignmentStartEmployeeTypeId,
                TypeName = "Full Time",
                IsActive = true,
            });
            await db.SaveChangesAsync(token);
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var employee = new Employee
        {
            FirstName = $"{firstName}-{suffix}",
            LastName = "Test",
            Email = $"{firstName}.{suffix}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = AssignmentStartEmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
        };
        var client = new Client { ClientName = $"{clientName}-{suffix}", IsInternal = false };
        db.Set<Employee>().Add(employee);
        db.Set<Client>().Add(client);
        await db.SaveChangesAsync(token);

        var assignment = new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = startDate.AddYears(-1),
            EndDate = null,
        };
        db.Set<ClientAssignment>().Add(assignment);
        await db.SaveChangesAsync(token);

        db.Set<Sow>().Add(new Sow
        {
            ClientAssignmentId = assignment.Id,
            SowType = sowType,
            SowStartDate = startDate,
            SowEndDate = startDate.AddMonths(6),
            HasPassedApplicationValidation = true,
        });
        await db.SaveChangesAsync(token);

        return ($"{employee.FirstName} {employee.LastName}", client.ClientName);
    }

    /// <summary>
    /// Seeds one EDJEr, one client and one assignment starting on <paramref name="startDate"/>, returning
    /// the names to assert against.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT the full <see cref="CompassDirectorySeeder"/> the availability tests use. That
    /// seeder anchors its dates to the business date, so a boundary assertion written against it would be
    /// asserting arithmetic rather than the range filter, and would change meaning as the calendar moves.
    /// These rows carry fixed, unambiguous dates.
    /// </remarks>
    private async Task<(string EmployeeName, string ClientName)> SeedAssignmentStartingAsync(
        string firstName, string clientName, DateOnly startDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var token = TestContext.Current.CancellationToken;

        if (!await db.Set<EmployeeType>().AnyAsync(token))
        {
            db.Set<EmployeeType>().Add(new EmployeeType
            {
                Id = AssignmentStartEmployeeTypeId,
                TypeName = "Full Time",
                IsActive = true,
            });
            await db.SaveChangesAsync(token);
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var employee = new Employee
        {
            FirstName = $"{firstName}-{suffix}",
            LastName = "Test",
            Email = $"{firstName}.{suffix}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = AssignmentStartEmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
        };
        var client = new Client { ClientName = $"{clientName}-{suffix}", IsInternal = false };
        db.Set<Employee>().Add(employee);
        db.Set<Client>().Add(client);
        await db.SaveChangesAsync(token);

        db.Set<ClientAssignment>().Add(new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = startDate,
            EndDate = null,
        });
        await db.SaveChangesAsync(token);

        return ($"{employee.FirstName} {employee.LastName}", client.ClientName);
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

    // ------------------------------------------------------------------ T098 — role × route sweep

    /// <summary>
    /// T098 — each of the report routes answers 200 for the three permitted reporting roles
    /// (Compass Sales, Ops, Super Admin) and 403 for the denied Compass Admin.
    /// </summary>
    /// <remarks>
    /// T051a above proved the denial on one report route for one role; this sweeps all four roles
    /// across every report route (extended for issue #534's SOW Extension Report), and the dashboard
    /// file's twin covers the other two. Together they complete SC-001 across every surface and
    /// SC-002's server-side refusal of Compass Admin. The assignment-start and sow-extension routes
    /// carry a valid wide range so a permitted role's answer is a genuine 200, not a 400 from the range
    /// guard. The EXACT status is asserted, never "not 200".
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReportRoleRouteMatrix))]
    public async Task EveryReportRoute_AdmitsPermittedRoles_AndRefusesCompassAdmin(
        string roleKey, string route, HttpStatusCode expected)
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await ClientForRole(roleKey).GetAsync(route, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(expected, $"{roleKey} on {route}");
    }

    public static TheoryData<string, string, HttpStatusCode> ReportRoleRouteMatrix()
    {
        string[] routes =
        [
            AvailabilityRoute,
            DurationRoute,
            // A valid wide range: any permitted role must get a 200, not a range-guard 400.
            $"{AssignmentStartRoute}?from=2000-01-01&to=2100-01-01",
            $"{SowExtensionRoute}?from=2000-01-01&to=2100-01-01",
        ];
        (string Role, HttpStatusCode Expected)[] roles =
        [
            ("Sales", HttpStatusCode.OK),
            ("Ops", HttpStatusCode.OK),
            ("SuperAdmin", HttpStatusCode.OK),
            ("Admin", HttpStatusCode.Forbidden),
        ];

        var data = new TheoryData<string, string, HttpStatusCode>();
        foreach (var route in routes)
        {
            foreach (var (role, expected) in roles)
            {
                data.Add(role, route, expected);
            }
        }

        return data;
    }

    // ------------------------------------------------------------------ T097 — no read-audit row

    /// <summary>
    /// T097 — opening a report writes NO audit row (FR-025, Principle VIII). A read is not an
    /// auditable event; the write surfaces that create <see cref="AuditLog"/> rows are covered
    /// elsewhere.
    /// </summary>
    [Fact]
    public async Task OpeningAReport_WritesNoAuditRow()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();
        var before = await CountAuditLogsAsync();

        // Act — a permitted role opens every report.
        var client = _factory.AsCompassSales();
        (await client.GetAsync(AvailabilityRoute, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync(DurationRoute, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync(
            $"{AssignmentStartRoute}?from=2000-01-01&to=2100-01-01", TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync(
            $"{SowExtensionRoute}?from=2000-01-01&to=2100-01-01", TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert
        var after = await CountAuditLogsAsync();
        after.ShouldBe(before, "opening a read surface must write no audit row (FR-025, Principle VIII)");
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
}
