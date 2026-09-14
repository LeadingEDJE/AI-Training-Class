using System.Net;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// Phase 40 Wave 5 removed the HTTP-era TPS status/refresh endpoints. These integration tests
/// assert the routes are gone (404) against the real MySQL-backed host.
/// </summary>
public class SystemEndpointsIntegrationTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetTpsStatus_RouteRemoved_ReturnsNotFound()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await Client.GetAsync(
            "/api/system/tps-status", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RefreshTpsCache_RouteRemoved_ReturnsNotFound()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await Client.PostAsync(
            "/api/admin/refresh-tps-cache", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
