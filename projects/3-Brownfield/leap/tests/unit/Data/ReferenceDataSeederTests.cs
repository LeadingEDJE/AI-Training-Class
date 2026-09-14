using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Data.SeedData;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

public class ReferenceDataSeederTests
{
    private static LeapDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new LeapDbContext(options);
    }

    [Fact]
    public async Task SeedAsync_WhenAllTablesEmpty_SeedsProductionOotoUrl()
    {
        // Arrange
        using var context = CreateContext();
        var seeder = new ReferenceDataSeeder(context);
        var ct = TestContext.Current.CancellationToken;

        // Act
        await seeder.SeedAsync();

        // Assert
        var setting = await context.SystemSettings.SingleAsync(s => s.Key == "ooo_system_url", ct);
        setting.Value.ShouldBe("https://ooto.leadingedje.com");
    }
}
