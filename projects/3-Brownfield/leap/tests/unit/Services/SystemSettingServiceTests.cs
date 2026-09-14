using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

public class SystemSettingServiceTests : IDisposable
{
    private readonly InMemorySystemSettingRepository _repository;
    private readonly RecordingAuditService _auditService;
    private readonly LeapDbContext _context;
    private readonly SystemSettingService _service;

    public SystemSettingServiceTests()
    {
        _repository = new InMemorySystemSettingRepository();
        _auditService = new RecordingAuditService();
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new LeapDbContext(options);
        _service = new SystemSettingService(_repository, _auditService, _context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllSettings()
    {
        // Arrange
        await _repository.CreateAsync(new SystemSetting { Key = "setting1", Value = "val1", Description = "desc1" });
        await _repository.CreateAsync(new SystemSetting { Key = "setting2", Value = "val2", Description = "desc2" });

        // Act
        var result = (await _service.GetAllAsync()).ToList();

        // Assert
        result.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetByKeyAsync_ReturnsSettingOrNull()
    {
        // Arrange
        await _repository.CreateAsync(new SystemSetting { Key = "my_key", Value = "my_val", Description = "desc" });

        // Act
        var found = await _service.GetByKeyAsync("my_key");
        var notFound = await _service.GetByKeyAsync("nonexistent");

        // Assert
        found.ShouldNotBeNull();
        found.Key.ShouldBe("my_key");
        notFound.ShouldBeNull();
    }

    [Fact]
    public async Task CreateAsync_CreatesSettingAndLogsAudit()
    {
        // Act
        var (setting, error) = await _service.CreateAsync("new_key", "new_val", "new desc");

        // Assert
        setting.ShouldNotBeNull();
        error.ShouldBeNull();
        setting.Key.ShouldBe("new_key");
        setting.Value.ShouldBe("new_val");

        var persisted = await _repository.GetByKeyAsync("new_key");
        persisted.ShouldNotBeNull();

        _auditService.Calls.ShouldBe(1);
        _auditService.LastEntry.ShouldNotBeNull();
        _auditService.LastEntry.EntityType.ShouldBe("SystemSetting");
        _auditService.LastEntry.Action.ShouldBe("create");
    }

    [Fact]
    public async Task UpdateAsync_UpdatesValueAndLogsAuditWithDiff()
    {
        // Arrange
        await _repository.CreateAsync(new SystemSetting { Key = "upd_key", Value = "old_val", Description = "old desc" });

        // Act
        var (setting, error) = await _service.UpdateAsync("upd_key", "new_val", "new desc");

        // Assert
        setting.ShouldNotBeNull();
        error.ShouldBeNull();
        setting.Value.ShouldBe("new_val");
        setting.Description.ShouldBe("new desc");

        _auditService.Calls.ShouldBe(1);
        _auditService.LastEntry.ShouldNotBeNull();
        _auditService.LastEntry.Action.ShouldBe("update");
        _auditService.LastEntry.Changes.ShouldContain(c => c.Field == "Value" && c.Before == "old_val" && c.After == "new_val");
    }

    [Fact]
    public async Task DeleteAsync_DeletesAndLogsAudit()
    {
        // Arrange
        await _repository.CreateAsync(new SystemSetting { Key = "del_key", Value = "val", Description = "desc" });

        // Act
        var (success, error) = await _service.DeleteAsync("del_key");

        // Assert
        success.ShouldBeTrue();
        error.ShouldBeNull();

        var deleted = await _repository.GetByKeyAsync("del_key");
        deleted.ShouldBeNull();

        _auditService.Calls.ShouldBe(1);
        _auditService.LastEntry.ShouldNotBeNull();
        _auditService.LastEntry.Action.ShouldBe("delete");
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateKey()
    {
        // Arrange
        await _repository.CreateAsync(new SystemSetting { Key = "dup_key", Value = "val", Description = "desc" });

        // Act
        var (setting, error) = await _service.CreateAsync("dup_key", "other_val", "other desc");

        // Assert
        setting.ShouldBeNull();
        error.ShouldBe("Setting with key 'dup_key' already exists");
    }
}

internal class RecordingAuditService : IAuditService
{
    public int Calls { get; private set; }
    public AuditEntry? LastEntry { get; private set; }

    public Task LogAsync(AuditEntry entry)
    {
        Calls++;
        LastEntry = entry;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditLogResponse>> GetByEntityAsync(string entityType, string entityId)
        => Task.FromResult<IReadOnlyList<AuditLogResponse>>([]);

    public Task<PaginatedAuditLogResponse> BrowseAsync(
        string? entityType, string? actor, string? employeeId, DateTime? fromDate, DateTime? toDate, int page, int pageSize)
        => Task.FromResult(new PaginatedAuditLogResponse([], 0, page, pageSize));

    public Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync()
        => Task.FromResult<IReadOnlyList<string>>([]);
}
