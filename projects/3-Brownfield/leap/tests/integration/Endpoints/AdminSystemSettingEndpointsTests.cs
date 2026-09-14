using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

public class AdminSystemSettingEndpointsTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private const string BaseUrl = "/api/admin/system-settings";

    [Fact]
    public async Task GetAll_ReturnsSeededSettings()
    {
        // Arrange
        await ResetDatabaseAsync();
        // Create a setting via POST first (no seed data after truncate)
        await Client.PostAsJsonAsync(BaseUrl,
            new CreateSystemSettingRequest("data_retention_years", "7", "How long to keep data"),
            TestContext.Current.CancellationToken);

        // Act
        var response = await Client.GetAsync(BaseUrl, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = await response.Content.ReadFromJsonAsync<List<SystemSettingResponse>>(
            TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.Count.ShouldBeGreaterThanOrEqualTo(1);
        settings.ShouldContain(s => s.Key == "data_retention_years");
    }

    [Fact]
    public async Task Create_ReturnsCreatedWithSetting()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await Client.PostAsJsonAsync(BaseUrl,
            new CreateSystemSettingRequest("test_key", "test_value", "A test setting"),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var setting = await response.Content.ReadFromJsonAsync<SystemSettingResponse>(
            TestContext.Current.CancellationToken);
        setting.ShouldNotBeNull();
        setting.Key.ShouldBe("test_key");
        setting.Value.ShouldBe("test_value");
        setting.Description.ShouldBe("A test setting");
    }

    [Fact]
    public async Task GetByKey_ReturnsSpecificSetting()
    {
        // Arrange
        await ResetDatabaseAsync();
        await Client.PostAsJsonAsync(BaseUrl,
            new CreateSystemSettingRequest("lookup_key", "lookup_value", "For lookup test"),
            TestContext.Current.CancellationToken);

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}/lookup_key", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var setting = await response.Content.ReadFromJsonAsync<SystemSettingResponse>(
            TestContext.Current.CancellationToken);
        setting.ShouldNotBeNull();
        setting.Key.ShouldBe("lookup_key");
        setting.Value.ShouldBe("lookup_value");
    }

    [Fact]
    public async Task GetByKey_NotFound_Returns404()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}/nonexistent_key", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_ChangesValue()
    {
        // Arrange
        await ResetDatabaseAsync();
        await Client.PostAsJsonAsync(BaseUrl,
            new CreateSystemSettingRequest("update_key", "old_value", "To be updated"),
            TestContext.Current.CancellationToken);

        // Act
        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/update_key",
            new UpdateSystemSettingRequest("new_value", "Updated description"),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var setting = await response.Content.ReadFromJsonAsync<SystemSettingResponse>(
            TestContext.Current.CancellationToken);
        setting.ShouldNotBeNull();
        setting.Value.ShouldBe("new_value");
        setting.Description.ShouldBe("Updated description");
    }

    [Fact]
    public async Task Delete_RemovesSetting()
    {
        // Arrange
        await ResetDatabaseAsync();
        await Client.PostAsJsonAsync(BaseUrl,
            new CreateSystemSettingRequest("delete_key", "to_delete", "Will be deleted"),
            TestContext.Current.CancellationToken);

        // Act
        var response = await Client.DeleteAsync(
            $"{BaseUrl}/delete_key", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify deleted
        var getResponse = await Client.GetAsync(
            $"{BaseUrl}/delete_key", TestContext.Current.CancellationToken);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_DuplicateKey_ReturnsBadRequest()
    {
        // Arrange
        await ResetDatabaseAsync();
        await Client.PostAsJsonAsync(BaseUrl,
            new CreateSystemSettingRequest("dup_key", "first", "First insert"),
            TestContext.Current.CancellationToken);

        // Act
        var response = await Client.PostAsJsonAsync(BaseUrl,
            new CreateSystemSettingRequest("dup_key", "second", "Duplicate insert"),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
