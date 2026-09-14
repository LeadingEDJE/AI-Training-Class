
using LeadingEDJE.Leap.Api.Platform.Domain;
namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>Manages system settings with validation, defaulting, and audit-logged updates.</summary>
public interface ISystemSettingService
{
    /// <summary>Returns every configured setting.</summary>
    Task<IReadOnlyList<SystemSetting>> GetAllAsync();

    /// <summary>Returns the setting with the given key, or null.</summary>
    Task<SystemSetting?> GetByKeyAsync(string key);

    /// <summary>Creates a new setting. Returns a user-friendly error on validation failure (e.g. duplicate key).</summary>
    Task<(SystemSetting? Setting, string? Error)> CreateAsync(string key, string value, string description);

    /// <summary>Updates an existing setting. Returns a user-friendly error on validation failure or not-found.</summary>
    Task<(SystemSetting? Setting, string? Error)> UpdateAsync(string key, string value, string description);

    /// <summary>Deletes the setting with the given key. Returns a user-friendly error if the setting is required or not found.</summary>
    Task<(bool Success, string? Error)> DeleteAsync(string key);
}
