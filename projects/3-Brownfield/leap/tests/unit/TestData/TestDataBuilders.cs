
using LeadingEDJE.Leap.Api.Platform.Domain;
namespace LeadingEDJE.Leap.Api.Tests.TestData;

public static class TestDataBuilders
{
    public static AuditLog BuildAuditLog(
        string entityType = "TimeCategory",
        string entityId = "1",
        string action = "Create",
        string actor = "system:test",
        string triggeredBy = "test@example.com",
        string reason = "Test reason",
        string changes = "[]",
        DateTime? timestamp = null) =>
        new()
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Actor = actor,
            TriggeredBy = triggeredBy,
            Reason = reason,
            Changes = changes,
            Timestamp = timestamp ?? DateTime.UtcNow
        };

    public static SystemSetting BuildSystemSetting(
        string key = "test.setting",
        string value = "test-value",
        string description = "A test setting") =>
        new()
        {
            Key = key,
            Value = value,
            Description = description
        };
}
