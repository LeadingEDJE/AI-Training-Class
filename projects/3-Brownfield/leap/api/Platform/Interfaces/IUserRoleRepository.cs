
using LeadingEDJE.Leap.Api.Platform.Domain;
namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>Data access for additive role assignments (EDJEr, Manager, TimesheetProcessor, etc.) per employee.</summary>
public interface IUserRoleRepository
{
    /// <summary>Returns every role assignment for the given EDJE identity (one row per role).</summary>
    Task<IReadOnlyList<UserRole>> GetByEdjeIdAsync(Guid edjeId);

    /// <summary>Returns every employee currently holding the given role.</summary>
    Task<IReadOnlyList<UserRole>> GetByRoleAsync(string role);

    /// <summary>Returns the single assignment row for (<paramref name="edjeId"/>, <paramref name="role"/>), or null.</summary>
    Task<UserRole?> FindAsync(Guid edjeId, string role);

    /// <summary>Inserts a new role assignment row.</summary>
    Task<UserRole> AddAsync(UserRole userRole);

    /// <summary>Removes an existing role assignment row.</summary>
    Task RemoveAsync(UserRole userRole);

    /// <summary>Returns every role-assignment row across every user (used by admin role browser).</summary>
    Task<IReadOnlyList<UserRole>> GetAllAsync();
}
