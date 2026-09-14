using System.Net;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Phase 40 Wave 5 removed the dev-only TPS cache refresh endpoint. With live DB reads there is
/// no cache to refresh, so the route is gone. This test asserts it now returns 404.
/// </summary>
public class DevEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task RefreshTpsCache_RouteRemoved_ReturnsNotFound()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/dev/refresh-tps-cache", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
