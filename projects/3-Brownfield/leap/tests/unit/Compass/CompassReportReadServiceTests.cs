using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// <see cref="CompassReportReadService"/> — feature 007 US2, the Availability Report (AC-38, FR-009
/// to FR-013).
/// </summary>
/// <remarks>
/// <para>
/// What this file can establish, and what it deliberately does not. The service resolves the
/// business date ONCE and projects the three sections from
/// <see cref="ICompassDashboardRepository"/>'s existing breakdowns — it owns the business-date seam
/// and the mapping, and nothing else. The three POPULATIONS are the dashboard's, by construction
/// (FR-005/FR-010, FR-004/FR-011, FR-003/FR-012 each require one derivation), so their data rules are
/// asserted where the queries live: <c>CompassDashboardRepositoryTests</c>. Asserting them here over a
/// fake repository would assert the fake.
/// </para>
/// <para>
/// Why the split is spelled out rather than assumed. US1 shipped a 300-line
/// <c>CompassDashboardReadServiceTests</c> in which every test constructed a repository, so the
/// service had 0% unit coverage behind a file bearing its name — worse than no file, because it read
/// as covered. That file is now <c>CompassDashboardRepositoryTests</c>. Tasks T044, T044a, T045 and
/// T046 name this file for four data rules; three of them are repository behaviour and are asserted
/// there for exactly that reason. T046's no-coach rule is asserted in BOTH places: the repository
/// proves a null coach survives the query, and the mapping below proves the projection does not drop
/// it on the way into the report's own row types.
/// </para>
/// <para>
/// Hand-rolled doubles. The repository double records what it was
/// asked, because "one business date across all three sections" is a claim about ARGUMENTS — asserting
/// only on the returned value would pass just as happily if the service resolved a date per section.
/// </para>
/// </remarks>
public class CompassReportReadServiceTests
{
    private static readonly DateOnly BusinessToday = new(2026, 8, 17);

    /// <summary>A business date that is NOT the system date, so a service reading the clock fails.</summary>
    private sealed class FixedBusinessDate(DateOnly today) : ICompassBusinessDate
    {
        public int Calls { get; private set; }

        public DateOnly Today()
        {
            Calls++;
            return today;
        }
    }

    // ---------------------------------------------------------------- US4 / AC-40 — Assignment Start

    /// <summary>
    /// The lookup passes its range to the repository UNCHANGED and returns what comes back.
    /// </summary>
    /// <remarks>
    /// The service is a straight delegation, so the thing worth asserting is that it stays one. A
    /// handler that swapped, clamped or widened the bounds would still return rows and still look
    /// right from a test that only checked the output — hence the assertion on what the repository was
    /// ASKED, not merely on what it gave back.
    /// </remarks>
    [Fact]
    public async Task GetAssignmentStartsAsync_PassesTheRangeThrough_Unchanged()
    {
        // Arrange
        var reportRepository = new RecordingReportRepository
        {
            AssignmentStarts =
            [
                new AssignmentStartRowDto
                {
                    EmployeeName = "Maya Alvarez",
                    ClientName = "Buckeye Mutual",
                    StartDate = new DateOnly(2026, 3, 15),
                },
            ],
        };
        var service = Service(
            new RecordingRepository(), new FixedBusinessDate(BusinessToday), reportRepository);

        // Act
        var rows = await service.GetAssignmentStartsAsync(
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31),
            TestContext.Current.CancellationToken);

        // Assert
        reportRepository.AssignmentStartCalls.ShouldHaveSingleItem()
            .ShouldBe((new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));
        rows.ShouldHaveSingleItem().EmployeeName.ShouldBe("Maya Alvarez");
    }

    /// <summary>
    /// The lookup does NOT resolve the business date — unlike the availability report, its range comes
    /// from the caller, so there is nothing here to anchor.
    /// </summary>
    /// <remarks>
    /// Worth pinning because "resolve today and use it" is the habit the rest of this service follows,
    /// and a range report that quietly clamped its bounds to the business date would silently answer a
    /// narrower question than the one asked.
    /// </remarks>
    [Fact]
    public async Task GetAssignmentStartsAsync_AcceptsARangeEntirelyInTheFuture()
    {
        // Arrange
        var reportRepository = new RecordingReportRepository();
        var service = Service(
            new RecordingRepository(), new FixedBusinessDate(BusinessToday), reportRepository);

        // Act
        var rows = await service.GetAssignmentStartsAsync(
            new DateOnly(2090, 1, 1),
            new DateOnly(2090, 12, 31),
            TestContext.Current.CancellationToken);

        // Assert
        reportRepository.AssignmentStartCalls.ShouldHaveSingleItem()
            .ShouldBe((new DateOnly(2090, 1, 1), new DateOnly(2090, 12, 31)));
        rows.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- issue #534 — SOW Extension Report

    /// <summary>The report passes its range to the repository UNCHANGED and returns what comes back.</summary>
    [Fact]
    public async Task GetSowExtensionsAsync_PassesTheRangeThrough_Unchanged()
    {
        // Arrange
        var reportRepository = new RecordingReportRepository
        {
            SowExtensions =
            [
                new SowExtensionRowDto
                {
                    EmployeeName = "Maya Alvarez",
                    ClientName = "Buckeye Mutual",
                    ExtensionStartDate = new DateOnly(2026, 3, 15),
                },
            ],
        };
        var service = Service(
            new RecordingRepository(), new FixedBusinessDate(BusinessToday), reportRepository);

        // Act
        var rows = await service.GetSowExtensionsAsync(
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31),
            TestContext.Current.CancellationToken);

        // Assert
        reportRepository.SowExtensionCalls.ShouldHaveSingleItem()
            .ShouldBe((new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));
        rows.ShouldHaveSingleItem().EmployeeName.ShouldBe("Maya Alvarez");
    }

    /// <summary>Unlike the availability report, this one resolves no business date — the range is the caller's.</summary>
    [Fact]
    public async Task GetSowExtensionsAsync_AcceptsARangeEntirelyInTheFuture()
    {
        // Arrange
        var reportRepository = new RecordingReportRepository();
        var service = Service(
            new RecordingRepository(), new FixedBusinessDate(BusinessToday), reportRepository);

        // Act
        var rows = await service.GetSowExtensionsAsync(
            new DateOnly(2090, 1, 1),
            new DateOnly(2090, 12, 31),
            TestContext.Current.CancellationToken);

        // Assert
        reportRepository.SowExtensionCalls.ShouldHaveSingleItem()
            .ShouldBe((new DateOnly(2090, 1, 1), new DateOnly(2090, 12, 31)));
        rows.ShouldBeEmpty();
    }

    /// <summary>
    /// The duration report resolves the business date ONCE, from
    /// <see cref="ICompassBusinessDate"/>, and passes it through unchanged.
    /// </summary>
    /// <remarks>
    /// The service is a straight delegation — the grain, the FR-032 qualifying test and FR-015's span
    /// rule are all set-based SQL in <c>CompassReportRepository</c>, asserted in
    /// <c>CompassReportRepositoryTests</c> against a real query. What is left to this layer is the date,
    /// and it is worth pinning: a handler that called <c>DateOnly.FromDateTime(DateTime.Today)</c>
    /// instead would return plausible rows and disagree with the dashboard above it for four hours every
    /// evening, which is the failure <c>CompassBusinessDateTests</c> exists to prevent and the one
    /// nobody catches by hand.
    /// </remarks>
    [Fact]
    public async Task GetAssignmentDurationAsync_ResolvesTheBusinessDateOnce_AndPassesItThrough()
    {
        // Arrange
        var businessDate = new FixedBusinessDate(BusinessToday);
        var reportRepository = new RecordingReportRepository
        {
            DurationRows =
            [
                new AssignmentDurationRowDto
                {
                    EmployeeId = 1,
                    EmployeeName = "Priya Natarajan",
                    ClientId = 10,
                    ClientName = "Olentangy Health",
                    CoachName = "Jordan Wells",
                    TotalDays = 1238,
                    DurationDisplay = "3 yrs 4 mos (1,238 days)",
                },
            ],
        };
        var service = Service(new RecordingRepository(), businessDate, reportRepository);

        // Act
        var rows = await service.GetAssignmentDurationAsync(TestContext.Current.CancellationToken);

        // Assert
        reportRepository.DurationCalls.ShouldHaveSingleItem().ShouldBe(BusinessToday);
        businessDate.Calls.ShouldBe(1, "the date is resolved once per request, not once per row");

        // Returned untransformed: this layer must not re-sort or re-format. Ordering is applied in SQL
        // and DurationDisplay is built where the rows are materialised.
        var row = rows.ShouldHaveSingleItem();
        row.EmployeeName.ShouldBe("Priya Natarajan");
        row.TotalDays.ShouldBe(1238);
        row.DurationDisplay.ShouldBe("3 yrs 4 mos (1,238 days)");
    }

    private sealed class RecordingRepository : ICompassDashboardRepository
    {
        public List<(DashboardCategory Category, DateOnly Today)> BreakdownCalls { get; } = [];

        public Dictionary<DashboardCategory, IReadOnlyList<DashboardBreakdownRowDto>> Rows { get; init; } = [];

        public Task<SalesDashboardDto> GetCountsAsync(DateOnly today, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "the Availability Report must not ask for tile COUNTS — it projects the breakdowns");

        public Task<IReadOnlyList<DashboardBreakdownRowDto>> GetBreakdownAsync(
            DashboardCategory category,
            DateOnly today,
            CancellationToken cancellationToken)
        {
            BreakdownCalls.Add((category, today));

            return Task.FromResult(
                Rows.TryGetValue(category, out var rows) ? rows : []);
        }

    }

    private static DashboardBreakdownRowDto Row(
        int employeeId,
        string employeeName,
        DateOnly? date,
        int? daysUntil,
        string? coachName,
        params (int Id, string Name)[] clients) =>
        new()
        {
            EmployeeId = employeeId,
            EmployeeName = employeeName,
            Clients = [.. clients.Select(c => new DashboardBreakdownClientDto { Id = c.Id, Name = c.Name })],
            Date = date,
            DaysUntil = daysUntil,
            CoachName = coachName,
        };

    private static DashboardBreakdownRowDto Row(
        int employeeId,
        string employeeName,
        DateOnly? date,
        int? daysUntil,
        string? coachName,
        int? daysAvailable,
        string employeeType,
        params (int Id, string Name)[] clients) =>
        new()
        {
            EmployeeId = employeeId,
            EmployeeName = employeeName,
            Clients = [.. clients.Select(c => new DashboardBreakdownClientDto { Id = c.Id, Name = c.Name })],
            Date = date,
            DaysUntil = daysUntil,
            DaysAvailable = daysAvailable,
            CoachName = coachName,
            EmployeeType = employeeType,
        };

    private static CompassReportReadService Service(
        RecordingRepository repository,
        FixedBusinessDate businessDate,
        RecordingReportRepository? reportRepository = null) =>
        new(repository, reportRepository ?? new RecordingReportRepository(), businessDate);

    /// <summary>
    /// The report-only repository at this seam: records the Assignment Start lookup, refuses the
    /// duration query.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The start lookup records rather than returns a fixture, because the service is a straight
    /// delegation and the thing worth asserting is that the range arrives UNCHANGED — a handler that
    /// swapped, clamped or widened the bounds would still return rows and still look right to a test
    /// that only checked the output.
    /// </para>
    /// <para>
    /// The duration query throws, deliberately. It is set-based SQL asserted in
    /// <c>CompassReportRepositoryTests</c> against a real query; returning a plausible empty list here
    /// would let a future change to the availability path quietly start depending on it. Throwing keeps
    /// this file honest about what it covers.
    /// </para>
    /// <para>
    /// Both members moved onto one fake when the two report queries were consolidated into
    /// <c>ICompassReportRepository</c> (2026-08-20). The start lookup was previously recorded on the
    /// DASHBOARD fake, which is where its query used to live.
    /// </para>
    /// </remarks>
    private sealed class RecordingReportRepository : ICompassReportRepository
    {
        public List<(DateOnly From, DateOnly To)> AssignmentStartCalls { get; } = [];

        /// <summary>Rows the lookup returns.</summary>
        public IReadOnlyList<AssignmentStartRowDto> AssignmentStarts { get; set; } = [];

        /// <summary>Business dates the duration query was asked for.</summary>
        public List<DateOnly> DurationCalls { get; } = [];

        /// <summary>
        /// Rows the duration query returns, and the OPT-IN that allows it to be called at all.
        /// </summary>
        /// <remarks>
        /// Null by default, which keeps the throw below in force for every test that does not
        /// deliberately exercise the duration path — that guarantee is why this fake throws rather than
        /// returning a plausible empty list. Setting it says "this test is about the duration query".
        /// </remarks>
        public IReadOnlyList<AssignmentDurationRowDto>? DurationRows { get; set; }

        public Task<IReadOnlyList<AssignmentDurationRowDto>> GetAssignmentDurationAsync(
            DateOnly today, CancellationToken cancellationToken)
        {
            if (DurationRows is null)
            {
                throw new NotSupportedException(
                    "the availability path must not reach the duration query");
            }

            DurationCalls.Add(today);

            return Task.FromResult(DurationRows);
        }

        public Task<IReadOnlyList<AssignmentStartRowDto>> GetAssignmentStartsAsync(
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken)
        {
            AssignmentStartCalls.Add((from, to));

            return Task.FromResult(AssignmentStarts);
        }

        /// <summary>Ranges the SOW Extension Report query was asked for (issue #534).</summary>
        public List<(DateOnly From, DateOnly To)> SowExtensionCalls { get; } = [];

        /// <summary>Rows the SOW Extension Report returns.</summary>
        public IReadOnlyList<SowExtensionRowDto> SowExtensions { get; set; } = [];

        public Task<IReadOnlyList<SowExtensionRowDto>> GetSowExtensionsAsync(
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken)
        {
            SowExtensionCalls.Add((from, to));

            return Task.FromResult(SowExtensions);
        }
    }

    [Fact]
    public async Task GetAvailabilityAsync_ResolvesOneBusinessDate_AndAsksForTheThreeSectionsWithIt()
    {
        // Arrange
        var repository = new RecordingRepository();
        var businessDate = new FixedBusinessDate(BusinessToday);

        // Act
        var report = await Service(repository, businessDate)
            .GetAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert — one date, on the report AND on every section's query (FR-027's rule, applied to
        // this screen: three sections computed against different days could disagree about who is
        // available, and SC-003's tile-to-section equality would fail intermittently rather than
        // outright.
        report.AsOfDate.ShouldBe(BusinessToday);
        businessDate.Calls.ShouldBe(1);
        repository.BreakdownCalls.Select(c => c.Today).Distinct().ShouldHaveSingleItem();
        repository.BreakdownCalls.Select(c => c.Category).ShouldBe(
            [DashboardCategory.Beach, DashboardCategory.ConfirmedRollouts, DashboardCategory.ExpiringSows],
            ignoreOrder: true);
    }

    [Fact]
    public async Task GetAvailabilityAsync_Section1_CarriesNoClient_ThreeColumnsNotFour()
    {
        // Arrange — the beach breakdown DOES carry a client (the internal EDJE client), because the
        // dashboard's row shape is uniform across its four categories. Section 1 must drop it.
        var repository = new RecordingRepository
        {
            Rows =
            {
                [DashboardCategory.Beach] =
                [
                    Row(7, "Dana Reed", new DateOnly(2026, 7, 1), null, "Sam Coach", (99, "EDJE Internal")),
                ],
            },
        };

        // Act
        var report = await Service(repository, new FixedBusinessDate(BusinessToday))
            .GetAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert — three fields: EDJEr, internal-assignment start, coach (FR-010, FR-029, T050). The
        // mockup's `EDJE Client Assignment Start` is ONE date column, not a client column plus a date;
        // an earlier revision of FR-029 read it as four and T050 exists to stop the fourth being
        // built. `AvailableEdjerRowDto` having no client property is what makes that structural rather
        // than a rendering choice the frontend could quietly reverse.
        var row = report.CurrentlyAvailable.ShouldHaveSingleItem();
        row.EmployeeName.ShouldBe("Dana Reed");
        row.InternalAssignmentStartDate.ShouldBe(new DateOnly(2026, 7, 1));
        row.CoachName.ShouldBe("Sam Coach");

        typeof(AvailableEdjerRowDto).GetProperties()
            .Select(p => p.Name)
            .ShouldNotContain(
                name => name.Contains("Client", StringComparison.OrdinalIgnoreCase),
                "section 1 is three columns (FR-029, T050) — a client property here is the fourth "
                    + "column no acceptance criterion covers and no design source shows");
    }

    [Fact]
    public async Task GetAvailabilityAsync_Sections2And3_CarryTheirClientAndDaysUntil()
    {
        // Arrange
        var repository = new RecordingRepository
        {
            Rows =
            {
                [DashboardCategory.ConfirmedRollouts] =
                [
                    Row(3, "Ivy Stone", new DateOnly(2026, 9, 30), 44, "Sam Coach", (5, "Nordhaven")),
                ],
                [DashboardCategory.ExpiringSows] =
                [
                    Row(4, "Ken Diaz", new DateOnly(2026, 10, 1), 45, "Sam Coach", (6, "Quarry Ridge")),
                ],
            },
        };

        // Act
        var report = await Service(repository, new FixedBusinessDate(BusinessToday))
            .GetAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert — five fields each (FR-011, FR-012)
        var rollout = report.ConfirmedRollouts.ShouldHaveSingleItem();
        rollout.EmployeeName.ShouldBe("Ivy Stone");
        rollout.Clients.ShouldHaveSingleItem().Name.ShouldBe("Nordhaven");
        rollout.AssignmentEndDate.ShouldBe(new DateOnly(2026, 9, 30));
        rollout.DaysUntilRollout.ShouldBe(44);
        rollout.CoachName.ShouldBe("Sam Coach");

        var sow = report.UnconfirmedSows.ShouldHaveSingleItem();
        sow.EmployeeName.ShouldBe("Ken Diaz");
        sow.Clients.ShouldHaveSingleItem().Name.ShouldBe("Quarry Ridge");
        sow.SowEndDate.ShouldBe(new DateOnly(2026, 10, 1));
        sow.DaysUntilExpiration.ShouldBe(45);
        sow.CoachName.ShouldBe("Sam Coach");
    }

    /// <summary>Issue #334 — §1 carries the beach breakdown's DaysAvailable through as its own column.</summary>
    [Fact]
    public async Task GetAvailabilityAsync_Section1_CarriesDaysAvailable()
    {
        // Arrange
        var repository = new RecordingRepository
        {
            Rows =
            {
                [DashboardCategory.Beach] =
                [
                    Row(7, "Dana Reed", new DateOnly(2026, 7, 1), null, "Sam Coach",
                        daysAvailable: 53, employeeType: "Full Time", (99, "EDJE Internal")),
                ],
            },
        };

        // Act
        var report = await Service(repository, new FixedBusinessDate(BusinessToday))
            .GetAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert
        report.CurrentlyAvailable.ShouldHaveSingleItem().DaysAvailable.ShouldBe(53);
    }

    /// <summary>Issue #334 — §2 and §3 carry the breakdown's EmployeeType next to the EDJEr's name.</summary>
    [Fact]
    public async Task GetAvailabilityAsync_Sections2And3_CarryTheEmployeeType()
    {
        // Arrange
        var repository = new RecordingRepository
        {
            Rows =
            {
                [DashboardCategory.ConfirmedRollouts] =
                [
                    Row(3, "Ivy Stone", new DateOnly(2026, 9, 30), 44, "Sam Coach",
                        daysAvailable: null, employeeType: "Full Time", (5, "Nordhaven")),
                ],
                [DashboardCategory.ExpiringSows] =
                [
                    Row(4, "Ken Diaz", new DateOnly(2026, 10, 1), 45, "Sam Coach",
                        daysAvailable: null, employeeType: "1099", (6, "Quarry Ridge")),
                ],
            },
        };

        // Act
        var report = await Service(repository, new FixedBusinessDate(BusinessToday))
            .GetAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert
        report.ConfirmedRollouts.ShouldHaveSingleItem().EmployeeType.ShouldBe("Full Time");
        report.UnconfirmedSows.ShouldHaveSingleItem().EmployeeType.ShouldBe("1099");
    }

    /// <summary>T046 — the no-coach EDJEr survives the PROJECTION into all three section types.</summary>
    /// <remarks>
    /// The repository proves a null coach survives the query
    /// (<c>CompassDashboardRepositoryTests.NoCoachEmployee_AppearsInAllThreeReportPopulations_WithNullCoach</c>);
    /// this proves the mapping does not drop the row or substitute a placeholder on the way in. J19's
    /// stated outcome is that this report is where EDJErs "whose coach was not notified because they
    /// have none" stay visible — it is the compensating control for the notification Stream 3 skips,
    /// so a filtered row is a silent loss of the control, not a cosmetic gap.
    /// </remarks>
    [Fact]
    public async Task GetAvailabilityAsync_NoCoachEdjer_IsPresentInAllThreeSections_WithNullCoach()
    {
        // Arrange
        var repository = new RecordingRepository
        {
            Rows =
            {
                [DashboardCategory.Beach] =
                    [Row(9, "Pat Nolan", new DateOnly(2026, 6, 1), null, null, (99, "EDJE Internal"))],
                [DashboardCategory.ConfirmedRollouts] =
                    [Row(9, "Pat Nolan", new DateOnly(2026, 9, 1), 15, null, (5, "Nordhaven"))],
                [DashboardCategory.ExpiringSows] =
                    [Row(9, "Pat Nolan", new DateOnly(2026, 9, 5), 19, null, (5, "Nordhaven"))],
            },
        };

        // Act
        var report = await Service(repository, new FixedBusinessDate(BusinessToday))
            .GetAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert
        report.CurrentlyAvailable.ShouldHaveSingleItem().CoachName.ShouldBeNull();
        report.ConfirmedRollouts.ShouldHaveSingleItem().CoachName.ShouldBeNull();
        report.UnconfirmedSows.ShouldHaveSingleItem().CoachName.ShouldBeNull();
    }

    [Fact]
    public async Task GetAvailabilityAsync_PreservesTheRepositorysOrder_EarliestDateFirst()
    {
        // Arrange — handed back in the order the repository produced (FR-009: earliest first). The
        // service must not re-sort: the repository orders in SQL on the source date column, and a
        // second ordering here is a second place for the direction to drift.
        var repository = new RecordingRepository
        {
            Rows =
            {
                [DashboardCategory.Beach] =
                [
                    Row(1, "Early Bird", new DateOnly(2026, 1, 15), null, null, (99, "EDJE Internal")),
                    Row(2, "Late Riser", new DateOnly(2026, 7, 20), null, null, (99, "EDJE Internal")),
                ],
            },
        };

        // Act
        var report = await Service(repository, new FixedBusinessDate(BusinessToday))
            .GetAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert
        report.CurrentlyAvailable.Select(r => r.EmployeeName).ShouldBe(["Early Bird", "Late Riser"]);
    }

    [Fact]
    public async Task GetAvailabilityAsync_EmptyPopulations_ReturnEmptySections_NotNull()
    {
        // Arrange — every section empty. The frontend renders an explicit per-section empty state
        // (FR-024, spec Edge Cases), which it cannot do against a null list.
        var repository = new RecordingRepository();

        // Act
        var report = await Service(repository, new FixedBusinessDate(BusinessToday))
            .GetAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert
        report.CurrentlyAvailable.ShouldBeEmpty();
        report.ConfirmedRollouts.ShouldBeEmpty();
        report.UnconfirmedSows.ShouldBeEmpty();
    }
}
