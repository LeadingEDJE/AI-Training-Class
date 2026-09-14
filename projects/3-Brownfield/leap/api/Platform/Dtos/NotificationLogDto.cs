namespace LeadingEDJE.Leap.Api.Platform.Dtos;

/// <summary>
/// API response projection of a notification delivery log entry.
/// </summary>
public record NotificationLogDto(
    long Id,
    string EmployeeId,
    string NotificationType,
    DateOnly PeriodWeekStart,
    string Channel,
    string Status,
    string? RecipientEmail,
    string? SlackUserId,
    string? ErrorMessage,
    int RetryCount,
    DateTime? SentAt,
    DateTime CreatedAt);
