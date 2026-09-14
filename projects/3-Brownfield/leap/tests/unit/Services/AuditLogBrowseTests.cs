using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Services;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

public class AuditLogBrowseTests : IDisposable
{
    private readonly AuditService _service;
    private readonly InMemoryAuditLogRepository _repository;
    private readonly InMemoryEmployeeDirectory _directory;
    private readonly LeapDbContext _context;

    public AuditLogBrowseTests()
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

    private async Task SeedAuditLogs()
    {
        // Seed 10 audit logs with varying entity types, actors, and timestamps
        for (int i = 1; i <= 10; i++)
        {
            var entityType = i <= 5 ? "Timesheet" : "EmployeeAttribute";
            var actor = i % 2 == 0 ? "admin@leadingedje.com" : "system:approval";
            await _service.LogAsync(new AuditEntry(
                entityType, i.ToString(), "edit", actor, "user@leadingedje.com",
                $"Action {i}", [new FieldChange("Field", "before", "after")]));
        }
    }

    [Fact]
    public async Task BrowseAsync_ReturnsPaginatedResults_Page1()
    {
        // Arrange
        await SeedAuditLogs();

        // Act
        var result = await _service.BrowseAsync(null, null, null, null, null, page: 1, pageSize: 3);

        // Assert
        result.Items.Count.ShouldBe(3);
        result.TotalCount.ShouldBe(10);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(3);
    }

    [Fact]
    public async Task BrowseAsync_FiltersByEntityType()
    {
        // Arrange
        await SeedAuditLogs();

        // Act
        var result = await _service.BrowseAsync("Timesheet", null, null, null, null, page: 1, pageSize: 25);

        // Assert
        result.Items.Count.ShouldBe(5);
        result.TotalCount.ShouldBe(5);
        result.Items.ShouldAllBe(i => i.EntityType == "Timesheet");
    }

    [Fact]
    public async Task BrowseAsync_FiltersByDateRange()
    {
        // Arrange
        await SeedAuditLogs();
        var now = DateTime.UtcNow;

        // Act - All entries are created "now", so a range including now should find them
        var result = await _service.BrowseAsync(null, null, null, now.AddMinutes(-1), now.AddMinutes(1), page: 1, pageSize: 25);

        // Assert
        result.TotalCount.ShouldBe(10);
    }

    [Fact]
    public async Task BrowseAsync_FiltersByActor()
    {
        // Arrange
        await SeedAuditLogs();

        // Act
        var result = await _service.BrowseAsync(null, "admin@leadingedje.com", null, null, null, page: 1, pageSize: 25);

        // Assert
        result.TotalCount.ShouldBe(5);
        result.Items.ShouldAllBe(i => i.Actor == "admin@leadingedje.com");
    }

    [Fact]
    public async Task BrowseAsync_ReturnsCorrectTotalCount()
    {
        // Arrange
        await SeedAuditLogs();

        // Act
        var result = await _service.BrowseAsync(null, null, null, null, null, page: 1, pageSize: 5);

        // Assert
        result.TotalCount.ShouldBe(10);
        result.Items.Count.ShouldBe(5);
    }

    [Fact]
    public async Task BrowseAsync_NoFilters_ReturnsAllDescendingByTimestamp()
    {
        // Arrange
        await SeedAuditLogs();

        // Act
        var result = await _service.BrowseAsync(null, null, null, null, null, page: 1, pageSize: 25);

        // Assert
        result.TotalCount.ShouldBe(10);
        result.Items.Count.ShouldBe(10);
        // Verify descending order by timestamp
        for (int i = 0; i < result.Items.Count - 1; i++)
        {
            result.Items[i].Timestamp.ShouldBeGreaterThanOrEqualTo(result.Items[i + 1].Timestamp);
        }
    }

    [Fact]
    public async Task GetDistinctEntityTypesAsync_ReturnsUniqueTypes()
    {
        // Arrange
        await SeedAuditLogs();

        // Act
        var types = await _service.GetDistinctEntityTypesAsync();

        // Assert
        types.Count.ShouldBe(2);
        types.ShouldContain("Timesheet");
        types.ShouldContain("EmployeeAttribute");
        // Should be ordered alphabetically
        types[0].ShouldBe("EmployeeAttribute");
        types[1].ShouldBe("Timesheet");
    }

    [Fact]
    public async Task BrowseAsync_ResolvesActorName_WhenTpsEmployeeExists()
    {
        // Arrange
        var actorGuid = Guid.NewGuid();
        _directory.AddEmployee("EMP-001", "Avery Quinn", "Avery", "Quinn", edjeId: actorGuid);
        var actorGuidString = actorGuid.ToString();
        await _service.LogAsync(new AuditEntry(
            "Timesheet", "1", "edit", actorGuidString, "system",
            "Test action", [new FieldChange("Field", "before", "after")]));

        // Act
        var result = await _service.BrowseAsync(null, null, null, null, null, page: 1, pageSize: 25);

        // Assert
        result.Items.Count.ShouldBe(1);
        result.Items[0].ActorName.ShouldBe("Avery Quinn");
        result.Items[0].Actor.ShouldBe(actorGuidString);
    }

    [Fact]
    public async Task BrowseAsync_ReturnsRawGuidAsActorName_WhenNoCachedEmployee()
    {
        // Arrange
        var actorGuid = Guid.NewGuid().ToString();
        await _service.LogAsync(new AuditEntry(
            "Timesheet", "1", "edit", actorGuid, "system",
            "Test action", [new FieldChange("Field", "before", "after")]));

        // Act
        var result = await _service.BrowseAsync(null, null, null, null, null, page: 1, pageSize: 25);

        // Assert
        result.Items.Count.ShouldBe(1);
        result.Items[0].ActorName.ShouldBe(actorGuid);
    }

    [Fact]
    public async Task BrowseAsync_FiltersByEmployeeId_MatchesBothStoredActorFormats()
    {
        // Arrange — post-Phase-43 both identifiers are GUIDs: Employee.Id and Person.EdjeId.
        // Audit rows are written under whichever identity was on the session at log time, so one
        // person's history is split across both forms.
        var employeeId = Guid.NewGuid();
        var edjeId = Guid.NewGuid();
        _directory.AddEmployee(employeeId.ToString(), "Avery Quinn", "Avery", "Quinn", edjeId: edjeId);

        await _service.LogAsync(new AuditEntry("Timesheet", "1", "edit", employeeId.ToString(), "system",
            "Logged under Employee.Id", []));
        await _service.LogAsync(new AuditEntry("Timesheet", "2", "edit", edjeId.ToString(), "system",
            "Logged under Person.EdjeId", []));
        await _service.LogAsync(new AuditEntry("Timesheet", "3", "edit", "other@example.com", "system",
            "Different actor", []));

        // Act — filter by the Employee.Id form; the service must widen to both stored forms
        var result = await _service.BrowseAsync(null, null, employeeId.ToString(), null, null, page: 1, pageSize: 25);

        // Assert — both of Avery's entries returned, the third excluded
        result.TotalCount.ShouldBe(2);
        result.Items.Select(i => i.Actor).ShouldContain(employeeId.ToString());
        result.Items.Select(i => i.Actor).ShouldContain(edjeId.ToString());
    }

    [Fact]
    public async Task BrowseAsync_FiltersByEmployeeId_WidensFromTheEdjeIdForm()
    {
        // Arrange — the picker sends `edjeId ?? id`, so the EdjeId form is the common inbound case
        // and must widen to the Employee.Id form just the same.
        var employeeId = Guid.NewGuid();
        var edjeId = Guid.NewGuid();
        _directory.AddEmployee(employeeId.ToString(), "Avery Quinn", "Avery", "Quinn", edjeId: edjeId);

        await _service.LogAsync(new AuditEntry("Timesheet", "1", "edit", employeeId.ToString(), "system",
            "Logged under Employee.Id", []));
        await _service.LogAsync(new AuditEntry("Timesheet", "2", "edit", edjeId.ToString(), "system",
            "Logged under Person.EdjeId", []));

        // Act
        var result = await _service.BrowseAsync(null, null, edjeId.ToString(), null, null, page: 1, pageSize: 25);

        // Assert
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task BrowseAsync_FiltersByEmployeeId_ReturnsEmpty_WhenEmployeeNotFound()
    {
        // Arrange
        await _service.LogAsync(new AuditEntry("Timesheet", "1", "edit", "actor", "system",
            "Action", []));

        // Act — unknown employeeId → no results (graceful no-match, never silently unfiltered)
        var result = await _service.BrowseAsync(null, null, Guid.NewGuid().ToString(), null, null, page: 1, pageSize: 25);

        // Assert
        result.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task BrowseAsync_ResolvesEntityDescription_ForPersonRow()
    {
        // Arrange — legacy #138's complaint: "The Entity Column still shows just a '1' for my edits."
        // A Person row must describe the name, never the bare id.
        var personId = Guid.NewGuid();
        _context.People.Add(new Person
        {
            Id = personId,
            EdjeId = Guid.NewGuid(),
            FirstName = "Avery",
            LastName = "Quinn"
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _service.LogAsync(new AuditEntry("Person", personId.ToString(), "edit", "system", "system",
            "Edited profile", []));

        // Act
        var result = await _service.BrowseAsync(null, null, null, null, null, page: 1, pageSize: 25);

        // Assert
        result.Items.Count.ShouldBe(1);
        result.Items[0].EntityDescription.ShouldBe("Avery Quinn");
    }

    [Fact]
    public async Task BrowseAsync_EntityDescriptionIsNull_WhenTheEntityNoLongerExists()
    {
        // Arrange — a deleted or never-seeded target must degrade to null so the UI falls back to
        // the raw id rather than rendering an empty cell.
        await _service.LogAsync(new AuditEntry("Timesheet", "9999", "edit", "system", "system",
            "Edited hours", []));

        // Act
        var result = await _service.BrowseAsync(null, null, null, null, null, page: 1, pageSize: 25);

        // Assert
        result.Items[0].EntityDescription.ShouldBeNull();
    }

    [Fact]
    public async Task GetByEntityAsync_AlsoResolvesEntityDescription()
    {
        // Arrange — the inline audit panel goes through GetByEntityAsync, not BrowseAsync; it must
        // describe entities too or the same bare id resurfaces there.
        var personId = Guid.NewGuid();
        _context.People.Add(new Person { Id = personId, EdjeId = Guid.NewGuid(), FirstName = "Avery", LastName = "Quinn" });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _service.LogAsync(new AuditEntry("Person", personId.ToString(), "update", "system", "system",
            "Renamed", []));

        // Act
        var result = await _service.GetByEntityAsync("Person", personId.ToString());

        // Assert
        result.Count.ShouldBe(1);
        result[0].EntityDescription.ShouldBe("Avery Quinn");
    }
}
