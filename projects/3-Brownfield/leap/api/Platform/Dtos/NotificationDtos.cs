namespace LeadingEDJE.Leap.Api.Platform.Dtos;

/// <summary>
/// Aggregated hours for a single time category, used in notification payloads and dashboard displays.
/// </summary>
public record CategoryTotal(string CategoryName, decimal TotalHours);

/// <summary>
/// Payload carrying all data needed to render and send a notification via email or Slack.
/// </summary>
public record NotificationPayload(
    string Subject,
    string Body,
    string? Reason = null,
    long? TimesheetId = null,
    string? EmployeeName = null,
    DateOnly? WeekEndDate = null,
    IReadOnlyList<CategoryTotal>? CategoryBreakdown = null,
    IReadOnlyList<string>? OooEvents = null,
    decimal? Over40PayOut = null,
    decimal? Over40Banked = null);
