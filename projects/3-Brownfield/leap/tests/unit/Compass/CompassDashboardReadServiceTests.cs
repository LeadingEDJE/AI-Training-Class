using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// <see cref="CompassDashboardReadService"/> — feature 007, the business-date seam.
/// </summary>
/// <remarks>
/// <para>
/// The service is thin, and the one thing it does is the thing worth pinning. It resolves
/// <see cref="ICompassBusinessDate"/> and hands the result to the repository, so that every tile
/// count and every breakdown in a request is computed against ONE day. Its own remarks say why that
/// belongs here rather than in the handler or the repository; nothing asserted it.
/// </para>
/// <para>
/// Why this file exists at all. A 300-line file named
/// <c>CompassDashboardReadServiceTests</c> already sat beside this one and never constructed the
/// service — every test in it built a <c>CompassDashboardRepository</c>. So the class had 0% unit
/// coverage while carrying a test file bearing its name, which is worse than having none: it reads
/// as covered. That file is now <c>CompassDashboardRepositoryTests</c>, named for what it tests.
/// </para>
/// <para>
/// Hand-rolled doubles rather than a mocking framework. Both
/// record what they were asked, because "passed the business date through" is a claim about an
/// ARGUMENT — asserting only on the returned value would pass just as happily if the service
/// invented its own date.
/// </para>
/// </remarks>
public class CompassDashboardReadServiceTests
{
    private static readonly DateOnly BusinessToday = new(2026, 8, 17);

    /// <summary>A business date that is NOT today, so a service reading the clock itself fails.</summary>
    private sealed class FixedBusinessDate(DateOnly today) : ICompassBusinessDate
    {
        public int Calls { get; private set; }

        public DateOnly Today()
        {
            Calls++;
            return today;
        }
    }

    private sealed class RecordingRepository : ICompassDashboardRepository
    {
        public DateOnly? CountsDate { get; private set; }
        public DateOnly? BreakdownDate { get; private set; }
        public DashboardCategory? BreakdownCategory { get; private set; }

        public SalesDashboardDto Counts { get; init; } = new();
        public IReadOnlyList<DashboardBreakdownRowDto> Rows { get; init; } = [];

        public Task<SalesDashboardDto> GetCountsAsync(DateOnly today, CancellationToken cancellationToken)
        {
            CountsDate = today;
            return Task.FromResult(Counts);
        }

        public Task<IReadOnlyList<DashboardBreakdownRowDto>> GetBreakdownAsync(
            DashboardCategory category,
            DateOnly today,
            CancellationToken cancellationToken)
        {
            BreakdownCategory = category;
            BreakdownDate = today;
            return Task.FromResult(Rows);
        }

        /// <summary>
        /// Present only to satisfy <see cref="ICompassDashboardRepository"/>; the dashboard read service
        /// never calls the Assignment Start lookup (US4, #78). Throwing rather than returning an empty list
        /// is the point — an empty list would let a future dashboard call this by mistake and look fine.
        /// </summary>
        public Task<IReadOnlyList<AssignmentStartRowDto>> GetAssignmentStartsAsync(
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "the Sales Dashboard must not call the Assignment Start lookup");
    }

    [Fact]
    public async Task GetDashboardAsync_AsksTheRepositoryForTheBusinessDate_NotTheSystemDate()
    {
        // Arrange — the fixed date is deliberately not DateOnly.FromDateTime(DateTime.UtcNow): a
        // service that read the clock itself would return a different day and fail here.
        var repository = new RecordingRepository();
        var businessDate = new FixedBusinessDate(BusinessToday);
        var service = new CompassDashboardReadService(repository, businessDate);

        // Act
        await service.GetDashboardAsync(TestContext.Current.CancellationToken);

        // Assert
        repository.CountsDate.ShouldBe(BusinessToday);
        businessDate.Calls.ShouldBe(1, "the date is resolved once per request, not per tile");
    }

    [Fact]
    public async Task GetDashboardAsync_ReturnsTheRepositorysProjectionUnchanged()
    {
        // Arrange — the service adds no shaping of its own, and a future one that did would be a
        // second place deriving dashboard values. This pins the pass-through.
        var counts = new SalesDashboardDto
        {
            AsOfDate = BusinessToday,
            ActiveSowCount = 7,
            ExpiringSowCount = 3,
            ConfirmedRolloutCount = 2,
            BeachCount = 1,
        };
        var service = new CompassDashboardReadService(
            new RecordingRepository { Counts = counts }, new FixedBusinessDate(BusinessToday));

        // Act
        var result = await service.GetDashboardAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeSameAs(counts);
    }

    [Theory]
    [InlineData(DashboardCategory.ActiveSows)]
    [InlineData(DashboardCategory.ExpiringSows)]
    [InlineData(DashboardCategory.ConfirmedRollouts)]
    [InlineData(DashboardCategory.Beach)]
    public async Task GetBreakdownAsync_ForwardsBothTheCategoryAndTheBusinessDate(
        DashboardCategory category)
    {
        // Arrange — every category, because forwarding the wrong one would show a category's rows
        // under another's heading, which is the failure the 404-on-unknown parser exists to prevent
        // at the route boundary. Same class of defect, one layer in.
        var repository = new RecordingRepository();
        var businessDate = new FixedBusinessDate(BusinessToday);
        var service = new CompassDashboardReadService(repository, businessDate);

        // Act
        await service.GetBreakdownAsync(category, TestContext.Current.CancellationToken);

        // Assert
        repository.BreakdownCategory.ShouldBe(category);
        repository.BreakdownDate.ShouldBe(BusinessToday);
        businessDate.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task GetBreakdownAsync_ReturnsTheRepositorysRowsUnchanged()
    {
        // Arrange
        IReadOnlyList<DashboardBreakdownRowDto> rows =
            [new DashboardBreakdownRowDto { EmployeeId = 1, EmployeeName = "Ada Lovelace" }];
        var service = new CompassDashboardReadService(
            new RecordingRepository { Rows = rows }, new FixedBusinessDate(BusinessToday));

        // Act
        var result = await service.GetBreakdownAsync(
            DashboardCategory.Beach, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeSameAs(rows);
    }
}
