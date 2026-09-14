using LeadingEDJE.Leap.Api.Platform.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.HealthChecks;

public class LiveHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy()
    {
        // Arrange
        var check = new LiveHealthCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("live", check, null, null)
        };

        // Act
        var result = await check.CheckHealthAsync(context, TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Description.ShouldBe("alive");
    }
}
