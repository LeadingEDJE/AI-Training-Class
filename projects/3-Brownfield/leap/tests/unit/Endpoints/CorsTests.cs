using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

public class CorsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Options_TimeCategoriesEndpoint_ReturnsCorsHeaders()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/time-categories");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOrigin).ShouldBeTrue();
        allowOrigin!.First().ShouldBe("http://localhost:5173");
        response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var allowCredentials).ShouldBeTrue();
        allowCredentials!.First().ShouldBe("true");
    }

    [Fact]
    public async Task Options_UnknownOrigin_DoesNotReturnCorsHeaders()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/time-categories");
        request.Headers.Add("Origin", "http://evil.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out _).ShouldBeFalse();
    }
}
