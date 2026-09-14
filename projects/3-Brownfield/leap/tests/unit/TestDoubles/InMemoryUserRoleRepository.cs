#pragma warning disable CS1591 // Phase 39 D-02: intentionally undocumented — test double.

using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
namespace LeadingEDJE.Leap.Api.Tests.TestDoubles;

public class InMemoryUserRoleRepository : IUserRoleRepository
{
    private readonly List<UserRole> _roles = [];
    private int _nextId = 1;

    public Task<IReadOnlyList<UserRole>> GetByEdjeIdAsync(Guid edjeId)
    {
        var result = _roles.Where(r => r.EdjeId == edjeId).ToList().AsReadOnly();
        return Task.FromResult<IReadOnlyList<UserRole>>(result);
    }

    public Task<IReadOnlyList<UserRole>> GetByRoleAsync(string role)
    {
        var result = _roles.Where(r => r.Role == role).ToList().AsReadOnly();
        return Task.FromResult<IReadOnlyList<UserRole>>(result);
    }

    public Task<UserRole?> FindAsync(Guid edjeId, string role)
    {
        var result = _roles.FirstOrDefault(r => r.EdjeId == edjeId && r.Role == role);
        return Task.FromResult(result);
    }

    public Task<UserRole> AddAsync(UserRole userRole)
    {
        userRole.Id = _nextId++;
        _roles.Add(userRole);
        return Task.FromResult(userRole);
    }

    public Task RemoveAsync(UserRole userRole)
    {
        _roles.Remove(userRole);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserRole>> GetAllAsync()
    {
        var result = _roles.ToList().AsReadOnly();
        return Task.FromResult<IReadOnlyList<UserRole>>(result);
    }
}
