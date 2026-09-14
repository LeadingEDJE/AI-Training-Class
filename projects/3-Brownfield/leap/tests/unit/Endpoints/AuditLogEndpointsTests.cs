using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

public class AuditLogEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetByEntity_WithValidParams_ReturnsOk()
    {
        // Arrange - seed an audit log via the service
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await service.LogAsync(new AuditEntry(
            "Timesheet", "42", "admin:override",
            "admin:override", "test@example.com",
            "Correcting hours",
            [new FieldChange("TotalHours", "40.00", "42.50")]));

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/audit-logs?entityType=Timesheet&entityId=42",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var logs = JsonSerializer.Deserialize<JsonElement[]>(content);
        logs.ShouldNotBeNull();
        logs.Length.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetByEntity_WithoutParams_ReturnsBadRequest()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/audit-logs",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetByEntity_MissingEntityId_ReturnsBadRequest()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/audit-logs?entityType=Timesheet",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetByEntity_NoMatchingLogs_ReturnsEmptyArray()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/audit-logs?entityType=Nonexistent&entityId=999",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var logs = JsonSerializer.Deserialize<JsonElement[]>(content);
        logs.ShouldNotBeNull();
        logs.Length.ShouldBe(0);
    }

    [Fact]
    public async Task GetEntityAuditTrail_WithValidTimesheetEntity_ReturnsOk()
    {
        // Arrange - seed an audit log for a timesheet entity
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await service.LogAsync(new AuditEntry(
            "Timesheet", "100", "submit",
            "submit", "test@example.com",
            "Submitted timesheet",
            [new FieldChange("Status", "Draft", "Submitted")]));

        // Act - hit the EDJEr-accessible entity endpoint
        HttpResponseMessage response = await _client.GetAsync(
            "/api/audit-logs/entity?entityType=Timesheet&entityId=100",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var logs = JsonSerializer.Deserialize<JsonElement[]>(content);
        logs.ShouldNotBeNull();
        logs.Length.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetEntityAuditTrail_MissingParams_ReturnsBadRequest()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/audit-logs/entity",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetEntityAuditTrail_WithEdjeIdActor_ReturnsResolvedActorName()
    {
        // Arrange - the actor is stored as the EDJE identity GUID; IEmployeeDirectory resolves it.
        var edjeId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        db.People.Add(new Person { Id = Guid.NewGuid(), EdjeId = edjeId, FirstName = "Alice", LastName = "Tester", IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await auditService.LogAsync(new AuditEntry(
            "Timesheet", "200", "Reopen",
            edjeId.ToString(), "ManagerAction",
            "Reopened for correction",
            [new FieldChange("Status", "Approved", "Draft")]));

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/audit-logs/entity?entityType=Timesheet&entityId=200",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var logs = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        logs.ShouldNotBeNull();
        logs.Length.ShouldBeGreaterThanOrEqualTo(1);
        var entry = logs.First(l => l.GetProperty("entityId").GetString() == "200");
        entry.GetProperty("actorName").GetString().ShouldBe("Alice Tester");
    }

    [Fact]
    public async Task GetByEntity_WithEdjeIdActor_ReturnsResolvedActorName()
    {
        // Arrange - actor stored as the EDJE identity GUID (the real production scenario)
        var edjeId = Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        db.People.Add(new Person { Id = Guid.NewGuid(), EdjeId = edjeId, FirstName = "Bob", LastName = "Reviewer", IsActive = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await auditService.LogAsync(new AuditEntry(
            "Timesheet", "201", "Reopen",
            edjeId.ToString(), "ManagerAction",
            "Admin correction",
            [new FieldChange("TotalHours", "40.00", "42.50")]));

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            "/api/audit-logs?entityType=Timesheet&entityId=201",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var logs = await response.Content.ReadFromJsonAsync<JsonElement[]>(
            TestContext.Current.CancellationToken);
        logs.ShouldNotBeNull();
        logs.Length.ShouldBeGreaterThanOrEqualTo(1);
        var entry = logs.First(l => l.GetProperty("entityId").GetString() == "201");
        entry.GetProperty("actorName").GetString().ShouldBe("Bob Reviewer");
    }
}
