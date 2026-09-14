using System.Text.Json;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

public class AuditServiceTests : IDisposable
{
    private readonly IAuditService _service;
    private readonly InMemoryAuditLogRepository _repository;
    private readonly InMemoryEmployeeDirectory _directory;
    private readonly LeapDbContext _context;

    public AuditServiceTests()
    {
        _repository = new InMemoryAuditLogRepository();
        _directory = new InMemoryEmployeeDirectory();
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new LeapDbContext(options);
        _service = new AuditService(_repository, _context, _directory);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task LogAsync_CreatesEntryWithAllFields()
    {
        // Arrange
        var entry = new AuditEntry(
            EntityType: "Timesheet",
            EntityId: "42",
            Action: "admin:override",
            Actor: "system:submission",
            TriggeredBy: "bob@leadingedje.com",
            Reason: "Employee requested correction",
            Changes: [new FieldChange("TotalHours", "40.00", "42.50")]);

        // Act
        await _service.LogAsync(entry);

        // Assert
        var logs = (await _repository.GetAllAsync()).ToList();
        logs.Count.ShouldBe(1);

        AuditLog log = logs[0];
        log.EntityType.ShouldBe("Timesheet");
        log.EntityId.ShouldBe("42");
        log.Action.ShouldBe("admin:override");
        log.Actor.ShouldBe("system:submission");
        log.TriggeredBy.ShouldBe("bob@leadingedje.com");
        log.Reason.ShouldBe("Employee requested correction");
        log.Timestamp.ShouldBeGreaterThan(DateTime.MinValue);
        // The eighth field. This entry uses the seven-argument shape every Timesheet and OOTO call
        // site uses, so effective roles must come back NOT CAPTURED (null) rather than an empty set,
        // which would claim the actor held no roles — FR-043 scopes capture to Compass. The full
        // three-value contract lives in tests/unit/Compass/AuditEffectiveRolesTests.cs.
        log.EffectiveRoles.ShouldBeNull();
    }

    [Fact]
    public async Task LogAsync_SerializesFieldChangesToJson()
    {
        // Arrange
        var entry = new AuditEntry(
            EntityType: "Timesheet",
            EntityId: "42",
            Action: "admin:override",
            Actor: "admin:override",
            TriggeredBy: "alice@leadingedje.com",
            Reason: "Correcting hours",
            Changes:
            [
                new FieldChange("Monday", "8.00", "7.50"),
                new FieldChange("Tuesday", "8.00", "9.00")
            ]);

        // Act
        await _service.LogAsync(entry);

        // Assert
        var logs = (await _repository.GetAllAsync()).ToList();
        AuditLog log = logs[0];

        var changes = JsonSerializer.Deserialize<List<JsonElement>>(log.Changes);
        changes.ShouldNotBeNull();
        changes.Count.ShouldBe(2);
        changes[0].GetProperty("field").GetString().ShouldBe("Monday");
        changes[0].GetProperty("before").GetString().ShouldBe("8.00");
        changes[0].GetProperty("after").GetString().ShouldBe("7.50");
    }

    [Fact]
    public async Task LogAsync_EmptyReason_ThrowsArgumentException()
    {
        // Arrange
        var entry = new AuditEntry(
            EntityType: "Timesheet",
            EntityId: "42",
            Action: "admin:override",
            Actor: "system:submission",
            TriggeredBy: "bob@leadingedje.com",
            Reason: "",
            Changes: []);

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(() => _service.LogAsync(entry));
    }

    [Fact]
    public async Task LogAsync_NullReason_ThrowsArgumentException()
    {
        // Arrange
        var entry = new AuditEntry(
            EntityType: "Timesheet",
            EntityId: "42",
            Action: "admin:override",
            Actor: "system:submission",
            TriggeredBy: "bob@leadingedje.com",
            Reason: null!,
            Changes: []);

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(() => _service.LogAsync(entry));
    }

    [Fact]
    public async Task GetByEntityAsync_ReturnsFilteredResults()
    {
        // Arrange
        await _service.LogAsync(new AuditEntry("Timesheet", "42", "edit", "admin:override", "a@b.com", "Fix hours", []));
        await _service.LogAsync(new AuditEntry("Timesheet", "42", "approve", "system:approval", "c@b.com", "Approved", []));
        await _service.LogAsync(new AuditEntry("Timesheet", "99", "edit", "admin:override", "a@b.com", "Other timesheet", []));

        // Act
        var results = (await _service.GetByEntityAsync("Timesheet", "42")).ToList();

        // Assert
        results.Count.ShouldBe(2);
        results.ShouldAllBe(r => r.EntityId == "42");
    }

    [Fact]
    public async Task GetByEntityAsync_NoLogs_ReturnsEmpty()
    {
        // Act
        var results = (await _service.GetByEntityAsync("Timesheet", "999")).ToList();

        // Assert
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetByEntityAsync_ResultsOrderedByTimestampDescending()
    {
        // Arrange
        await _service.LogAsync(new AuditEntry("Timesheet", "42", "first", "admin:override", "a@b.com", "First action", []));
        await _service.LogAsync(new AuditEntry("Timesheet", "42", "second", "admin:override", "a@b.com", "Second action", []));

        // Act
        var results = (await _service.GetByEntityAsync("Timesheet", "42")).ToList();

        // Assert
        results.Count.ShouldBe(2);
        results[0].Timestamp.ShouldBeGreaterThanOrEqualTo(results[1].Timestamp);
    }

    [Fact]
    public async Task GetByEntityAsync_WithTpsEmployeeIdActor_ResolvesActorNameFromTps()
    {
        // Arrange - most audit entries store TpsEmployeeId as actor (ReopenAsync, ApproveAsync, etc.)
        const string tpsId = "EMP-001";
        _directory.AddEmployee(tpsId, "Jane Smith", "Jane", "Smith",
            edjeId: Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"));
        await _service.LogAsync(new AuditEntry("Timesheet", "42", "reopen", tpsId, "manager@b.com", "Reopened for correction", []));

        // Act
        var results = (await _service.GetByEntityAsync("Timesheet", "42")).ToList();

        // Assert
        results.Count.ShouldBe(1);
        results[0].ActorName.ShouldBe("Jane Smith");
    }

    [Fact]
    public async Task GetByEntityAsync_WithEdjeIdActor_ResolvesActorNameFromTps()
    {
        // Arrange - some audit entries (e.g. role changes) store EdjeId GUID as actor
        const string edjeId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        _directory.AddEmployee("EMP-001", "Jane Smith", "Jane", "Smith",
            edjeId: Guid.Parse(edjeId));
        await _service.LogAsync(new AuditEntry("Timesheet", "43", "edit", edjeId, "admin@b.com", "Admin correction", []));

        // Act
        var results = (await _service.GetByEntityAsync("Timesheet", "43")).ToList();

        // Assert
        results.Count.ShouldBe(1);
        results[0].ActorName.ShouldBe("Jane Smith");
    }

    [Fact]
    public async Task GetByEntityAsync_WithUnknownActor_FallsBackToActorValue()
    {
        // Arrange - actor has no matching employee in cache (e.g. "system:flag-update")
        const string actor = "system:flag-update";
        await _service.LogAsync(new AuditEntry("Timesheet", "42", "edit", actor, "a@b.com", "Fix hours", []));

        // Act
        var results = (await _service.GetByEntityAsync("Timesheet", "42")).ToList();

        // Assert
        results.Count.ShouldBe(1);
        results[0].ActorName.ShouldBe(actor);
    }

    [Fact]
    public async Task LogAsync_SetsTimestampToUtcNow()
    {
        // Arrange
        var before = DateTime.UtcNow;
        var entry = new AuditEntry("Timesheet", "42", "edit", "admin:override", "a@b.com", "Fix", []);

        // Act
        await _service.LogAsync(entry);

        // Assert
        var after = DateTime.UtcNow;
        var logs = (await _repository.GetAllAsync()).ToList();
        logs[0].Timestamp.ShouldBeGreaterThanOrEqualTo(before);
        logs[0].Timestamp.ShouldBeLessThanOrEqualTo(after);
    }
}
