
using LeadingEDJE.Leap.Api.Platform.Domain;
namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>Data access for application-wide key-value configuration settings.</summary>
public interface ISystemSettingRepository
{
    /// <summary>Returns every configured setting.</summary>
    Task<IReadOnlyList<SystemSetting>> GetAllAsync();

    /// <summary>Returns the setting row with the given key, or null if none exists.</summary>
    Task<SystemSetting?> GetByKeyAsync(string key);

    /// <summary>Returns the raw string value for the given key, or null if the setting does not exist.</summary>
    Task<string?> GetValueAsync(string key);

    /// <summary>Adds a new setting row.</summary>
    Task<SystemSetting> CreateAsync(SystemSetting entity);

    /// <summary>Updates the setting identified by <paramref name="key"/>, or returns null if no such setting exists.</summary>
    Task<SystemSetting?> UpdateByKeyAsync(string key, SystemSetting entity);

    /// <summary>Deletes the setting with the given key. Returns false if no such setting exists.</summary>
    Task<bool> DeleteByKeyAsync(string key);
}
