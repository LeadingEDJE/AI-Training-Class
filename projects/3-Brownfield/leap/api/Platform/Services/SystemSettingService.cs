
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// CRUD operations for runtime-configurable system settings with audit-logged changes.
/// </summary>
public class SystemSettingService(
    ISystemSettingRepository repository,
    IAuditService auditService,
    LeapDbContext context) : ISystemSettingService
{
    /// <summary>Returns every system setting row.</summary>
    public Task<IReadOnlyList<SystemSetting>> GetAllAsync()
        => repository.GetAllAsync();

    /// <summary>Returns the system setting by its key, or <c>null</c> if not present.</summary>
    public Task<SystemSetting?> GetByKeyAsync(string key)
        => repository.GetByKeyAsync(key);

    /// <summary>
    /// Creates a new system setting with audit logging. Returns an error if a setting with the same key already exists.
    /// </summary>
    public async Task<(SystemSetting? Setting, string? Error)> CreateAsync(string key, string value, string description)
    {
        var existing = await repository.GetByKeyAsync(key);
        if (existing != null)
        {
            return (null, $"Setting with key '{key}' already exists");
        }

        var setting = new SystemSetting
        {
            Key = key,
            Value = value,
            Description = description
        };

        await repository.CreateAsync(setting);

        await auditService.LogAsync(new AuditEntry(
            "SystemSetting",
            key,
            "create",
            "system",
            "Admin",
            $"Created system setting '{key}'",
            [
                new FieldChange("Value", null, value),
                new FieldChange("Description", null, description)
            ]));

        await context.SaveChangesAsync();

        return (setting, null);
    }

    /// <summary>Updates an existing system setting with audit logging. Returns an error if the key is not found.</summary>
    public async Task<(SystemSetting? Setting, string? Error)> UpdateAsync(string key, string value, string description)
    {
        var existing = await repository.GetByKeyAsync(key);
        if (existing == null)
        {
            return (null, "Setting not found");
        }

        var beforeValue = existing.Value;
        var beforeDescription = existing.Description;

        var updated = await repository.UpdateByKeyAsync(key, new SystemSetting
        {
            Key = key,
            Value = value,
            Description = description
        });

        var changes = new List<FieldChange>();
        if (beforeValue != value)
        {
            changes.Add(new FieldChange("Value", beforeValue, value));
        }
        if (beforeDescription != description)
        {
            changes.Add(new FieldChange("Description", beforeDescription, description));
        }

        await auditService.LogAsync(new AuditEntry(
            "SystemSetting",
            key,
            "update",
            "system",
            "Admin",
            $"Updated system setting '{key}'",
            changes));

        await context.SaveChangesAsync();

        return (updated, null);
    }

    /// <summary>Deletes a system setting with audit logging. Returns an error if the key is not found.</summary>
    public async Task<(bool Success, string? Error)> DeleteAsync(string key)
    {
        var existing = await repository.GetByKeyAsync(key);
        if (existing == null)
        {
            return (false, "Setting not found");
        }

        await repository.DeleteByKeyAsync(key);

        await auditService.LogAsync(new AuditEntry(
            "SystemSetting",
            key,
            "delete",
            "system",
            "Admin",
            $"Deleted system setting '{key}'",
            [
                new FieldChange("Value", existing.Value, null),
                new FieldChange("Description", existing.Description, null)
            ]));

        await context.SaveChangesAsync();

        return (true, null);
    }
}
