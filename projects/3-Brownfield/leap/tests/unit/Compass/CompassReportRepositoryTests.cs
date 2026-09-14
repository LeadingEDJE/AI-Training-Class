using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The Client Assignment Duration query (feature 007 US3, T058–T061).
/// </summary>
/// <remarks>
/// <para>
/// This file exists because the service layer cannot hold the query.
/// <c>CompassBoundaryTests.RuleTwo</c> forbids a data context in a Compass service, and FR-021 requires
/// the summing be set-based, so the aggregation lives in the repository — which makes this the only
/// unit seam that exercises it. <c>CompassReportReadServiceTests</c> injects a fake repository and would
/// pass no matter what this query does.
/// </para>
/// <para>
/// What the InMemory provider CANNOT tell you. It evaluates the expression tree in .NET, so a
/// shape that will not translate to SQL passes here and answers HTTP 500 in a browser.
/// Translation is proved by the integration suite, not here.
/// </para>
/// </remarks>
public class CompassReportRepositoryTests
{
    private const int EmployeeTypeId = 1;
    private static readonly DateOnly Today = new(2026, 8, 17);

    private static LeapDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase($"CompassReportTestDb_{Guid.NewGuid():N}")
            .Options);

    private static CompassReportRepository Repository(LeapDbContext context) =>
        new(context, new ClientStatusDerivation());

    // ------------------------------------------------------------------ T059 — the per-pair sum

    [Fact]
    public async Task AnEdjerWhoLeftAClientAndReturned_IsOneRowSummingBothAssignments()
    {
        // Arrange — FR-015's whole point. Two non-overlapping assignments on ONE EDJEr–client pair:
        // 100 inclusive days closed, then 51 inclusive days still running.
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddDays(-400), Today.AddDays(-301));
        AddAssignment(context, id: 2, employee, client, Today.AddDays(-50), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.TotalDays.ShouldBe(
            151, "100 inclusive days for the closed leg plus 51 for the running one — the combined "
                + "tenure, not the current assignment alone");
    }

    [Fact]
    public async Task TwoDifferentClients_AreTwoRows()
    {
        // Arrange — the grain is the pair, so one EDJEr on two clients is two rows.
        using var context = CreateContext();
        var clientA = AddClient(context, id: 1);
        var clientB = AddClient(context, id: 2);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, clientA, Today.AddDays(-100), endDate: null);
        AddAssignment(context, id: 2, employee, clientB, Today.AddDays(-10), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        rows.Count.ShouldBe(2);
    }

    // ------------------------------------------------------------------ T059a — FR-032

    [Fact]
    public async Task APairWhoseOnlyAssignmentStartsTomorrow_IsAbsent()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddDays(1), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldBeEmpty("a future start is not an active assignment, so the pair does not qualify");
    }

    [Fact]
    public async Task APairWhoseOnlyAssignmentHasEnded_IsAbsent()
    {
        // Arrange — the pair must hold at least one ACTIVE assignment to appear at all (FR-014).
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddDays(-100), Today.AddDays(-10));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ T060a — the span rule

    [Fact]
    public async Task ASameDayAssignment_CountsOneDayNotZero()
    {
        // Arrange — inclusive of both endpoints (FR-015). Paired with a running assignment so the pair
        // qualifies; the same-day leg is the one under test.
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddDays(-200), Today.AddDays(-200));
        AddAssignment(context, id: 2, employee, client, Today, endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem().TotalDays.ShouldBe(
            2, "one day for the same-day leg plus one for the leg starting today — both inclusive");
    }

    [Fact]
    public async Task AnOpenEndedAssignment_RunsToTheBusinessDate()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddDays(-9), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem().TotalDays.ShouldBe(10, "nine days ago through today, inclusive");
    }

    [Fact]
    public async Task AFutureEndDate_CreditsElapsedTenureOnly_NotTheContractedSpan()
    {
        // Arrange — the decision FR-015 records. A two-year engagement signed nine days ago has served
        // ten days, and this report is ordered by longest TENURE. Crediting the contract would put a
        // brand-new engagement at the top of the report.
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddDays(-9), Today.AddYears(2));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem().TotalDays.ShouldBe(
            10, "elapsed tenure to the business date, not the contracted end date");
    }

    [Fact]
    public async Task TwoAdjacentAssignments_SumWithNoGapAndNoDoubleCount()
    {
        // Arrange — one ends the day BEFORE the next starts. Inclusive counting must total exactly the
        // elapsed span. A shared boundary DATE would double-count that day, which violates A-7 and is
        // obligation O-3's silent-inflation shape; this pins the correct adjacent case so the boundary
        // is deliberate rather than accidental.
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddDays(-9), Today.AddDays(-5));
        AddAssignment(context, id: 2, employee, client, Today.AddDays(-4), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem().TotalDays.ShouldBe(
            10, "five days plus five days — the same total as one unbroken ten-day span");
    }

    // ------------------------------------------------------------------ T060c — the zero floor

    [Fact]
    public async Task ANotYetStartedAssignmentOnAQualifyingPair_ContributesZero()
    {
        // Arrange — the pair qualifies on a started assignment; a second, future-dated one to the same
        // client must add nothing rather than a NEGATIVE span (FR-015's floor).
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddDays(-9), endDate: null);
        AddAssignment(context, id: 2, employee, client, Today.AddDays(30), Today.AddDays(400));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem().TotalDays.ShouldBe(
            10, "the not-yet-started leg contributes zero, never a negative span");
    }

    // ------------------------------------------------------------------ T060b — no coach

    [Fact]
    public async Task AnEdjerWithNoCoach_IsPresentWithANullCoach()
    {
        // Arrange — never filtered (spec Edge Cases, FR-013's rule applied to this surface).
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddDays(-100), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem().CoachName.ShouldBeNull();
    }

    [Fact]
    public async Task TheRow_CarriesEmployeeClientAndCoachNames()
    {
        // Arrange — FR-014's four displayed fields. Named by six artifacts and by no build task until
        // 2026-08-19, which is why this asserts them rather than assuming.
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var coach = AddEmployee(context, id: 1, coachId: null);
        var employee = AddEmployee(context, id: 2, coachId: coach.Id);
        AddAssignment(context, id: 1, employee, client, Today.AddDays(-100), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.EmployeeName.ShouldBe("Test Employee2");
        row.ClientName.ShouldBe("Client 1");
        row.CoachName.ShouldBe("Test Employee1");
    }

    // ------------------------------------------------------------------ T058/T061 — ordering

    [Fact]
    public async Task Rows_AreOrderedByTotalDaysDescending()
    {
        // Arrange — longest tenure first (FR-014). 10 yrs vs 3 yrs is the pair that exposes ordering on
        // the display STRING instead of the day count: "10 yrs" sorts before "3 yrs" lexically, which
        // looks right and is wrong.
        using var context = CreateContext();
        var shortClient = AddClient(context, id: 1);
        var longClient = AddClient(context, id: 2);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, shortClient, Today.AddYears(-3), endDate: null);
        AddAssignment(context, id: 2, employee, longClient, Today.AddYears(-10), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        rows.Select(r => r.ClientName).ShouldBe(["Client 2", "Client 1"]);
        rows[0].TotalDays.ShouldBeGreaterThan(rows[1].TotalDays);
    }

    // ------------------------------------------------------------------ Issue #335 — external only,
    //                                                                     employee type on the row

    /// <summary>
    /// A pair whose only active assignment is at an internal ("beach") client does not qualify — this
    /// report is scoped to external client assignments only (issue #335).
    /// </summary>
    [Fact]
    public async Task GetAssignmentDurationAsync_ExcludesAPairWhoseOnlyActiveAssignmentIsInternal()
    {
        // Arrange
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, internalClient, Today.AddDays(-100), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldBeEmpty();
    }

    /// <summary>
    /// An EDJEr holding both an internal and an external assignment still appears, carrying only the
    /// external pair's tenure — the internal leg contributes nothing to the row (issue #335).
    /// </summary>
    [Fact]
    public async Task GetAssignmentDurationAsync_KeepsTheExternalPair_ForAnEdjerHoldingBoth()
    {
        // Arrange
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var externalClient = AddClient(context, id: 2, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, internalClient, Today.AddDays(-1000), endDate: null);
        AddAssignment(context, id: 2, employee, externalClient, Today.AddDays(-99), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.ClientId.ShouldBe(externalClient.Id);
        row.TotalDays.ShouldBe(100);
    }

    /// <summary>The row carries the EDJEr's employee type, rendered next to their name (issue #335).</summary>
    [Fact]
    public async Task GetAssignmentDurationAsync_PopulatesEmployeeType()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, Today.AddDays(-100), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var rows = await Repository(context).GetAssignmentDurationAsync(
            Today, TestContext.Current.CancellationToken);

        // Assert — the AddEmployee helper seeds the one EmployeeType as "Full Time".
        rows.ShouldHaveSingleItem().EmployeeType.ShouldBe("Full Time");
    }

    // The Assignment Start lookup moved here from CompassDashboardRepositoryTests when the two report
    // queries were consolidated onto CompassReportRepository (2026-08-20). The tests are unchanged apart
    // from the repository they build -- a pure move, so a behaviour change here would be a defect in the
    // consolidation rather than in them.
    // ------------------------------------------------------------------ US4 / AC-40 — Assignment Start

    /// <summary>The range is inclusive at BOTH ends, and excludes the days either side of it.</summary>
    /// <remarks>
    /// The half-open upper bound is the easy mistake, and it is invisible in casual use: it drops only
    /// the assignments that started on the last day of the range, which is often the day the user cares
    /// most about. Rows sit exactly on both edges and exactly one day outside each.
    ///
    /// This runs against the EF InMemory provider, so it proves the FILTER and nothing about SQL
    /// translation — only a real-PostgreSQL test can establish that a query runs at all.
    /// `CompassReportEndpointsTests` carries that half.
    /// </remarks>
    [Fact]
    public async Task GetAssignmentStartsAsync_IsInclusiveAtBothEnds()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, new DateOnly(2026, 2, 28), endDate: null);
        AddAssignment(context, id: 2, employee, client, new DateOnly(2026, 3, 1), endDate: null);
        AddAssignment(context, id: 3, employee, client, new DateOnly(2026, 3, 31), endDate: null);
        AddAssignment(context, id: 4, employee, client, new DateOnly(2026, 4, 1), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetAssignmentStartsAsync(
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31),
            TestContext.Current.CancellationToken);

        // Assert
        rows.Select(r => r.StartDate).ShouldBe(
            [new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)],
            "both edges are inside the range and the days either side are not");
    }

    /// <summary>A single-day range where from == to returns that day's assignments.</summary>
    [Fact]
    public async Task GetAssignmentStartsAsync_FromEqualsTo_ReturnsThatDay()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, new DateOnly(2026, 3, 15), endDate: null);
        AddAssignment(context, id: 2, employee, client, new DateOnly(2026, 3, 16), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetAssignmentStartsAsync(
            new DateOnly(2026, 3, 15),
            new DateOnly(2026, 3, 15),
            TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem().StartDate.ShouldBe(new DateOnly(2026, 3, 15));
    }

    /// <summary>Rows carry the EDJEr's full name, their employee type, the client's name and the start date.</summary>
    [Fact]
    public async Task GetAssignmentStartsAsync_ProjectsTheFourContractColumns()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, new DateOnly(2026, 3, 15), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetAssignmentStartsAsync(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            TestContext.Current.CancellationToken);

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.EmployeeName.ShouldBe($"{employee.FirstName} {employee.LastName}");
        row.ClientName.ShouldBe(client.ClientName);
        row.StartDate.ShouldBe(new DateOnly(2026, 3, 15));
    }

    /// <summary>
    /// The row carries the EDJEr's employee type, as its own column following the name (issue #386) —
    /// the same treatment the Client Assignment Duration report gives it.
    /// </summary>
    [Fact]
    public async Task GetAssignmentStartsAsync_PopulatesEmployeeType()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, new DateOnly(2026, 3, 15), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetAssignmentStartsAsync(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            TestContext.Current.CancellationToken);

        // Assert — the AddEmployee helper seeds the one EmployeeType as "Full Time".
        rows.ShouldHaveSingleItem().EmployeeType.ShouldBe("Full Time");
    }

    /// <summary>Ordered earliest-first, with the EDJEr name breaking a same-day tie.</summary>
    /// <remarks>
    /// The tiebreak is not decoration. The contract fixes no order for this lookup, and without a
    /// deterministic second key two assignments starting on the same day could swap places between
    /// calls — which would make any test asserting a whole list intermittently fail, and would make the
    /// screen appear to reshuffle on refresh for no reason a user could explain.
    /// </remarks>
    [Fact]
    public async Task GetAssignmentStartsAsync_OrdersEarliestFirst_ThenByEdjerName()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var zoe = AddEmployee(context, id: 1, coachId: null);
        zoe.FirstName = "Zoe";
        var ada = AddEmployee(context, id: 2, coachId: null);
        ada.FirstName = "Ada";
        AddAssignment(context, id: 1, zoe, client, new DateOnly(2026, 3, 15), endDate: null);
        AddAssignment(context, id: 2, ada, client, new DateOnly(2026, 3, 15), endDate: null);
        AddAssignment(context, id: 3, zoe, client, new DateOnly(2026, 3, 1), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetAssignmentStartsAsync(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            TestContext.Current.CancellationToken);

        // Assert
        rows.Select(r => (r.StartDate, r.EmployeeName)).ShouldBe(
        [
            (new DateOnly(2026, 3, 1), $"{zoe.FirstName} {zoe.LastName}"),
            (new DateOnly(2026, 3, 15), $"{ada.FirstName} {ada.LastName}"),
            (new DateOnly(2026, 3, 15), $"{zoe.FirstName} {zoe.LastName}"),
        ]);
    }

    /// <summary>A range matching nothing is an empty list, never null.</summary>
    [Fact]
    public async Task GetAssignmentStartsAsync_MatchingNothing_IsEmptyNotNull()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, client, new DateOnly(2026, 3, 15), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetAssignmentStartsAsync(
            new DateOnly(2030, 1, 1),
            new DateOnly(2030, 12, 31),
            TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldNotBeNull();
        rows.ShouldBeEmpty();
    }

    /// <summary>Internal-client assignments are included — this lookup filters on DATE and nothing else.</summary>
    /// <remarks>
    /// Worth pinning because the surrounding queries in this repository all care about
    /// <c>Client.IsInternal</c>, so excluding internal clients here is the plausible wrong instinct. The
    /// mockup's own RPT-5 sample rows show `EDJE Internal` twice.
    /// </remarks>
    [Fact]
    public async Task GetAssignmentStartsAsync_IncludesInternalClientAssignments()
    {
        // Arrange
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(context, id: 1, employee, internalClient, new DateOnly(2026, 3, 15), endDate: null);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetAssignmentStartsAsync(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem().ClientName.ShouldBe(internalClient.ClientName);
    }

    /// <summary>An ENDED assignment still appears — the lookup asks when work STARTED.</summary>
    [Fact]
    public async Task GetAssignmentStartsAsync_IncludesAssignmentsThatHaveSinceEnded()
    {
        // Arrange -- "started in this range" says nothing about whether it is still running, and
        // filtering by current status would quietly answer a narrower question.
        using var context = CreateContext();
        var client = AddClient(context, id: 1, isInternal: false);
        var employee = AddEmployee(context, id: 1, coachId: null);
        AddAssignment(
            context, id: 1, employee, client, new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 30));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetAssignmentStartsAsync(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem().StartDate.ShouldBe(new DateOnly(2026, 3, 15));
    }

    // ------------------------------------------------------------------ SOW Extension Report (issue #534)

    /// <summary>
    /// Only extension SOWs qualify — an initial-contract period starting in the same range is excluded.
    /// </summary>
    [Fact]
    public async Task GetSowExtensionsAsync_OnlyIncludesExtensionType()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var assignment = AddAssignment(context, id: 1, employee, client, Today.AddDays(-400), endDate: null);
        AddSow(context, id: 1, assignment, SowType.InitialContract, new DateOnly(2026, 3, 1), new DateOnly(2026, 5, 31));
        AddSow(context, id: 2, assignment, SowType.SowExtension, new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetSowExtensionsAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem().ExtensionStartDate.ShouldBe(new DateOnly(2026, 6, 1));
    }

    /// <summary>A legacy-migrated SOW is never an extension (<c>Sow</c>'s remarks) and never qualifies.</summary>
    [Fact]
    public async Task GetSowExtensionsAsync_ExcludesLegacyMigratedType()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var assignment = AddAssignment(context, id: 1, employee, client, Today.AddDays(-400), endDate: null);
        AddSow(context, id: 1, assignment, SowType.LegacyMigrated, new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetSowExtensionsAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldBeEmpty();
    }

    /// <summary>Inclusive at both ends, matching the Assignment Start lookup's contract.</summary>
    [Fact]
    public async Task GetSowExtensionsAsync_IsInclusiveAtBothEnds()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var assignment = AddAssignment(context, id: 1, employee, client, Today.AddDays(-400), endDate: null);
        AddSow(context, id: 1, assignment, SowType.SowExtension, new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 1));
        AddSow(context, id: 2, assignment, SowType.SowExtension, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5));
        AddSow(context, id: 3, assignment, SowType.SowExtension, new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 5));
        AddSow(context, id: 4, assignment, SowType.SowExtension, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 10));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetSowExtensionsAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), TestContext.Current.CancellationToken);

        // Assert
        rows.Select(r => r.ExtensionStartDate).ShouldBe(
            [new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)],
            "both edges are inside the range and the days either side are not");
    }

    /// <summary>Rows carry the EDJEr's full name, their employee type, the client's name and the extension start date.</summary>
    [Fact]
    public async Task GetSowExtensionsAsync_ProjectsTheFourContractColumns()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var assignment = AddAssignment(context, id: 1, employee, client, Today.AddDays(-400), endDate: null);
        AddSow(context, id: 1, assignment, SowType.SowExtension, new DateOnly(2026, 3, 15), new DateOnly(2026, 6, 30));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetSowExtensionsAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);

        // Assert
        var row = rows.ShouldHaveSingleItem();
        row.EmployeeName.ShouldBe($"{employee.FirstName} {employee.LastName}");
        row.EmployeeType.ShouldBe("Full Time");
        row.ClientName.ShouldBe(client.ClientName);
        row.ExtensionStartDate.ShouldBe(new DateOnly(2026, 3, 15));
    }

    /// <summary>Ordered oldest-first, with the EDJEr name breaking a same-day tie (owner confirmation, issue #534).</summary>
    [Fact]
    public async Task GetSowExtensionsAsync_OrdersOldestFirst_ThenByEdjerName()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var zoe = AddEmployee(context, id: 1, coachId: null);
        zoe.FirstName = "Zoe";
        var ada = AddEmployee(context, id: 2, coachId: null);
        ada.FirstName = "Ada";
        var zoeAssignment = AddAssignment(context, id: 1, zoe, client, Today.AddDays(-400), endDate: null);
        var adaAssignment = AddAssignment(context, id: 2, ada, client, Today.AddDays(-400), endDate: null);
        AddSow(context, id: 1, zoeAssignment, SowType.SowExtension, new DateOnly(2026, 3, 15), new DateOnly(2026, 6, 30));
        AddSow(context, id: 2, adaAssignment, SowType.SowExtension, new DateOnly(2026, 3, 15), new DateOnly(2026, 6, 30));
        AddSow(context, id: 3, zoeAssignment, SowType.SowExtension, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 14));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetSowExtensionsAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);

        // Assert
        rows.Select(r => (r.ExtensionStartDate, r.EmployeeName)).ShouldBe(
        [
            (new DateOnly(2026, 3, 1), $"{zoe.FirstName} {zoe.LastName}"),
            (new DateOnly(2026, 3, 15), $"{ada.FirstName} {ada.LastName}"),
            (new DateOnly(2026, 3, 15), $"{zoe.FirstName} {zoe.LastName}"),
        ]);
    }

    /// <summary>A range matching nothing is an empty list, never null.</summary>
    [Fact]
    public async Task GetSowExtensionsAsync_MatchingNothing_IsEmptyNotNull()
    {
        // Arrange
        using var context = CreateContext();
        var client = AddClient(context, id: 1);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var assignment = AddAssignment(context, id: 1, employee, client, Today.AddDays(-400), endDate: null);
        AddSow(context, id: 1, assignment, SowType.SowExtension, new DateOnly(2026, 3, 15), new DateOnly(2026, 6, 30));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetSowExtensionsAsync(
            new DateOnly(2030, 1, 1), new DateOnly(2030, 12, 31), TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldNotBeNull();
        rows.ShouldBeEmpty();
    }

    /// <summary>Internal-client extensions are included — this lookup filters on SOW type and date, nothing else.</summary>
    [Fact]
    public async Task GetSowExtensionsAsync_IncludesInternalClientAssignments()
    {
        // Arrange
        using var context = CreateContext();
        var internalClient = AddClient(context, id: 1, isInternal: true);
        var employee = AddEmployee(context, id: 1, coachId: null);
        var assignment = AddAssignment(
            context, id: 1, employee, internalClient, Today.AddDays(-400), endDate: null);
        AddSow(context, id: 1, assignment, SowType.SowExtension, new DateOnly(2026, 3, 15), new DateOnly(2026, 6, 30));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = Repository(context);

        // Act
        var rows = await repository.GetSowExtensionsAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem().ClientName.ShouldBe(internalClient.ClientName);
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>
    /// A client. <paramref name="isInternal"/> defaults false, matching how most tests in this file use
    /// it. The Duration query now EXCLUDES internal clients (issue #335) and the Assignment Start
    /// lookup INCLUDES them — the parameter exists so both can be asserted from the same helper.
    /// </summary>
    private static Client AddClient(LeapDbContext context, int id, bool isInternal = false)
    {
        var client = new Client { Id = id, ClientName = $"Client {id}", IsInternal = isInternal };
        context.Set<Client>().Add(client);
        return client;
    }

    private static Employee AddEmployee(LeapDbContext context, int id, int? coachId)
    {
        if (!context.ChangeTracker.Entries<EmployeeType>().Any())
        {
            context.Set<EmployeeType>().Add(
                new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        }

        var employee = new Employee
        {
            Id = id,
            FirstName = "Test",
            LastName = $"Employee{id}",
            Email = $"test.employee{id}@example.test",
            HireDate = Today.AddYears(-20),
            StateOfResidence = "OH",
            EmployeeTypeId = EmployeeTypeId,
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

    private static void AddSow(
        LeapDbContext context,
        int id,
        ClientAssignment assignment,
        SowType sowType,
        DateOnly startDate,
        DateOnly endDate)
        => context.Set<Sow>().Add(new Sow
        {
            Id = id,
            ClientAssignmentId = assignment.Id,
            SowType = sowType,
            SowStartDate = startDate,
            SowEndDate = endDate,
            HasPassedApplicationValidation = true,
        });
}
