using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

public class AdminSystemSettingEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetAll_ReturnsOk()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/admin/system-settings", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetByKey_NonExistent_ReturnsNotFound()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/admin/system-settings/nonexistent-key", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var request = new CreateSystemSettingRequest("test-key-create", "test-value", "A test setting");

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/admin/system-settings", request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<SystemSettingResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.Key.ShouldBe("test-key-create");
    }

    [Fact]
    public async Task GetByKey_AfterCreate_ReturnsOk()
    {
        // Arrange - create first
        var request = new CreateSystemSettingRequest("test-key-get", "value", "description");
        await _client.PostAsJsonAsync(
            "/api/admin/system-settings", request, TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/admin/system-settings/test-key-get", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var request = new UpdateSystemSettingRequest("new-value", "new description");

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            "/api/admin/system-settings/nonexistent-update-key", request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_AfterCreate_ReturnsOk()
    {
        // Arrange - create first
        var createRequest = new CreateSystemSettingRequest("test-key-update", "original", "original desc");
        await _client.PostAsJsonAsync(
            "/api/admin/system-settings", createRequest, TestContext.Current.CancellationToken);

        var updateRequest = new UpdateSystemSettingRequest("updated-value", "updated desc");

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            "/api/admin/system-settings/test-key-update", updateRequest, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_NonExistent_ReturnsNotFound()
    {
        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            "/api/admin/system-settings/nonexistent-delete-key", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_AfterCreate_ReturnsNoContent()
    {
        // Arrange - create first
        var request = new CreateSystemSettingRequest("test-key-delete", "to-delete", "delete me");
        await _client.PostAsJsonAsync(
            "/api/admin/system-settings", request, TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            "/api/admin/system-settings/test-key-delete", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
