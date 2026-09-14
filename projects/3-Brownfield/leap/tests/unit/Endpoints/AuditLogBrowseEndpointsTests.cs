using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

public class AuditLogBrowseEndpointsTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Browse_ReturnsOk_WithPaginatedResponse()
    {
        // Arrange
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await service.LogAsync(new AuditEntry(
            "Timesheet", "1", "submit",
            "submit", "user@test.com",
            "Submitted",
            [new FieldChange("Status", "Draft", "Submitted")]));

        // Act
        var response = await _client.GetAsync(
            "/api/audit-logs/browse?page=1&pageSize=10",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Browse_WithEntityTypeFilter_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/audit-logs/browse?entityType=Timesheet&page=1&pageSize=10",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Browse_WithActorFilter_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/audit-logs/browse?actor=user@test.com&page=1&pageSize=10",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Browse_WithDateFilters_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/audit-logs/browse?fromDate=2026-01-01&toDate=2026-12-31&page=1&pageSize=10",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetEntityTypes_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/audit-logs/entity-types",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetByEntity_MissingParams_ReturnsBadRequest()
    {
        // Act - no entityType or entityId
        var response = await _client.GetAsync(
            "/api/audit-logs",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetByEntity_WithParams_ReturnsOk()
    {
        // Arrange
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await service.LogAsync(new AuditEntry(
            "Employee", "42", "update",
            "update-attributes", "admin@test.com",
            "Updated attributes", []));

        // Act
        var response = await _client.GetAsync(
            "/api/audit-logs?entityType=Employee&entityId=42",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetEntityAuditTrail_MissingParams_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/audit-logs/entity",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Browse_WithEmployeeIdFilter_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/audit-logs/browse?employeeId=EMP-001&page=1&pageSize=10",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetEntityAuditTrail_WithParams_ReturnsOk()
    {
        // Act - SuperAdmin can access any entity audit trail
        var response = await _client.GetAsync(
            "/api/audit-logs/entity?entityType=Timesheet&entityId=1",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
