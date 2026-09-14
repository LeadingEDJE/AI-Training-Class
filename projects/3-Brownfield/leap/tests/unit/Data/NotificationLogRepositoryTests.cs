using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

public class NotificationLogRepositoryTests
{
    private readonly InMemoryNotificationLogRepository _repo = new();

    [Fact]
    public async Task AddAsync_StoresLogEntry_AndRetrievesIt()
    {
        // Arrange
        var log = new NotificationLog
        {
            EmployeeId = "EMP-001",
            NotificationType = "submitted",
            PeriodWeekStart = new DateOnly(2026, 3, 16),
            Channel = "email",
            Status = "Pending"
        };

        // Act
        var created = await _repo.AddAsync(log);
        var retrieved = await _repo.GetByIdAsync(created.Id);

        // Assert
        retrieved.ShouldNotBeNull();
        retrieved.EmployeeId.ShouldBe("EMP-001");
        retrieved.NotificationType.ShouldBe("submitted");
        retrieved.Status.ShouldBe("Pending");
    }

    [Fact]
    public async Task GetByIdempotencyKeyAsync_ReturnsExisting_WhenMatch()
    {
        // Arrange
        await _repo.AddAsync(new NotificationLog
        {
            EmployeeId = "EMP-001",
            NotificationType = "submitted",
            PeriodWeekStart = new DateOnly(2026, 3, 16),
            Channel = "email",
            Status = "Sent"
        });

        // Act
        var result = await _repo.GetByIdempotencyKeyAsync("EMP-001", "submitted", new DateOnly(2026, 3, 16));

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe("Sent");
    }

    [Fact]
    public async Task GetByIdempotencyKeyAsync_ReturnsNull_WhenNoMatch()
    {
        // Arrange
        await _repo.AddAsync(new NotificationLog
        {
            EmployeeId = "EMP-001",
            NotificationType = "submitted",
            PeriodWeekStart = new DateOnly(2026, 3, 16),
            Channel = "email",
            Status = "Sent"
        });

        // Act
        var result = await _repo.GetByIdempotencyKeyAsync("EMP-002", "submitted", new DateOnly(2026, 3, 16));

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsOrderedByCreatedAtDescending_WithPagination()
    {
        // Arrange
        var log1 = await _repo.AddAsync(new NotificationLog
        {
            EmployeeId = "EMP-001",
            NotificationType = "submitted",
            PeriodWeekStart = new DateOnly(2026, 3, 9)
        });
        log1.CreatedAt = new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc);

        var log2 = await _repo.AddAsync(new NotificationLog
        {
            EmployeeId = "EMP-002",
            NotificationType = "reopened",
            PeriodWeekStart = new DateOnly(2026, 3, 16)
        });
        log2.CreatedAt = new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc);

        var log3 = await _repo.AddAsync(new NotificationLog
        {
            EmployeeId = "EMP-003",
            NotificationType = "nudge",
            PeriodWeekStart = new DateOnly(2026, 3, 23)
        });
        log3.CreatedAt = new DateTime(2026, 3, 23, 0, 0, 0, DateTimeKind.Utc);

        // Act - get first page of 2
        var page1 = (await _repo.GetRecentAsync(2, 0)).ToList();

        // Assert - most recent first
        page1.Count.ShouldBe(2);
        page1[0].EmployeeId.ShouldBe("EMP-003");
        page1[1].EmployeeId.ShouldBe("EMP-002");

        // Act - get second page
        var page2 = (await _repo.GetRecentAsync(2, 2)).ToList();

        // Assert
        page2.Count.ShouldBe(1);
        page2[0].EmployeeId.ShouldBe("EMP-001");
    }

    [Fact]
    public async Task UpdateStatusAsync_ChangesStatus_AndSetsSentAt()
    {
        // Arrange
        var log = await _repo.AddAsync(new NotificationLog
        {
            EmployeeId = "EMP-001",
            NotificationType = "submitted",
            PeriodWeekStart = new DateOnly(2026, 3, 16),
            Status = "Pending"
        });

        // Act
        await _repo.UpdateStatusAsync(log.Id, "Sent", null);

        // Assert
        var updated = await _repo.GetByIdAsync(log.Id);
        updated.ShouldNotBeNull();
        updated.Status.ShouldBe("Sent");
        updated.SentAt.ShouldNotBeNull();
        updated.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_Failed_SetsErrorMessage_NoSentAt()
    {
        // Arrange
        var log = await _repo.AddAsync(new NotificationLog
        {
            EmployeeId = "EMP-001",
            NotificationType = "submitted",
            PeriodWeekStart = new DateOnly(2026, 3, 16),
            Status = "Pending"
        });

        // Act
        await _repo.UpdateStatusAsync(log.Id, "Failed", "SMTP connection refused");

        // Assert
        var updated = await _repo.GetByIdAsync(log.Id);
        updated.ShouldNotBeNull();
        updated.Status.ShouldBe("Failed");
        updated.ErrorMessage.ShouldBe("SMTP connection refused");
        updated.SentAt.ShouldBeNull();
    }
}
