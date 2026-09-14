using System.Net;
using System.Text.Json;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

public class OpenApiEndpointTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetOpenApiSpec_ReturnsOkWithJson()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }

    [Fact]
    public async Task GetOpenApiSpec_ContainsOpenApi31Version()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        string content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var doc = JsonDocument.Parse(content);

        // Assert
        doc.RootElement.TryGetProperty("openapi", out JsonElement versionElement).ShouldBeTrue();
        versionElement.GetString()!.ShouldStartWith("3.1");
    }

    [Fact]
    public async Task GetScalarUi_ReturnsOk()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync("/scalar/v1", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
