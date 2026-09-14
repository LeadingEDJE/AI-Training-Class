using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

public class NotificationTestDoubleCoverageTests
{
    [Fact]
    public async Task NullNotificationService_WithPeriodWeekStart_RecordsSentNotification()
    {
        // Arrange
        var service = new NullNotificationService();
        var edjeId = Guid.NewGuid();
        var payload = new NotificationPayload("Subject", "Body");
        var weekStart = new DateOnly(2026, 3, 16);

        // Act
        await service.NotifyAsync("submitted", edjeId, payload, weekStart);

        // Assert
        service.SentNotifications.Count.ShouldBe(1);
        service.SentNotifications[0].Type.ShouldBe("submitted");
        service.SentNotifications[0].RecipientEdjeId.ShouldBe(edjeId);
    }

    [Fact]
    public void NotificationLogDto_CanBeInstantiated()
    {
        // Arrange & Act
        var dto = new NotificationLogDto(
            1,
            "EMP-001",
            "submitted",
            new DateOnly(2026, 3, 16),
            "email",
            "Sent",
            "test@example.com",
            null,
            null,
            0,
            DateTime.UtcNow,
            DateTime.UtcNow);

        // Assert
        dto.Id.ShouldBe(1);
        dto.EmployeeId.ShouldBe("EMP-001");
        dto.NotificationType.ShouldBe("submitted");
        dto.Status.ShouldBe("Sent");
    }

    [Fact]
    public async Task InMemoryNotificationLogRepository_GetFailedForRetry_ReturnsOnlyFailed()
    {
        // Arrange
        var repo = new InMemoryNotificationLogRepository();
        await repo.AddAsync(new NotificationLog
        {
            EmployeeId = "EMP-001",
            NotificationType = "submitted",
            PeriodWeekStart = new DateOnly(2026, 3, 16),
            Status = "Sent"
        });
        await repo.AddAsync(new NotificationLog
        {
            EmployeeId = "EMP-002",
            NotificationType = "submitted",
            PeriodWeekStart = new DateOnly(2026, 3, 16),
            Status = "Failed",
            ErrorMessage = "connection refused"
        });

        // Act
        var failed = (await repo.GetFailedForRetryAsync()).ToList();

        // Assert
        failed.Count.ShouldBe(1);
        failed[0].EmployeeId.ShouldBe("EMP-002");
    }

    [Fact]
    public async Task InMemoryNotificationLogRepository_UpdateStatusAsync_NonExistentId_DoesNothing()
    {
        // Arrange
        var repo = new InMemoryNotificationLogRepository();

        // Act - should not throw
        await repo.UpdateStatusAsync(999, "Sent", null);

        // Assert - no entries
        var all = (await repo.GetRecentAsync(100, 0)).ToList();
        all.Count.ShouldBe(0);
    }
}
