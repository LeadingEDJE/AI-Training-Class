using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The Sales Dashboard's counting and breakdown logic — feature 007, T021-T023.
/// </summary>
/// <remarks>
/// <para>
/// Targets <see cref="CompassDashboardRepository"/>, and is named for it.
/// <c>CompassBoundaryTests.RuleTwo</c> forbids the Compass <c>Services/</c> layer from referencing a
/// data context at all, so everything these tasks exercise (the split-allocation shape, the
/// vacuous-truth trap, no-coach visibility) lives in the repository rather than in the service.
/// </para>
/// <para>
/// This file was originally named <c>CompassDashboardReadServiceTests</c> — the originally
/// specified name — while every test in it constructed a repository. The service was
/// therefore never instantiated by any test in the tree, which the per-file coverage gate caught as
/// 0%. Renamed to what it tests; the service now has its own file next to this one, covering the
/// one thing it genuinely owns (resolving the business date). A test file named for a class it
/// never touches reads as coverage while asserting nothing about it.
/// </para>
/// <para>
/// EF Core InMemory, per the house pattern (<c>CompassDirectoryRepositoryTests</c>): fast, and
/// sufficient for the LOGIC under test here. Translatability against real PostgreSQL is proved
/// separately by the integration suite (T018-T024, T024a) — InMemory evaluates every predicate as
/// ordinary LINQ-to-Objects and cannot tell a query that becomes SQL from one that only compiles.
/// </para>
/// </remarks>
public class CompassDashboardRepositoryTests
{
    private const int EmployeeTypeId = 1;
    private static readonly DateOnly Today = new(2026, 8, 17);

    private static LeapDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase($"CompassDashboardTestDb_{Guid.NewGuid():N}")
            .Options);

    // ------------------------------------------------------------------ T021(a) — split allocation

    [Fact]
    public async Task SplitAllocationEmployee_Contributes1ToActiveSows_1ToBeach_1ToRollout()
    {
        // Arrange — one current assignment to an internal client with NO end date (beach time), one
        // current assignment to a non-internal client WITH a future end date carrying an active SOW.
        // The one EDJEr shows on the active-assignments tile (via the non-internal assignment itself,
        // issue #633 — the SOW on it is incidental), the beach tile (via the internal assignment) and
        // the confirmed-rollouts tile (their real client assignment has an end date; issue #379 ignores
        // the internal one for rollout).
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var otherClient = AddClient(context, id: 2, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, internalClient, Today.AddMonths(-6), endDate: null);
        var realAssignment = AddAssignment(
            context, id: 2, employee, otherClient, Today.AddMonths(-3), Today.AddDays(30));
        AddSow(context, id: 1, realAssignment, Today.AddMonths(-3), Today.AddDays(30));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var activeSowsBreakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ActiveSows, Today, TestContext.Current.CancellationToken);
        var rolloutBreakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ActiveSowCount.ShouldBe(1, "the single active non-internal client assignment");
        counts.BeachCount.ShouldBe(1, "one EDJEr on the internal client, counted once");
        counts.ConfirmedRolloutCount.ShouldBe(
            1, "the internal-client assignment is ignored entirely; the real client assignment has "
                + "an end date, so the universal condition is satisfied over the remaining set");
        activeSowsBreakdown.ShouldContain(r => r.Clients.Any(c => c.Id == otherClient.Id));
        rolloutBreakdown.Count.ShouldBe(1);
        rolloutBreakdown.ShouldContain(r => r.Clients.Any(c => c.Id == otherClient.Id));
        rolloutBreakdown.ShouldNotContain(r => r.Clients.Any(c => c.Id == internalClient.Id));
    }

    // ------------------------------------------------------- FR-002 — internal-client SOWs excluded

    [Fact]
    public async Task ActiveSowsTile_ExcludesAnActiveSowOnAnInternalClientAssignment_FR002()
    {
        // Arrange — an open-ended, started INTERNAL-client (bench) assignment carrying a SOW, alongside
        // a normal non-internal assignment also carrying a SOW. Only migrated TPS history can produce
        // the internal-client SOW: the write surface refuses to create one (CompassSowService, "An
        // internal client's assignment has no statements of work"). FR-002 keeps internal,
        // non-client-facing work off the tile — the internal ASSIGNMENT is excluded regardless of the
        // SOW sitting on it — so only the real-client assignment counts.
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var realClient = AddClient(context, id: 2, isInternal: false);
        var edjerOnBench = AddEmployee(context, id: 1, coachId: null);
        var edjerOnRealWork = AddEmployee(context, id: 2, coachId: null);
        var internalAssignment = AddAssignment(
            context, id: 1, edjerOnBench, internalClient, Today.AddMonths(-6), endDate: null);
        var realAssignment = AddAssignment(
            context, id: 2, edjerOnRealWork, realClient, Today.AddMonths(-3), endDate: null);
        AddSow(context, id: 1, internalAssignment, Today.AddMonths(-3), Today.AddDays(60));
        AddSow(context, id: 2, realAssignment, Today.AddMonths(-2), Today.AddDays(90));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ActiveSows, Today, TestContext.Current.CancellationToken);

        // Assert — the internal-client assignment is excluded from BOTH the count and the breakdown,
        // and the two still agree (SC-003): the tile's count equals its breakdown's row count.
        counts.ActiveSowCount.ShouldBe(
            1, "only the real-client assignment; the internal-client one is excluded (FR-002)");
        breakdown.Count.ShouldBe(1);
        breakdown.ShouldContain(r => r.Clients.Any(c => c.Id == realClient.Id));
        breakdown.ShouldNotContain(r => r.Clients.Any(c => c.Id == internalClient.Id));
    }

    // ------------------------------------------------------------------ T021(b) — two internal clients

    [Fact]
    public async Task EmployeesOnTwoDifferentInternalClients_AreBothCountedOnTheBeachTile()
    {
        // Arrange — proves the query matches Client.IsInternal, never a single hardcoded client id.
        using var context = CreateContext();
        var internalClientA = AddClient(context, id: 1, isInternal: true);
        var internalClientB = AddClient(context, id: 2, isInternal: true);
        var employeeA = AddEmployee(context, id: 1, coachId: null);
        var employeeB = AddEmployee(context, id: 2, coachId: null);
        AddAssignment(context, id: 1, employeeA, internalClientA, Today.AddMonths(-2), endDate: null);
        AddAssignment(context, id: 2, employeeB, internalClientB, Today.AddMonths(-1), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);

        // Assert
        counts.BeachCount.ShouldBe(2, "both EDJErs are on a DIFFERENT internal client and must both count");
    }

    // ------------------------------------------------------------------ T022 — the vacuous-truth trap

    [Fact]
    public async Task EmployeeWithNoCurrentAssignmentAtAll_IsNotCountedAsAConfirmedRollout()
    {
        // Arrange — research D-2: "every active assignment has an end date" is vacuously true over an
        // EMPTY set of active assignments. An EDJEr with none at all must not be swept in by that.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddYears(-2), Today.AddDays(-1));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ConfirmedRolloutCount.ShouldBe(0);
        breakdown.ShouldBeEmpty();
    }

    // ------------------------------------------------------- issue #379 — internal clients ignored

    [Fact]
    public async Task EmployeeWhoseOnlyEndingAssignmentIsInternal_IsNotCountedAsAConfirmedRollout()
    {
        // Arrange — issue #379, question 2: an EDJEr whose only current assignment is to an INTERNAL
        // client, even one WITH an end date, must be left out of Confirmed Rollouts entirely — an
        // internal-client assignment is ignored for this tile, so this EDJEr has zero non-internal
        // current assignments and forms no group at all (the same vacuous-truth guard as
        // EmployeeWithNoCurrentAssignmentAtAll_IsNotCountedAsAConfirmedRollout above, reached via a
        // different route: filtered out rather than never present).
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, internalClient, Today.AddMonths(-6), Today.AddDays(10));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ConfirmedRolloutCount.ShouldBe(0);
        breakdown.ShouldBeEmpty();
    }

    [Fact]
    public async Task EmployeeOnTwoInternalClients_BothWithEndDates_IsNotCountedAsAConfirmedRollout()
    {
        // Arrange — same rule, with more than one internal assignment so the universal condition
        // would trivially hold if internal clients were not excluded (both carry an end date). Proves
        // the exclusion happens before the group forms, not just when exactly one internal assignment
        // is present.
        using var context = CreateContext();
        var internalA = AddClient(context, id: 1, isInternal: true);
        var internalB = AddClient(context, id: 2, isInternal: true);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, internalA, Today.AddMonths(-6), Today.AddDays(10));
        AddAssignment(context, id: 2, employee, internalB, Today.AddMonths(-2), Today.AddDays(20));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ConfirmedRolloutCount.ShouldBe(0);
        breakdown.ShouldBeEmpty();
    }

    [Fact]
    public async Task InternalAssignmentWithEndDate_NeverSetsTheRolloutDate_EvenWhenItsLatest()
    {
        // Arrange — issue #379: the correlated MAX that picks the displayed date/client must also
        // exclude internal clients. The internal assignment ends AFTER the real one, so if it were not
        // excluded from the MAX subquery it would incorrectly become the row shown.
        using var context = CreateContext();
        var realClient = AddClient(context, id: 1, isInternal: false);
        var internalClient = AddClient(context, id: 2, isInternal: true);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, realClient, Today.AddMonths(-6), Today.AddDays(10));
        AddAssignment(context, id: 2, employee, internalClient, Today.AddMonths(-1), Today.AddDays(90));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ConfirmedRolloutCount.ShouldBe(1);
        breakdown.Count.ShouldBe(1, "the tile's count and the breakdown's row count must always match");
        breakdown[0].Clients.ShouldHaveSingleItem().Id.ShouldBe(realClient.Id);
        breakdown[0].Date.ShouldBe(Today.AddDays(10), "the internal assignment's later end date must "
            + "never win the correlated MAX");
    }

    // ------------------------------------------------------------------ T023 — no-coach visibility

    [Fact]
    public async Task NoCoachEmployee_AppearsInRolloutAndExpiringSowBreakdowns_WithNullCoach()
    {
        // Arrange — issue #457 makes the two populations mutually exclusive on one non-internal
        // assignment: a confirmed rollout HAS an end date, an expiring SOW must sit on an OPEN-ENDED
        // one. So two no-coach EDJErs, one per population, each visible with an empty coach field.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var rolloutEmployee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, rolloutEmployee, client, Today.AddMonths(-8), Today.AddDays(30));

        var expiringEmployee = AddEmployee(context, id: 2, coachId: null);
        var expiringAssignment = AddAssignment(
            context, id: 2, expiringEmployee, client, Today.AddMonths(-8), endDate: null);
        context.Set<Sow>().Add(new Sow
        {
            Id = 1,
            ClientAssignmentId = expiringAssignment.Id,
            SowStartDate = Today.AddMonths(-8),
            SowEndDate = Today.AddDays(30),
            HasPassedApplicationValidation = true,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var rollouts = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);
        var expiringSows = await repository.GetBreakdownAsync(
            DashboardCategory.ExpiringSows, Today, TestContext.Current.CancellationToken);

        // Assert — present, not filtered out, with an empty coach field.
        rollouts.ShouldContain(r => r.EmployeeId == rolloutEmployee.Id && r.CoachName == null);
        expiringSows.ShouldContain(r => r.EmployeeId == expiringEmployee.Id && r.CoachName == null);
    }

    // ------------------------------------------------------------------ Issue #330 — the coach's id

    /// <summary>
    /// Issue #330 — every breakdown carries the coach's OWN id beside their name, so the Sales
    /// Dashboard can link the coach cell into their record the way it already links the client.
    /// </summary>
    /// <remarks>
    /// All four categories in one test on purpose: the coach is resolved by four separate
    /// projections, and two of them (rollouts, beach) then fold through
    /// <c>GroupToOneRowPerEmployee</c>, whose tuple is a fifth place the pair can be dropped.
    /// <see cref="NoCoachEmployee_AppearsInRolloutAndExpiringSowBreakdowns_WithNullCoach"/> and
    /// <see cref="NoCoachEmployee_AppearsInTheBeachBreakdown_WithNullCoach"/> cover the null half;
    /// this is the only test in the file that gives an EDJEr a coach at all.
    /// </remarks>
    [Fact]
    public async Task CoachedEmployee_CarriesTheCoachsIdBesideTheirName_InEveryBreakdown()
    {
        // Arrange — a split-allocation EDJEr who HAS a coach and qualifies for all four tiles at once:
        // a real client assignment ending inside the window (confirmed rollout) carrying an active SOW
        // (active tile), and an open-ended internal bench assignment (beach) whose SOW expires inside
        // the window (expiring). Two SOWs are needed because FR-002 excludes internal-client SOWs from
        // the ACTIVE tile and #457 requires the EXPIRING SOW to sit on an open-ended assignment — so
        // the active SOW is on the real (non-internal, end-dated) assignment and the expiring SOW is on
        // the internal (open-ended) one.
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var realClient = AddClient(context, id: 2, isInternal: false);
        var coach = AddEmployee(context, id: 1, coachId: null);
        var employee = AddEmployee(context, id: 2, coachId: coach.Id);
        var realAssignment = AddAssignment(
            context, id: 1, employee, realClient, Today.AddMonths(-6), Today.AddDays(30));
        var openEndedAssignment = AddAssignment(
            context, id: 2, employee, internalClient, Today.AddMonths(-3), endDate: null);
        // The expiring SOW sits on the OPEN-ENDED (internal) assignment (#457). FR-002 excludes
        // internal-client SOWs from the active tile, so it drives expiring/beach, never active.
        AddSow(context, id: 1, openEndedAssignment, Today.AddMonths(-3), Today.AddDays(30));
        // A separate active SOW on the real (non-internal) client puts the EDJEr on the Active SOWs
        // tile (FR-002). Its assignment is end-dated — a planned rollout — so #457 keeps this SOW off
        // the expiring tile; it drives the active tile alone.
        AddSow(context, id: 2, realAssignment, Today.AddMonths(-6), Today.AddDays(30));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        DashboardCategory[] categories =
        [
            DashboardCategory.ActiveSows,
            DashboardCategory.ExpiringSows,
            DashboardCategory.ConfirmedRollouts,
            DashboardCategory.Beach,
        ];

        // Assert — the id and the name travel together, and the id is the COACH's, not the EDJEr's.
        foreach (var category in categories)
        {
            var rows = await repository.GetBreakdownAsync(
                category, Today, TestContext.Current.CancellationToken);

            var row = rows.ShouldHaveSingleItem();
            row.EmployeeId.ShouldBe(employee.Id, $"{category}");
            row.CoachId.ShouldBe(coach.Id, $"{category} must carry the coach's own id (issue #330)");
            row.CoachName.ShouldBe("Test Employee1", $"{category}");
        }
    }

    // ------------------------------------------------------------------ Issue #332 — employee type

    [Fact]
    public async Task ActiveSowsBreakdown_CarriesTheEmployeesRealEmployeeType()
    {
        // Arrange — one Full Time and one Part Time EDJEr, each with an active SOW on a current
        // assignment.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var fullTime = AddEmployee(context, id: 1, coachId: null);
        var partTime = AddEmployee(
            context, id: 2, coachId: null, employeeTypeId: 2, employeeTypeName: "Part Time");
        var fullTimeAssignment = AddAssignment(context, id: 1, fullTime, client, Today.AddMonths(-6), endDate: null);
        var partTimeAssignment = AddAssignment(context, id: 2, partTime, client, Today.AddMonths(-3), endDate: null);
        AddSow(context, id: 1, fullTimeAssignment, Today.AddMonths(-6), Today.AddDays(30));
        AddSow(context, id: 2, partTimeAssignment, Today.AddMonths(-3), Today.AddDays(60));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ActiveSows, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.ShouldContain(r => r.EmployeeId == fullTime.Id && r.EmployeeType == "Full Time");
        breakdown.ShouldContain(r => r.EmployeeId == partTime.Id && r.EmployeeType == "Part Time");
    }

    [Fact]
    public async Task ExpiringSowsBreakdown_CarriesTheEmployeesRealEmployeeType()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var contractor = AddEmployee(
            context, id: 1, coachId: null, employeeTypeId: 3, employeeTypeName: "1099");
        // Open-ended assignment (issue #457): an expiring SOW qualifies only when its assignment has
        // no end date.
        var assignment = AddAssignment(
            context, id: 1, contractor, client, Today.AddMonths(-8), endDate: null);
        context.Set<Sow>().Add(new Sow
        {
            Id = 1,
            ClientAssignmentId = assignment.Id,
            SowStartDate = Today.AddMonths(-8),
            SowEndDate = Today.AddDays(30),
            HasPassedApplicationValidation = true,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ExpiringSows, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.ShouldContain(r => r.EmployeeId == contractor.Id && r.EmployeeType == "1099");
    }

    [Fact]
    public async Task ConfirmedRolloutsBreakdown_CarriesTheEmployeesRealEmployeeType()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var partTime = AddEmployee(
            context, id: 1, coachId: null, employeeTypeId: 2, employeeTypeName: "Part Time");
        AddAssignment(context, id: 1, partTime, client, Today.AddMonths(-8), Today.AddDays(30));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.ShouldContain(r => r.EmployeeId == partTime.Id && r.EmployeeType == "Part Time");
    }

    [Fact]
    public async Task BeachBreakdown_CarriesTheEmployeesRealEmployeeType()
    {
        // Arrange — the frontend has no column for this on the beach grid (1099s are never on the
        // beach), but the repository still resolves it consistently across all four categories.
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var fullTime = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, fullTime, internalClient, Today.AddMonths(-2), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.Beach, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.ShouldContain(r => r.EmployeeId == fullTime.Id && r.EmployeeType == "Full Time");
    }

    // ------------------------------------------------------------------ Sort order (soonest first)

    [Fact]
    public async Task ExpiringSowsBreakdown_IsSortedByDaysUntil_SmallestFirst()
    {
        // Arrange — three SOWs expiring at different points inside the 90-day window, seeded out of
        // order, so a pass-through-unsorted implementation would fail this.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var soonest = AddEmployee(context, id: 1, coachId: null);
        var middle = AddEmployee(context, id: 2, coachId: null);
        var latest = AddEmployee(context, id: 3, coachId: null);
        // Open-ended assignments (issue #457): the ordering is over the SOW end dates, not the
        // assignment's — which now must be null for the SOW to qualify.
        var middleAssignment = AddAssignment(
            context, id: 1, middle, client, Today.AddMonths(-8), endDate: null);
        var latestAssignment = AddAssignment(
            context, id: 2, latest, client, Today.AddMonths(-8), endDate: null);
        var soonestAssignment = AddAssignment(
            context, id: 3, soonest, client, Today.AddMonths(-8), endDate: null);
        context.Set<Sow>().AddRange(
            new Sow
            {
                Id = 1,
                ClientAssignmentId = middleAssignment.Id,
                SowStartDate = Today.AddMonths(-8),
                SowEndDate = Today.AddDays(60),
                HasPassedApplicationValidation = true,
            },
            new Sow
            {
                Id = 2,
                ClientAssignmentId = latestAssignment.Id,
                SowStartDate = Today.AddMonths(-8),
                SowEndDate = Today.AddDays(89),
                HasPassedApplicationValidation = true,
            },
            new Sow
            {
                Id = 3,
                ClientAssignmentId = soonestAssignment.Id,
                SowStartDate = Today.AddMonths(-8),
                SowEndDate = Today.AddDays(10),
                HasPassedApplicationValidation = true,
            });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ExpiringSows, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.Select(r => r.EmployeeId).ShouldBe(
            [soonest.Id, middle.Id, latest.Id], "smallest DaysUntil must sort to the top");
    }

    // ------------------------------------------------------ expiring SOWs ties (issue #693)

    [Fact]
    public async Task ExpiringSowsBreakdown_TiedOnEndDate_SortsByClientNameBeforeEmployeeName()
    {
        // Arrange — two SOWs expiring on the SAME day, deliberately seeded so client-name order and
        // employee-name order DISAGREE: "Acme" (alphabetically first) belongs to the employee whose
        // name would sort LAST, and "Zephyr" (alphabetically last) belongs to the employee whose name
        // would sort FIRST. Only a client-name-first tiebreak puts Acme's row on top.
        using var context = CreateContext();
        var acme = AddClient(context, id: 1, isInternal: false);
        acme.ClientName = "Acme Inc";
        var zephyr = AddClient(context, id: 2, isInternal: false);
        zephyr.ClientName = "Zephyr Corp";

        var zoe = AddEmployee(context, id: 1, coachId: null);
        zoe.FirstName = "Zoe";
        zoe.LastName = "Zephyr";
        var aaron = AddEmployee(context, id: 2, coachId: null);
        aaron.FirstName = "Aaron";
        aaron.LastName = "Aaronson";

        // Zoe (last alphabetically) is on Acme (first alphabetically); Aaron (first alphabetically)
        // is on Zephyr (last alphabetically).
        var acmeAssignment = AddAssignment(context, id: 1, zoe, acme, Today.AddMonths(-8), endDate: null);
        var zephyrAssignment = AddAssignment(
            context, id: 2, aaron, zephyr, Today.AddMonths(-8), endDate: null);
        AddSow(context, id: 1, acmeAssignment, Today.AddMonths(-8), Today.AddDays(30));
        AddSow(context, id: 2, zephyrAssignment, Today.AddMonths(-8), Today.AddDays(30));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ExpiringSows, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.Select(r => r.Clients.Single().Name).ShouldBe(
            ["Acme Inc", "Zephyr Corp"], "a same-day tie must break on client name, not EDJEr name");
    }

    [Fact]
    public async Task ExpiringSowsBreakdown_TiedOnEndDateAndClient_SortsByEmployeeName()
    {
        // Arrange — two SOWs on the SAME client, expiring the SAME day, held by two different EDJErs
        // seeded out of name order.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var zoe = AddEmployee(context, id: 1, coachId: null);
        zoe.FirstName = "Zoe";
        zoe.LastName = "Abbott";
        var adam = AddEmployee(context, id: 2, coachId: null);
        adam.FirstName = "Adam";
        adam.LastName = "Zephyr";
        var zoeAssignment = AddAssignment(context, id: 1, zoe, client, Today.AddMonths(-8), endDate: null);
        var adamAssignment = AddAssignment(
            context, id: 2, adam, client, Today.AddMonths(-8), endDate: null);
        AddSow(context, id: 1, zoeAssignment, Today.AddMonths(-8), Today.AddDays(30));
        AddSow(context, id: 2, adamAssignment, Today.AddMonths(-8), Today.AddDays(30));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ExpiringSows, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.Select(r => r.EmployeeName).ShouldBe(
            ["Adam Zephyr", "Zoe Abbott"], "a same-client, same-day tie must break on EDJEr name");
    }

    [Fact]
    public async Task ConfirmedRolloutsBreakdown_IsSortedByDaysUntil_SmallestFirst()
    {
        // Arrange — three EDJErs each rolling out at a different point, seeded out of order.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var soonest = AddEmployee(context, id: 1, coachId: null);
        var middle = AddEmployee(context, id: 2, coachId: null);
        var latest = AddEmployee(context, id: 3, coachId: null);
        AddAssignment(context, id: 1, middle, client, Today.AddMonths(-8), Today.AddDays(45));
        AddAssignment(context, id: 2, latest, client, Today.AddMonths(-8), Today.AddDays(80));
        AddAssignment(context, id: 3, soonest, client, Today.AddMonths(-8), Today.AddDays(5));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.Select(r => r.EmployeeId).ShouldBe(
            [soonest.Id, middle.Id, latest.Id], "smallest DaysUntil must sort to the top");
    }

    // ------------------------------------------------------------------ Clients (client-link navigation)

    [Fact]
    public async Task EveryBreakdownCategory_CarriesTheClient_SoTheRowCanLinkToIt()
    {
        // Arrange — one qualifying row for each of the four categories, on a distinguishable client.
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 10, isInternal: true);
        var otherClient = AddClient(context, id: 11, isInternal: false);
        var beachEmployee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, beachEmployee, internalClient, Today.AddMonths(-2), endDate: null);

        var rolloutEmployee = AddEmployee(context, id: 2, coachId: null);
        var rolloutAssignment = AddAssignment(
            context, id: 2, rolloutEmployee, otherClient, Today.AddMonths(-6), Today.AddDays(30));
        context.Set<Sow>().Add(new Sow
        {
            Id = 1,
            ClientAssignmentId = rolloutAssignment.Id,
            SowStartDate = Today.AddMonths(-6),
            SowEndDate = Today.AddDays(30),
            HasPassedApplicationValidation = true,
        });

        // Issue #457: the expiring-SOWs row is a SEPARATE EDJEr, on an OPEN-ENDED assignment — the
        // rollout employee above has an end date, so his SOW is now excluded from that population.
        var expiringClient = AddClient(context, id: 12, isInternal: false);
        var expiringEmployee = AddEmployee(context, id: 3, coachId: null);
        var expiringAssignment = AddAssignment(
            context, id: 3, expiringEmployee, expiringClient, Today.AddMonths(-6), endDate: null);
        context.Set<Sow>().Add(new Sow
        {
            Id = 2,
            ClientAssignmentId = expiringAssignment.Id,
            SowStartDate = Today.AddMonths(-6),
            SowEndDate = Today.AddDays(30),
            HasPassedApplicationValidation = true,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var active = await repository.GetBreakdownAsync(
            DashboardCategory.ActiveSows, Today, TestContext.Current.CancellationToken);
        var expiring = await repository.GetBreakdownAsync(
            DashboardCategory.ExpiringSows, Today, TestContext.Current.CancellationToken);
        var rollouts = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);
        var beach = await repository.GetBreakdownAsync(
            DashboardCategory.Beach, Today, TestContext.Current.CancellationToken);

        // Assert — every row names the client it is actually about, not merely its name.
        active.ShouldContain(r => r.EmployeeId == rolloutEmployee.Id && r.Clients.Any(c => c.Id == otherClient.Id));
        expiring.ShouldContain(r => r.EmployeeId == expiringEmployee.Id && r.Clients.Any(c => c.Id == expiringClient.Id));
        rollouts.ShouldContain(r => r.EmployeeId == rolloutEmployee.Id && r.Clients.Any(c => c.Id == otherClient.Id));
        beach.ShouldContain(r => r.EmployeeId == beachEmployee.Id && r.Clients.Any(c => c.Id == internalClient.Id));
    }

    // ------------------------------------------------------------------ Helpers

    // ------------------------------------------------------------ ties (SC-003)

    [Fact]
    public async Task TwoInternalClientsStartedTheSameDay_AreOneBeachRow_NamingBoth()
    {
        // Arrange — the tie the beach breakdown is keyed on. The correlated MIN subquery matches BOTH
        // assignments, so before the in-memory fold this produced two rows for one EDJEr while the
        // tile counted them once. SC-003 requires those two numbers to agree.
        using var context = CreateContext();
        var zulu = AddClient(context, id: 1, isInternal: true);
        zulu.ClientName = "Zulu Internal";
        var alpha = AddClient(context, id: 2, isInternal: true);
        alpha.ClientName = "Alpha Internal";
        var employee = AddEmployee(context, id: 1, coachId: null);
        var sameDay = Today.AddMonths(-2);
        AddAssignment(context, id: 1, employee, zulu, sameDay, endDate: null);
        AddAssignment(context, id: 2, employee, alpha, sameDay, endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.Beach, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.Count.ShouldBe(
            counts.BeachCount,
            "the beach tile counts DISTINCT EDJErs, so a tie must not add a breakdown row");
        breakdown.Count.ShouldBe(1);

        // Neither client is dropped, and they are named in an order a viewer can predict.
        breakdown[0].Clients.Select(c => c.Name).ShouldBe(["Alpha Internal", "Zulu Internal"]);
        breakdown[0].Clients.Select(c => c.Id).ShouldBe([alpha.Id, zulu.Id]);
        breakdown[0].Date.ShouldBe(sameDay);
    }

    // ------------------------------------------------------------------ Issue #329 — days available

    [Fact]
    public async Task BeachBreakdown_DaysAvailable_IsWholeDaysFromStartDateToToday()
    {
        // Arrange — an EDJEr who has been on the internal bench for exactly 10 whole days.
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var startDate = Today.AddDays(-10);
        AddAssignment(context, id: 1, employee, internalClient, startDate, endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.Beach, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.Count.ShouldBe(1);
        breakdown[0].DaysAvailable.ShouldBe(10, "today minus the internal-assignment start date");
    }

    [Fact]
    public async Task OtherCategoryBreakdowns_LeaveDaysAvailableNull()
    {
        // Arrange — DaysAvailable is the beach category's own field; the other three categories'
        // Date lies in the future, so it must never be populated for them.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var assignment = AddAssignment(context, id: 1, employee, client, Today.AddMonths(-6), Today.AddDays(30));
        AddSow(context, id: 1, assignment, Today.AddMonths(-6), Today.AddDays(30));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var activeSows = await repository.GetBreakdownAsync(
            DashboardCategory.ActiveSows, Today, TestContext.Current.CancellationToken);
        var rollouts = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);

        // Assert
        activeSows.ShouldNotBeEmpty();
        activeSows.ShouldAllBe(r => r.DaysAvailable == null);
        rollouts.ShouldAllBe(r => r.DaysAvailable == null);
    }

    // ------------------------------------------------------------------ Issue #334 — employee type

    [Fact]
    public async Task ConfirmedRolloutsAndExpiringSowsBreakdowns_IncludeTheEmployeeType()
    {
        // Arrange — issue #457 splits the two populations across two EDJErs (a rollout HAS an end
        // date, an expiring SOW sits on an OPEN-ENDED assignment); both seeded (via AddEmployee) with
        // the "Full Time" employee type.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var rolloutEmployee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, rolloutEmployee, client, Today.AddMonths(-8), Today.AddDays(30));

        var expiringEmployee = AddEmployee(context, id: 2, coachId: null);
        var expiringAssignment = AddAssignment(
            context, id: 2, expiringEmployee, client, Today.AddMonths(-8), endDate: null);
        context.Set<Sow>().Add(new Sow
        {
            Id = 1,
            ClientAssignmentId = expiringAssignment.Id,
            SowStartDate = Today.AddMonths(-8),
            SowEndDate = Today.AddDays(30),
            HasPassedApplicationValidation = true,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var rollouts = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);
        var expiringSows = await repository.GetBreakdownAsync(
            DashboardCategory.ExpiringSows, Today, TestContext.Current.CancellationToken);

        // Assert
        rollouts.ShouldHaveSingleItem().EmployeeType.ShouldBe("Full Time");
        expiringSows.ShouldHaveSingleItem().EmployeeType.ShouldBe("Full Time");
    }

    /// <summary>
    /// Issue #332 SUPERSEDES issue #334 here. #334 populated employee type for the two categories
    /// its report sections render and asserted the other two were left null; #332 needs it on the
    /// Sales Dashboard's active grid too, and populates all four so the shared AC-37 shape has no
    /// per-category hole. This test is the inverted successor to #334's
    /// <c>BeachAndActiveAssignmentsBreakdowns_LeaveEmployeeTypeNull</c> — same arrangement, opposite
    /// expectation. Nothing #334 delivered depended on the null: its §1 row shape carries no
    /// employee-type field at all.
    /// </summary>
    [Fact]
    public async Task BeachAndActiveSowsBreakdowns_AlsoCarryTheEmployeeType()
    {
        // Arrange — one EDJEr on both an internal and an external current assignment (the external one
        // carrying an active SOW), so the same person appears in the beach and active-SOWs breakdowns
        // at once.
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var otherClient = AddClient(context, id: 2, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, internalClient, Today.AddDays(-10), endDate: null);
        var otherAssignment = AddAssignment(context, id: 2, employee, otherClient, Today.AddMonths(-3), endDate: null);
        AddSow(context, id: 1, otherAssignment, Today.AddMonths(-3), Today.AddDays(30));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var beach = await repository.GetBreakdownAsync(
            DashboardCategory.Beach, Today, TestContext.Current.CancellationToken);
        var activeSows = await repository.GetBreakdownAsync(
            DashboardCategory.ActiveSows, Today, TestContext.Current.CancellationToken);

        // Assert
        beach.ShouldNotBeEmpty();
        activeSows.ShouldNotBeEmpty();
        beach.ShouldAllBe(r => r.EmployeeType == "Full Time");
        activeSows.ShouldAllBe(r => r.EmployeeType == "Full Time");
    }

    [Fact]
    public async Task TwoAssignmentsEndingTheSameDay_AreOneRolloutRow_NamingBoth()
    {
        // Arrange — the same tie on the rollout side: both assignments carry an end date (so FR-004's
        // universal condition holds) and both END on the latest date, so the correlated MAX matches
        // both.
        using var context = CreateContext();
        var zulu = AddClient(context, id: 1, isInternal: false);
        zulu.ClientName = "Zulu Corp";
        var alpha = AddClient(context, id: 2, isInternal: false);
        alpha.ClientName = "Alpha Corp";
        var employee = AddEmployee(context, id: 1, coachId: null);
        var sameEnd = Today.AddDays(40);
        AddAssignment(context, id: 1, employee, zulu, Today.AddMonths(-6), sameEnd);
        AddAssignment(context, id: 2, employee, alpha, Today.AddMonths(-3), sameEnd);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.Count.ShouldBe(counts.ConfirmedRolloutCount);
        breakdown.Count.ShouldBe(1);
        breakdown[0].Clients.Select(c => c.Name).ShouldBe(["Alpha Corp", "Zulu Corp"]);
        breakdown[0].Date.ShouldBe(sameEnd);
        breakdown[0].DaysUntil.ShouldBe(40, "measured to the date the EDJEr actually becomes available");
    }

    [Fact]
    public async Task AnEdjerWithNoTie_StillCarriesExactlyOneClient()
    {
        // Arrange — the fold must not turn the ordinary single-assignment case into a list of one
        // that the screen then renders differently.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: true);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddMonths(-1), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.Beach, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.ShouldHaveSingleItem().Clients.ShouldHaveSingleItem().Id.ShouldBe(client.Id);
    }

    // ------------------------------------------------------------ deterministic order (finding #2)

    [Fact]
    public async Task ActiveSowsBreakdown_IsOrderedByTheEdjerNameTheCellDisplays()
    {
        // Arrange — the active-assignments breakdown's default order is the EDJEr name the cell
        // displays (Ordinal), then the max SOW end date, then assignment id. Seeded deliberately out of
        // name order.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var zoe = AddEmployee(context, id: 1, coachId: null);
        zoe.FirstName = "Zoe";
        zoe.LastName = "Abbott";
        var adam = AddEmployee(context, id: 2, coachId: null);
        adam.FirstName = "Adam";
        adam.LastName = "Zephyr";
        var zoeAssignment = AddAssignment(context, id: 1, zoe, client, Today.AddMonths(-2), endDate: null);
        var adamAssignment = AddAssignment(context, id: 2, adam, client, Today.AddMonths(-1), endDate: null);
        AddSow(context, id: 1, zoeAssignment, Today.AddMonths(-2), Today.AddDays(30));
        AddSow(context, id: 2, adamAssignment, Today.AddMonths(-1), Today.AddDays(60));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ActiveSows, Today, TestContext.Current.CancellationToken);

        // Assert — by the displayed "First Last" string, which is what a viewer can predict.
        breakdown.Select(r => r.EmployeeName).ShouldBe(["Adam Zephyr", "Zoe Abbott"]);
    }

    /// <summary>
    /// Issue #633 — the regression this fix exists for. Two SOWs on ONE assignment used to produce two
    /// rows and let the tile show whichever end date the query happened to pick first; now it is one
    /// row for the assignment, and the date shown is the MAX end date across both SOWs, not the
    /// earlier-ending one.
    /// </summary>
    [Fact]
    public async Task ActiveSowsBreakdown_OneRowPerAssignment_ShowsTheMaxSowEndDateAcrossAllItsSows()
    {
        // Arrange — one EDJEr holding TWO SOWs on the SAME assignment: one ending sooner (Today+40),
        // one ending later (Today+80). The reported bug showed the sooner date; the fix must show the
        // later one, on a SINGLE row for the one assignment.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var assignment = AddAssignment(context, id: 1, employee, client, Today.AddMonths(-6), endDate: null);
        AddSow(context, id: 1, assignment, Today.AddMonths(-6), Today.AddDays(40));
        AddSow(context, id: 2, assignment, Today.AddMonths(-2), Today.AddDays(80));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ActiveSows, Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ActiveSowCount.ShouldBe(1, "one assignment, however many SOWs it owns, counts once");
        breakdown.Count.ShouldBe(
            counts.ActiveSowCount, "one row per assignment; the tile and breakdown agree");
        breakdown.ShouldHaveSingleItem().Date.ShouldBe(
            Today.AddDays(80), "the MAX end date across the assignment's SOWs, not the earlier one");
    }

    // ------------------------------------------------------------ issue #459 / #633 — start date

    [Fact]
    public async Task ActiveSowsBreakdown_CarriesTheAssignmentStartDateAndMaxSowEndDate()
    {
        // Arrange — an assignment whose own start date differs from its one SOW's start and end, so the
        // row carries the ASSIGNMENT start in StartDate (issue #633 — was the SOW's own start) and the
        // SOW end in Date.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var assignmentStart = Today.AddMonths(-6);
        var assignment = AddAssignment(context, id: 1, employee, client, assignmentStart, endDate: null);
        var sowEnd = Today.AddDays(30);
        AddSow(context, id: 1, assignment, Today.AddMonths(-3), sowEnd);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ActiveSows, Today, TestContext.Current.CancellationToken);

        // Assert
        var row = breakdown.ShouldHaveSingleItem();
        row.StartDate.ShouldBe(assignmentStart, "StartDate is the ASSIGNMENT start date (issue #633)");
        row.Date.ShouldBe(sowEnd, "Date is the (max) SOW end date");
        row.SowId.ShouldBeNull("a row is now per-assignment, which can own several SOWs (issue #633)");
        row.DaysUntil.ShouldBeNull("active-sows has no urgency axis");
    }

    [Fact]
    public async Task ActiveSowsBreakdown_AssignmentWithNoSow_ShowsANullEndDate()
    {
        // Arrange — issue #633: this tile is about active ASSIGNMENTS, so one with no SOW at all still
        // appears, with nothing to show for the end date.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddMonths(-6), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ActiveSows, Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ActiveSowCount.ShouldBe(1, "the assignment counts regardless of whether it has a SOW");
        breakdown.ShouldHaveSingleItem().Date.ShouldBeNull("no SOW means no end date to show");
    }

    [Fact]
    public async Task NonActiveSowsBreakdowns_LeaveStartDateNull()
    {
        // Arrange — StartDate is an active-sows-only column (issue #459); the other three
        // categories must not pick up a stray value from whichever record backs their own row.
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, internalClient, Today.AddMonths(-2), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var beachBreakdown = await repository.GetBreakdownAsync(
            DashboardCategory.Beach, Today, TestContext.Current.CancellationToken);

        // Assert
        beachBreakdown.ShouldHaveSingleItem().StartDate.ShouldBeNull();
    }

    [Fact]
    public async Task BeachBreakdown_IsOrderedByStartDate_LongestAvailableFirst()
    {
        // Arrange — FR-009 calls this population's report section chronologically sorted, and FR-006
        // puts the most urgent row on top; the EDJEr available longest is the one to staff first.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: true);
        var recent = AddEmployee(context, id: 1, coachId: null);
        var longest = AddEmployee(context, id: 2, coachId: null);
        AddAssignment(context, id: 1, recent, client, Today.AddDays(-10), endDate: null);
        AddAssignment(context, id: 2, longest, client, Today.AddMonths(-6), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.Beach, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.Select(r => r.EmployeeId).ShouldBe([longest.Id, recent.Id]);
        breakdown[0].Date.ShouldBe(Today.AddMonths(-6));
    }

    // ------------------------------------------------------------ US2 report populations (T044-T046)

    // The Availability Report projects THESE breakdowns rather than re-deriving its own (FR-005/FR-010,
    // FR-004/FR-011, FR-003/FR-012 each mandate one derivation; T049 calls a second definition the
    // divergence BR-11 forbids). So the report's data rules are the repository's data rules, and
    // tasks T044, T044a and T045 are asserted here rather than in CompassReportReadServiceTests --
    // over a fake repository they would assert the fake. Same reasoning, and the same resolution, as
    // the rename recorded in this file's own remarks.

    /// <summary>T044 -- section 2 is one row per EDJEr on the LATEST end date, with THAT client.</summary>
    /// <remarks>
    /// The existing tie test pins two assignments ending the SAME day. This pins the ordinary case the
    /// tie test cannot: two DIFFERENT end dates, where picking the earlier would report an EDJEr as
    /// available months before they are. FR-011 is explicit that the later date is the one they
    /// actually become available on.
    /// </remarks>
    [Fact]
    public async Task RolloutBreakdown_TwoDifferentEndDates_IsOneRowOnTheLater_WithThatClient()
    {
        // Arrange -- both current, both end-dated, so the EDJEr IS a confirmed rollout (FR-004).
        using var context = CreateContext();
        var soonClient = AddClient(context, id: 1, isInternal: false);
        var laterClient = AddClient(context, id: 2, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, soonClient, Today.AddMonths(-6), Today.AddDays(20));
        AddAssignment(context, id: 2, employee, laterClient, Today.AddMonths(-6), Today.AddDays(75));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var rows = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.Date.ShouldBe(Today.AddDays(75), "FR-011 takes the LATEST end date across current assignments");
        row.DaysUntil.ShouldBe(75);
        row.Clients.ShouldHaveSingleItem().Id.ShouldBe(
            laterClient.Id, "the client shown must be the one whose assignment carries that latest date");
    }

    /// <summary>T044a -- section 1 is one row per EDJEr on the EARLIEST internal start date.</summary>
    /// <remarks>
    /// An invariant, not a business case: an EDJEr is not expected to hold two concurrent internal
    /// assignments (owner, 2026-08-18) and nothing models it as one, but no constraint prevents it and
    /// the beach tile counts DISTINCT EDJErs -- so a per-assignment section 1 would break SC-003's
    /// equality on exactly this shape. The existing tie test covers two internal clients started the
    /// same day; this covers different days, where the earlier date is the one that says how long the
    /// person has actually been available.
    /// </remarks>
    [Fact]
    public async Task BeachBreakdown_TwoInternalAssignments_IsOneRowOnTheEarlierStart()
    {
        // Arrange -- two DIFFERENT internal clients, matching on the flag rather than an id (FR-005).
        using var context = CreateContext();
        var firstInternal = AddClient(context, id: 1, isInternal: true);
        var secondInternal = AddClient(context, id: 2, isInternal: true);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, firstInternal, Today.AddMonths(-5), endDate: null);
        AddAssignment(context, id: 2, employee, secondInternal, Today.AddMonths(-2), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var rows = await repository.GetBreakdownAsync(
            DashboardCategory.Beach, Today, TestContext.Current.CancellationToken);

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.Date.ShouldBe(
            Today.AddMonths(-5), "the EARLIEST internal start is when they went on the beach (FR-010)");
        rows.Count.ShouldBe(counts.BeachCount, "section 1's grain must match the tile's distinct-EDJEr count");
    }

    /// <summary>T045 -- a SOW 45 days out is excluded when a follow-on exists, included when not.</summary>
    /// <remarks>
    /// Both halves in one test, over the same two-assignment fixture, because the requirement is the
    /// CONTRAST: either condition alone is a defect (FR-003), and two separate tests would each pass
    /// against an implementation that ignored the other. The predicate itself is pinned by
    /// <c>ClientStatusDerivationTests</c> (T005); this pins the breakdown that composes it.
    /// </remarks>
    [Fact]
    public async Task ExpiringSowsBreakdown_At45Days_ExcludesTheOneWithAFollowOn_IncludesTheOneWithout()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var covered = AddEmployee(context, id: 1, coachId: null);
        var exposed = AddEmployee(context, id: 2, coachId: null);
        var coveredAssignment = AddAssignment(
            context, id: 1, covered, client, Today.AddMonths(-8), endDate: null);
        var exposedAssignment = AddAssignment(
            context, id: 2, exposed, client, Today.AddMonths(-8), endDate: null);
        context.Set<Sow>().AddRange(
            new Sow
            {
                Id = 1,
                ClientAssignmentId = coveredAssignment.Id,
                SowStartDate = Today.AddMonths(-8),
                SowEndDate = Today.AddDays(45),
                HasPassedApplicationValidation = true,
            },
            // The follow-on: starts AFTER the first ends, on the SAME assignment (research D-3).
            new Sow
            {
                Id = 2,
                ClientAssignmentId = coveredAssignment.Id,
                SowStartDate = Today.AddDays(46),
                SowEndDate = Today.AddDays(400),
                HasPassedApplicationValidation = true,
            },
            new Sow
            {
                Id = 3,
                ClientAssignmentId = exposedAssignment.Id,
                SowStartDate = Today.AddMonths(-8),
                SowEndDate = Today.AddDays(45),
                HasPassedApplicationValidation = true,
            });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var rows = await repository.GetBreakdownAsync(
            DashboardCategory.ExpiringSows, Today, TestContext.Current.CancellationToken);

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.EmployeeId.ShouldBe(exposed.Id, "only the SOW with NO follow-on is unconfirmed (FR-003, BR-5)");
        row.DaysUntil.ShouldBe(45, "days-until-expiration is measured from the as-of date, not the clock");
    }

    /// <summary>
    /// Issue #457 -- a SOW expiring inside the window is included only when its assignment has NO end
    /// date; an otherwise-identical SOW on an assignment that already carries an end date is excluded.
    /// </summary>
    /// <remarks>
    /// The contrast in ONE test, over two assignments that differ only by end date, because either
    /// condition alone would pass against an implementation that ignored the other. The count is
    /// asserted alongside the breakdown so the tile and its drill-down stay in lockstep (SC-003).
    /// </remarks>
    [Fact]
    public async Task ExpiringSowsBreakdown_ExcludesSowsWhoseAssignmentHasAnEndDate()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var openEnded = AddEmployee(context, id: 1, coachId: null);
        var planned = AddEmployee(context, id: 2, coachId: null);
        var openEndedAssignment = AddAssignment(
            context, id: 1, openEnded, client, Today.AddMonths(-8), endDate: null);
        var plannedAssignment = AddAssignment(
            context, id: 2, planned, client, Today.AddMonths(-8), Today.AddDays(30));
        context.Set<Sow>().AddRange(
            new Sow
            {
                Id = 1,
                ClientAssignmentId = openEndedAssignment.Id,
                SowStartDate = Today.AddMonths(-8),
                SowEndDate = Today.AddDays(45),
                HasPassedApplicationValidation = true,
            },
            new Sow
            {
                Id = 2,
                ClientAssignmentId = plannedAssignment.Id,
                SowStartDate = Today.AddMonths(-8),
                SowEndDate = Today.AddDays(45),
                HasPassedApplicationValidation = true,
            });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var rows = await repository.GetBreakdownAsync(
            DashboardCategory.ExpiringSows, Today, TestContext.Current.CancellationToken);

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.EmployeeId.ShouldBe(
            openEnded.Id, "only the SOW on the OPEN-ENDED assignment is flagged (issue #457)");
        rows.ShouldNotContain(
            r => r.EmployeeId == planned.Id, "a SOW on a planned-rollout assignment is excluded");
        counts.ExpiringSowCount.ShouldBe(1, "the tile's count must match the breakdown's row count");
    }

    /// <summary>
    /// Issue #457 -- "active" excludes a future-dated assignment: an open-ended assignment that has not
    /// started yet does not surface its expiring SOW.
    /// </summary>
    [Fact]
    public async Task ExpiringSowsBreakdown_ExcludesFutureDatedAssignments()
    {
        // Arrange -- an open-ended assignment starting tomorrow, with a SOW expiring inside the window.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var assignment = AddAssignment(
            context, id: 1, employee, client, Today.AddDays(1), endDate: null);
        context.Set<Sow>().Add(new Sow
        {
            Id = 1,
            ClientAssignmentId = assignment.Id,
            SowStartDate = Today.AddDays(1),
            SowEndDate = Today.AddDays(45),
            HasPassedApplicationValidation = true,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var rows = await repository.GetBreakdownAsync(
            DashboardCategory.ExpiringSows, Today, TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldBeEmpty("a not-yet-started assignment is not active (FR-032)");
        counts.ExpiringSowCount.ShouldBe(0);
    }

    /// <summary>T046 -- the no-coach EDJEr is visible in the THIRD report population too.</summary>
    /// <remarks>
    /// <c>NoCoachEmployee_AppearsInRolloutAndExpiringSowBreakdowns_WithNullCoach</c> above covers the
    /// report's sections 2 and 3. This covers section 1, completing FR-013's "all three sections" --
    /// which matters because J19 names this report as the compensating control for the coach
    /// notification Stream 3 skips, and a coachless EDJEr on the beach is precisely the person that
    /// control exists to surface.
    /// </remarks>
    [Fact]
    public async Task NoCoachEmployee_AppearsInTheBeachBreakdown_WithNullCoach()
    {
        // Arrange
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, internalClient, Today.AddMonths(-3), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var rows = await repository.GetBreakdownAsync(
            DashboardCategory.Beach, Today, TestContext.Current.CancellationToken);

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.EmployeeId.ShouldBe(employee.Id);
        row.CoachName.ShouldBeNull("the row is present with an empty coach field, never filtered (FR-013)");
    }

    // ------------------------------------------------------------ the closed category set

    [Fact]
    public async Task GetBreakdownAsync_UnknownCategory_Throws_RatherThanReturningAnEmptyList()
    {
        // Arrange — the route boundary parses a segment to this enum and 404s anything else, so a
        // value outside the four can only arrive from a NEW enum member whose breakdown someone
        // forgot to write. Throwing is what makes that a loud failure; returning [] would render an
        // empty panel under a real heading and read as "no rows in this category".
        using var context = CreateContext();
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var act = async () => await repository.GetBreakdownAsync(
            (DashboardCategory)999, Today, TestContext.Current.CancellationToken);

        // Assert
        var thrown = await act.ShouldThrowAsync<ArgumentOutOfRangeException>();
        thrown.ParamName.ShouldBe("category");
    }

    // ------------------------------------------------------------------ T057a — FR-032, future starts
    //
    // IsCurrent is `EndDate == null || EndDate >= today` and never reads StartDate, so it means
    // "not ended", NOT "active". Every assertion below failed before FR-032 (spec Deviation 10), and
    // none of them could have been caught by the seeded data: all 19 seeded assignments start in the
    // past. Feature 006's write surface accepts a future start date, so the shape is reachable today.

    [Fact]
    public async Task SowStartingTomorrow_StillCountsTheAlreadyActiveAssignment_AndShowsThatSowsEndDate()
    {
        // Arrange — issue #633: the tile is keyed to the ASSIGNMENT's own currency, not any one SOW's.
        // A SOW that has not yet begun no longer excludes the (already active) assignment it sits on —
        // it is still the only SOW under this assignment, so it still sets the shown end date.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var assignment = AddAssignment(context, id: 1, employee, client, Today.AddMonths(-6), endDate: null);
        AddSow(context, id: 1, assignment, Today.AddDays(1), Today.AddDays(400));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ActiveSows, Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ActiveSowCount.ShouldBe(1, "the assignment itself is already active (issue #633)");
        breakdown.ShouldHaveSingleItem().Date.ShouldBe(Today.AddDays(400));
    }

    [Fact]
    public async Task SowEndedYesterday_StillCountsTheStillOpenAssignment_AndShowsThatSowsEndDate()
    {
        // Arrange — issue #633: an assignment with no end date is still active even though its one SOW
        // already ended — the MAX-end-date column simply reports that ended SOW's date.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var assignment = AddAssignment(context, id: 1, employee, client, Today.AddMonths(-6), endDate: null);
        AddSow(context, id: 1, assignment, Today.AddMonths(-6), Today.AddDays(-1));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ActiveSows, Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ActiveSowCount.ShouldBe(1, "the open-ended assignment is still active (issue #633)");
        breakdown.ShouldHaveSingleItem().Date.ShouldBe(Today.AddDays(-1));
    }

    [Fact]
    public async Task InternalAssignmentStartingTomorrow_DoesNotPutTheEdjerOnTheBeachToday()
    {
        // Arrange — the EDJEr moves to internal allocation next week. They are not on the beach now.
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, internalClient, Today.AddDays(1), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.Beach, Today, TestContext.Current.CancellationToken);

        // Assert
        counts.BeachCount.ShouldBe(0, "a future internal allocation is not beach time today (FR-032)");
        breakdown.ShouldBeEmpty();
    }

    [Fact]
    public async Task AssignmentStartingTomorrow_DoesNotAppearInTheConfirmedRolloutsBreakdown()
    {
        // Arrange — a started assignment with an end date qualifies the EDJEr for rollout; the
        // future-start assignment must not surface as one of their rows, nor set the rollout date.
        using var context = CreateContext();
        var clientA = AddClient(context, id: 1, isInternal: false);
        var clientB = AddClient(context, id: 2, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, clientA, Today.AddMonths(-6), Today.AddDays(10));
        AddAssignment(context, id: 2, employee, clientB, Today.AddDays(1), Today.AddDays(400));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);

        // Assert
        breakdown.ShouldContain(
            r => r.Clients.Any(c => c.Id == clientA.Id),
            "the started assignment is the one they actually roll off");
        breakdown.ShouldNotContain(
            r => r.Clients.Any(c => c.Id == clientB.Id),
            "an assignment that has not begun must not set the rollout date, even though its end "
                + "date is the LATEST of the two (FR-011's correlated MAX must respect FR-032)");
    }

    [Fact]
    public async Task FutureStartWithNoEndDate_NoLongerDisqualifiesItsEdjerFromRollout()
    {
        // Arrange — THE counter-intuitive one. FR-004's condition is universal: every active
        // assignment must carry an end date. A future-start assignment with NO end date used to be
        // "active", so it broke that condition and hid a genuine rollout. Excluding it means MORE
        // EDJErs qualify after FR-032, not fewer.
        using var context = CreateContext();
        var clientA = AddClient(context, id: 1, isInternal: false);
        var clientB = AddClient(context, id: 2, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, clientA, Today.AddMonths(-6), Today.AddDays(20));
        AddAssignment(context, id: 2, employee, clientB, Today.AddDays(1), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);
        var breakdown = await repository.GetBreakdownAsync(
            DashboardCategory.ConfirmedRollouts, Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ConfirmedRolloutCount.ShouldBe(
            1, "every ACTIVE assignment carries an end date once the not-yet-begun one is excluded");
        breakdown.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AnEdjerWhoseOnlyAssignmentStartsTomorrow_IsOnNoTileAtAll()
    {
        // Arrange — the vacuous-truth trap's sibling (T022). With their only assignment excluded, this
        // EDJEr forms no group, so they must not qualify for rollout by an empty universal condition.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddDays(1), Today.AddDays(400));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ActiveSowCount.ShouldBe(0, "the future-start assignment has not begun (issue #633)");
        counts.BeachCount.ShouldBe(0);
        counts.ConfirmedRolloutCount.ShouldBe(
            0, "an EDJEr with no ACTIVE assignment must not qualify vacuously (research D-2)");
    }

    [Fact]
    public async Task AssignmentStartingExactlyToday_IsActive()
    {
        // Arrange — the inclusive start boundary, in the query rather than only in HasStarted's own
        // test (issue #633 — this tile's boundary is now the ASSIGNMENT's, not any SOW's).
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today, endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ActiveSowCount.ShouldBe(1, "an assignment starting this morning is active today");
    }

    [Fact]
    public async Task AssignmentEndingExactlyToday_IsActive()
    {
        // Arrange — the inclusive end boundary (issue #633 — was the SOW's own end date).
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddMonths(-6), Today);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new CompassDashboardRepository(context, new ClientStatusDerivation());

        // Act
        var counts = await repository.GetCountsAsync(Today, TestContext.Current.CancellationToken);

        // Assert
        counts.ActiveSowCount.ShouldBe(1, "an assignment ending tonight is active for the whole day");
    }

    private static Client AddClient(LeapDbContext context, int id, bool isInternal)
    {
        var client = new Client { Id = id, ClientName = $"Client {id}", IsInternal = isInternal };
        context.Set<Client>().Add(client);
        return client;
    }

    private static Employee AddEmployee(
        LeapDbContext context, int id, int? coachId, int employeeTypeId = EmployeeTypeId,
        string employeeTypeName = "Full Time")
    {
        // ChangeTracker, not Set<T>().Any(): the latter queries the store and misses a row this same
        // test already Add()-ed but has not yet SaveChanges-d, which duplicated the EmployeeType key
        // the first time this helper was called twice in one Arrange. Keyed on THIS id, not "any
        // lookup row exists at all" — issue #332's tests add a second, differently-typed employee in
        // the same Arrange, and the original any-check silently skipped seeding it.
        if (!context.ChangeTracker.Entries<EmployeeType>().Any(e => e.Entity.Id == employeeTypeId))
        {
            context.Set<EmployeeType>().Add(
                new EmployeeType { Id = employeeTypeId, TypeName = employeeTypeName, IsActive = true });
        }

        var employee = new Employee
        {
            Id = id,
            FirstName = "Test",
            LastName = $"Employee{id}",
            Email = $"test.employee{id}@example.test",
            HireDate = Today.AddYears(-2),
            StateOfResidence = "OH",
            EmployeeTypeId = employeeTypeId,
            CoachEmployeeId = coachId,
            IsActive = true,
        };
        context.Set<Employee>().Add(employee);
        return employee;
    }

    private static ClientAssignment AddAssignment(
        LeapDbContext context, int id, Employee employee, Client client, DateOnly start, DateOnly? endDate)
    {
        var assignment = new ClientAssignment
        {
            Id = id,
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = start,
            EndDate = endDate,
        };
        context.Set<ClientAssignment>().Add(assignment);
        return assignment;
    }

    private static Sow AddSow(
        LeapDbContext context, int id, ClientAssignment assignment, DateOnly start, DateOnly end)
    {
        var sow = new Sow
        {
            Id = id,
            ClientAssignmentId = assignment.Id,
            SowStartDate = start,
            SowEndDate = end,
            HasPassedApplicationValidation = true,
        };
        context.Set<Sow>().Add(sow);
        return sow;
    }
}
