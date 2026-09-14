using System.Net;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Phase 40 Wave 5 removed the HTTP-era TPS status/refresh machinery. TPS reads are now live
/// same-instance DB queries, so the staleness/circuit-breaker status surface no longer exists.
/// These tests assert the routes are gone (404) rather than serving a status payload.
/// </summary>
public class SystemEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetTpsStatus_RouteRemoved_ReturnsNotFound()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/system/tps-status", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RefreshTpsCache_RouteRemoved_ReturnsNotFound()
    {
        // Act
        HttpResponseMessage response = await _client.PostAsync(
            "/api/admin/refresh-tps-cache", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
