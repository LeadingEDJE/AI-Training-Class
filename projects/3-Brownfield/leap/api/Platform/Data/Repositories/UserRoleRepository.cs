using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Platform.Data.Repositories;

/// <summary>EF Core repository for additive role assignments; multi-row reads use AsNoTracking as a read-only query optimization.</summary>
public class UserRoleRepository(LeapDbContext context) : IUserRoleRepository
{
    private readonly DbSet<UserRole> _dbSet = context.UserRoles;

    // AsNoTracking() on multi-row reads is a deliberate read-only query optimization (no change tracking overhead).
    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRole>> GetByEdjeIdAsync(Guid edjeId) =>
        await _dbSet.AsNoTracking().Where(ur => ur.EdjeId == edjeId).ToListAsync();

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRole>> GetByRoleAsync(string role) =>
        await _dbSet.AsNoTracking().Where(ur => ur.Role == role).ToListAsync();

    /// <inheritdoc />
    public async Task<UserRole?> FindAsync(Guid edjeId, string role) =>
        await _dbSet.FirstOrDefaultAsync(ur => ur.EdjeId == edjeId && ur.Role == role);

    /// <inheritdoc />
    public async Task<UserRole> AddAsync(UserRole userRole)
    {
        await _dbSet.AddAsync(userRole);
        return userRole;
    }

    /// <inheritdoc />
    public Task RemoveAsync(UserRole userRole)
    {
        _dbSet.Remove(userRole);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRole>> GetAllAsync() =>
        await _dbSet.AsNoTracking().OrderBy(ur => ur.EdjeId).ThenBy(ur => ur.Role).ToListAsync();
}
