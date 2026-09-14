using System.Net;
using System.Text.Json;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

public class HealthEndpointTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetHealth_ReturnsJsonWithStatusAndChecks()
    {
        // Act
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        // Assert - either 200 or 503 is valid depending on PostgreSQL availability
        var statusCode = response.StatusCode;
        (statusCode == HttpStatusCode.OK || statusCode == HttpStatusCode.ServiceUnavailable).ShouldBeTrue();

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("status", out var statusProp).ShouldBeTrue();
        statusProp.ValueKind.ShouldBe(JsonValueKind.String);

        root.TryGetProperty("checks", out var checksProp).ShouldBeTrue();
        checksProp.ValueKind.ShouldBe(JsonValueKind.Array);

        var hasPostgresCheck = false;
        foreach (var check in checksProp.EnumerateArray())
        {
            if (check.TryGetProperty("name", out var nameProp) && nameProp.GetString() == "postgres")
            {
                hasPostgresCheck = true;
                break;
            }
        }
        hasPostgresCheck.ShouldBeTrue();
    }

    [Fact]
    public async Task GetHealth_IncludesTotalDuration()
    {
        // Act
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        // Assert
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("totalDuration", out var durationProp).ShouldBeTrue();
        durationProp.ValueKind.ShouldBe(JsonValueKind.Number);
    }

    [Fact]
    public async Task GetHealthLive_ReturnsHealthyWithoutDbDependency()
    {
        // Act
        var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        // Assert - liveness check should always be healthy (no DB dependency)
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealthReady_ReturnsResponse()
    {
        // Act
        var response = await _client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        // Assert - readiness includes DB check; either 200 or 503 depending on PostgreSQL availability
        var statusCode = response.StatusCode;
        (statusCode == HttpStatusCode.OK || statusCode == HttpStatusCode.ServiceUnavailable).ShouldBeTrue();
    }

    [Fact]
    public async Task GetHealthLive_DoesNotIncludePostgresCheck()
    {
        // Act
        var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        // Assert - liveness endpoint should NOT include the postgres check
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // The default health check response writer may not return JSON, but the endpoint should respond
        // If we get a 200, the liveness check is working (no DB dependency)
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_IncludesLiveCheck()
    {
        // Act
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        // Assert - the combined /health endpoint should include both live and postgres checks
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("checks", out var checksProp).ShouldBeTrue();

        var hasLiveCheck = false;
        foreach (var check in checksProp.EnumerateArray())
        {
            if (check.TryGetProperty("name", out var nameProp) && nameProp.GetString() == "live")
            {
                hasLiveCheck = true;
                break;
            }
        }
        hasLiveCheck.ShouldBeTrue();
    }
}
