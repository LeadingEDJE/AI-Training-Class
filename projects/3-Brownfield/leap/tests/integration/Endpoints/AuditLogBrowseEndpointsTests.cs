using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

public class AuditLogBrowseEndpointsTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private const string BaseUrl = "/api/audit-logs";

    private async Task SeedAuditLogsAsync(int count, string entityType = "Timesheet")
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        for (int i = 0; i < count; i++)
        {
            dbContext.AuditLogs.Add(new AuditLog
            {
                EntityType = entityType,
                EntityId = (i + 1).ToString(),
                Action = "Updated",
                Actor = "admin@leadingedje.com",
                TriggeredBy = "IntegrationTest",
                Reason = $"Test reason {i + 1}",
                Changes = $"{{\"field\":\"{i + 1}\"}}",
                Timestamp = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Browse_ReturnsPaginatedResults()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAuditLogsAsync(10);

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}/browse", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedAuditLogResponse>(
            TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.TotalCount.ShouldBe(10);
        result.Items.Count.ShouldBe(10);
        result.Page.ShouldBe(1);
    }

    [Fact]
    public async Task Browse_FiltersByEntityType()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAuditLogsAsync(5, "Timesheet");
        await SeedAuditLogsAsync(3, "SystemSetting");

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}/browse?entityType=Timesheet", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedAuditLogResponse>(
            TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.TotalCount.ShouldBe(5);
        result.Items.ShouldAllBe(i => i.EntityType == "Timesheet");
    }

    [Fact]
    public async Task Browse_RespectsPageSize()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAuditLogsAsync(10);

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}/browse?page=1&pageSize=5", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedAuditLogResponse>(
            TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.TotalCount.ShouldBe(10);
        result.Items.Count.ShouldBe(5);
        result.PageSize.ShouldBe(5);
    }

    [Fact]
    public async Task GetEntityTypes_ReturnsDistinctTypes()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAuditLogsAsync(3, "Timesheet");
        await SeedAuditLogsAsync(2, "SystemSetting");
        await SeedAuditLogsAsync(1, "EmployeeYearBalance");

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}/entity-types", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var types = await response.Content.ReadFromJsonAsync<List<string>>(
            TestContext.Current.CancellationToken);
        types.ShouldNotBeNull();
        types.Count.ShouldBe(3);
        types.ShouldContain("Timesheet");
        types.ShouldContain("SystemSetting");
        types.ShouldContain("EmployeeYearBalance");
    }

    [Fact]
    public async Task Browse_EmptyDatabase_ReturnsEmptyPage()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}/browse", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedAuditLogResponse>(
            TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.TotalCount.ShouldBe(0);
        result.Items.ShouldBeEmpty();
    }

    private async Task SeedAuditLogAsync(string entityType, string entityId, string actor)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        dbContext.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = "Updated",
            Actor = actor,
            TriggeredBy = "IntegrationTest",
            Reason = "Test reason",
            Changes = "[]",
            Timestamp = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Browse_FilteringByUnknownEmployeeId_ReturnsEmptyNotUnfiltered()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAuditLogAsync("Timesheet", "1", "someone@leadingedje.com");

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}/browse?employeeId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Assert — legacy #130: a filter that returns everything reads as "filter not working"
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedAuditLogResponse>(
            TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Browse_UnresolvableEntity_LeavesTheDescriptionNullForUiFallback()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAuditLogAsync("Timesheet", "999999", "admin@leadingedje.com");

        // Act
        var response = await Client.GetAsync(
            $"{BaseUrl}/browse", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedAuditLogResponse>(
            TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Items[0].EntityDescription.ShouldBeNull();
    }
}
