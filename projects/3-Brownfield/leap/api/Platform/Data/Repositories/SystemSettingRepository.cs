using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Platform.Data.Repositories;

/// <summary>EF Core repository for application-wide key-value configuration settings.</summary>
public class SystemSettingRepository(LeapDbContext context) : ISystemSettingRepository
{
    private readonly DbSet<SystemSetting> _dbSet = context.SystemSettings;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SystemSetting>> GetAllAsync()
        => await _dbSet.ToListAsync();

    /// <inheritdoc />
    public async Task<SystemSetting?> GetByKeyAsync(string key)
        => await _dbSet.FirstOrDefaultAsync(s => s.Key == key);

    /// <inheritdoc />
    public async Task<string?> GetValueAsync(string key)
        => (await GetByKeyAsync(key))?.Value;

    /// <inheritdoc />
    public async Task<SystemSetting> CreateAsync(SystemSetting entity)
    {
        await _dbSet.AddAsync(entity);
        return entity;
    }

    /// <inheritdoc />
    public async Task<SystemSetting?> UpdateByKeyAsync(string key, SystemSetting entity)
    {
        var existing = await _dbSet.FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
        {
            return null;
        }

        existing.Value = entity.Value;
        existing.Description = entity.Description;
        return existing;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteByKeyAsync(string key)
    {
        var existing = await _dbSet.FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
        {
            return false;
        }

        _dbSet.Remove(existing);
        return true;
    }
}
