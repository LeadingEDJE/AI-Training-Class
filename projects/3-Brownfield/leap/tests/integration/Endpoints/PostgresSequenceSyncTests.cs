using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Data.SeedData;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// Proves identity sequences are resynced after explicit-ID seeding. On Postgres,
/// inserting a row with an explicit ID does NOT advance the identity sequence
/// (unlike MySQL AUTO_INCREMENT), so the next default insert collides with 23505
/// unless the sequence is resynced to MAX(id).
/// </summary>
public class PostgresSequenceSyncTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory), IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task ResyncAsync_AfterExplicitIdInsert_NextDefaultInsertSucceeds()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        dbContext.Set<EmployeeType>().Add(new EmployeeType { Id = 1, TypeName = "Explicit Id Type" });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act - resync sequences, then insert WITHOUT an explicit id
        await PostgresSequenceSync.ResyncIdentitySequencesAsync(dbContext, TestContext.Current.CancellationToken);

        dbContext.Set<EmployeeType>().Add(new EmployeeType { TypeName = "Default Id Type" });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert - no 23505; the default insert received the next id after the explicit one
        var types = dbContext.Set<EmployeeType>().OrderBy(t => t.Id).ToList();
        types.Count.ShouldBe(2);
        types[1].Id.ShouldBe(2);
    }

    [Fact]
    public async Task ResyncAsync_OnEmptyTables_NextInsertStartsAtOne()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act - resync with all tables empty, then default insert
        await PostgresSequenceSync.ResyncIdentitySequencesAsync(dbContext, TestContext.Current.CancellationToken);

        dbContext.Set<EmployeeType>().Add(new EmployeeType { TypeName = "First Type" });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        dbContext.Set<EmployeeType>().Single().Id.ShouldBe(1);
    }
}
