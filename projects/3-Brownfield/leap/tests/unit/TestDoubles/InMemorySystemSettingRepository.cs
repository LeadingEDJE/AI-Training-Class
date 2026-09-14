#pragma warning disable CS1591 // Phase 39 D-02: intentionally undocumented — test double.

using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
namespace LeadingEDJE.Leap.Api.Tests.TestDoubles;

public class InMemorySystemSettingRepository : ISystemSettingRepository
{
    private readonly List<SystemSetting> _settings = [];

    public Task<IReadOnlyList<SystemSetting>> GetAllAsync()
        => Task.FromResult<IReadOnlyList<SystemSetting>>(_settings.ToList());

    public Task<SystemSetting?> GetByKeyAsync(string key)
        => Task.FromResult(_settings.FirstOrDefault(s => s.Key == key));

    public async Task<string?> GetValueAsync(string key)
        => (await GetByKeyAsync(key))?.Value;

    public Task<SystemSetting> CreateAsync(SystemSetting entity)
    {
        _settings.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<SystemSetting?> UpdateByKeyAsync(string key, SystemSetting entity)
    {
        var existing = _settings.FirstOrDefault(s => s.Key == key);
        if (existing is null)
        {
            return Task.FromResult<SystemSetting?>(null);
        }

        existing.Value = entity.Value;
        existing.Description = entity.Description;
        return Task.FromResult<SystemSetting?>(existing);
    }

    public Task<bool> DeleteByKeyAsync(string key)
    {
        var existing = _settings.FirstOrDefault(s => s.Key == key);
        if (existing is null)
        {
            return Task.FromResult(false);
        }

        _settings.Remove(existing);
        return Task.FromResult(true);
    }
}
