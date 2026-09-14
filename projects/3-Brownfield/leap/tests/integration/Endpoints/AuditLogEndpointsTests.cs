using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

public class AuditLogEndpointsTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private const string BaseUrl = "/api/audit-logs";

    private async Task SeedAuditLogAsync(string entityType, string entityId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        dbContext.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = "Created",
            Actor = "test@leadingedje.com",
            TriggeredBy = "IntegrationTest",
            Reason = "Seed data for test",
            Changes = "{}",
            Timestamp = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetByEntity_WithMatchingLogs_ReturnsOkWithList()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAuditLogAsync("TimeCategory", "42");

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}?entityType=TimeCategory&entityId=42",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var logs = await response.Content.ReadFromJsonAsync<List<AuditLogResponse>>(
            TestContext.Current.CancellationToken);
        logs.ShouldNotBeNull();
        logs.Count.ShouldBe(1);
        logs[0].EntityType.ShouldBe("TimeCategory");
        logs[0].EntityId.ShouldBe("42");
        logs[0].Action.ShouldBe("Created");
    }

    [Fact]
    public async Task GetByEntity_NoMatchingLogs_ReturnsEmptyList()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}?entityType=TimeCategory&entityId=999",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var logs = await response.Content.ReadFromJsonAsync<List<AuditLogResponse>>(
            TestContext.Current.CancellationToken);
        logs.ShouldNotBeNull();
        logs.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetByEntity_MissingParameters_ReturnsBadRequest()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await Client.GetAsync(
            BaseUrl, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
