using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

public class InMemoryAuditLogRepositoryGapTests
{
    [Fact]
    public async Task GetByIdAsync_ExistingLog_ReturnsLog()
    {
        // Arrange
        var repo = new InMemoryAuditLogRepository();
        var log = new AuditLog
        {
            EntityType = "Timesheet",
            EntityId = "123",
            Action = "Created",
            Actor = "test-user",
            Timestamp = DateTime.UtcNow
        };
        await repo.CreateAsync(log);

        // Act
        var result = await repo.GetByIdAsync((int)log.Id);

        // Assert
        result.ShouldNotBeNull();
        result.EntityType.ShouldBe("Timesheet");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var repo = new InMemoryAuditLogRepository();

        // Act
        var result = await repo.GetByIdAsync(999);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateAsync_ThrowsNotSupported()
    {
        // Arrange
        var repo = new InMemoryAuditLogRepository();

        // Act & Assert
        await Should.ThrowAsync<NotSupportedException>(
            () => repo.UpdateAsync(1, new AuditLog()));
    }

    [Fact]
    public async Task DeleteAsync_ThrowsNotSupported()
    {
        // Arrange
        var repo = new InMemoryAuditLogRepository();

        // Act & Assert
        await Should.ThrowAsync<NotSupportedException>(
            () => repo.DeleteAsync(1));
    }
}

public class InMemorySystemSettingRepositoryGapTests
{
    [Fact]
    public async Task UpdateByKeyAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var repo = new InMemorySystemSettingRepository();

        // Act
        var result = await repo.UpdateByKeyAsync("nonexistent", new SystemSetting { Key = "nonexistent", Value = "val" });

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateByKeyAsync_Existing_UpdatesFields()
    {
        // Arrange
        var repo = new InMemorySystemSettingRepository();
        await repo.CreateAsync(new SystemSetting { Key = "test-key", Value = "old-val", Description = "old-desc" });

        // Act
        var result = await repo.UpdateByKeyAsync("test-key",
            new SystemSetting { Key = "test-key", Value = "new-val", Description = "new-desc" });

        // Assert
        result.ShouldNotBeNull();
        result.Value.ShouldBe("new-val");
        result.Description.ShouldBe("new-desc");
    }

    [Fact]
    public async Task DeleteByKeyAsync_NonExistent_ReturnsFalse()
    {
        // Arrange
        var repo = new InMemorySystemSettingRepository();

        // Act
        var result = await repo.DeleteByKeyAsync("nonexistent");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteByKeyAsync_Existing_ReturnsTrueAndRemoves()
    {
        // Arrange
        var repo = new InMemorySystemSettingRepository();
        await repo.CreateAsync(new SystemSetting { Key = "to-delete", Value = "val" });

        // Act
        var result = await repo.DeleteByKeyAsync("to-delete");

        // Assert
        result.ShouldBeTrue();
        var check = await repo.GetByKeyAsync("to-delete");
        check.ShouldBeNull();
    }
}
