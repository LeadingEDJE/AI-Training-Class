using LeadingEDJE.Leap.Api.Platform.Data.Repositories;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

/// <summary>
/// Covers the actor-id filter on <see cref="AuditLogRepository.BrowseAsync"/> — the null / empty /
/// one / many boundaries. The three-id case is deliberate: the salvaged upstream fix hard-capped the
/// filter at two elements as a workaround for a retired MySQL provider, so this pins that the filter
/// is not element-capped.
/// </summary>
public class AuditLogRepositoryTests : IDisposable
{
    private readonly LeapDbContext _context;
    private readonly AuditLogRepository _repository;

    public AuditLogRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new LeapDbContext(options);
        _repository = new AuditLogRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SeedAsync(params string[] actors)
    {
        for (int i = 0; i < actors.Length; i++)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                EntityType = "Timesheet",
                EntityId = (i + 1).ToString(),
                Action = "Submit",
                Actor = actors[i],
                TriggeredBy = "UI",
                Reason = "r",
                Changes = "[]",
                Timestamp = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BrowseAsync_ActorIds_Null_ReturnsAllRows()
    {
        // Arrange
        await SeedAsync("a", "b");

        // Act — null means no actor filter
        var (_, totalCount) = await _repository.BrowseAsync(null, null, null, null, 1, 25);

        // Assert
        totalCount.ShouldBe(2);
    }

    [Fact]
    public async Task BrowseAsync_ActorIds_EmptyList_ReturnsZeroResults()
    {
        // Arrange
        await SeedAsync("a");

        // Act — an empty list signals "employee not found" and must not fall through to unfiltered
        var (items, totalCount) = await _repository.BrowseAsync(null, [], null, null, 1, 25);

        // Assert
        totalCount.ShouldBe(0);
        items.Count.ShouldBe(0);
    }

    [Fact]
    public async Task BrowseAsync_ActorIds_SingleId_ReturnsMatchingRows()
    {
        // Arrange
        await SeedAsync("a", "b");

        // Act
        var (items, totalCount) = await _repository.BrowseAsync(null, ["a"], null, null, 1, 25);

        // Assert
        totalCount.ShouldBe(1);
        items[0].Actor.ShouldBe("a");
    }

    [Fact]
    public async Task BrowseAsync_ActorIds_TwoIds_ReturnsRowsMatchingEither()
    {
        // Arrange — the same person logged under both their Employee.Id and Person.EdjeId
        var employeeId = Guid.NewGuid().ToString();
        var edjeId = Guid.NewGuid().ToString();
        await SeedAsync(employeeId, edjeId, "other@example.com");

        // Act
        var (items, totalCount) = await _repository.BrowseAsync(null, [employeeId, edjeId], null, null, 1, 25);

        // Assert
        totalCount.ShouldBe(2);
        items.Select(i => i.Actor).ShouldContain(employeeId);
        items.Select(i => i.Actor).ShouldContain(edjeId);
    }

    [Fact]
    public async Task BrowseAsync_ActorIds_MoreThanTwoIds_ReturnsAllMatches()
    {
        // Arrange
        await SeedAsync("a", "b", "c", "d");

        // Act — must not be capped at two elements
        var (items, totalCount) = await _repository.BrowseAsync(null, ["a", "b", "c"], null, null, 1, 25);

        // Assert
        totalCount.ShouldBe(3);
        items.Select(i => i.Actor).ShouldNotContain("d");
    }

    [Fact]
    public async Task BrowseAsync_ActorIds_CombinesWithTheDateRangeFilter()
    {
        // Arrange
        await SeedAsync("a", "a", "a");
        var cutoff = DateTime.UtcNow.AddMinutes(-1.5);

        // Act — "all stuff from Avery between these dates" is the whole point of #138
        var (_, totalCount) = await _repository.BrowseAsync(null, ["a"], cutoff, null, 1, 25);

        // Assert
        totalCount.ShouldBe(2);
    }
}
