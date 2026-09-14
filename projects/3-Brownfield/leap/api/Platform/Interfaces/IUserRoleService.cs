
using LeadingEDJE.Leap.Api.Platform.Domain;
namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>Manages the additive role model: assign, remove, sync from IdP profile, and ensure base roles for all employees.</summary>
public interface IUserRoleService
{
    /// <summary>Returns the role names (strings) held by <paramref name="edjeId"/>.</summary>
    Task<IReadOnlyList<string>> GetRolesAsync(Guid edjeId);

    /// <summary>Returns true if <paramref name="edjeId"/> holds <paramref name="role"/>.</summary>
    Task<bool> HasRoleAsync(Guid edjeId, string role);

    /// <summary>Returns true if <paramref name="edjeId"/> holds any of the supplied <paramref name="roles"/>.</summary>
    Task<bool> HasAnyRoleAsync(Guid edjeId, params string[] roles);

    /// <summary>
    /// Assigns <paramref name="role"/> to <paramref name="edjeId"/>. Idempotent — no-op if already assigned.
    /// <paramref name="actor"/> and <paramref name="reason"/> are written to the audit log.
    /// </summary>
    Task AssignRoleAsync(Guid edjeId, string role, string actor, string reason);

    /// <summary>
    /// Removes <paramref name="role"/> from <paramref name="edjeId"/>. Idempotent — no-op if not currently assigned.
    /// <paramref name="actor"/> and <paramref name="reason"/> are written to the audit log.
    /// </summary>
    Task RemoveRoleAsync(Guid edjeId, string role, string actor, string reason);

    /// <summary>Returns every user currently holding <paramref name="role"/> (one row per assignment).</summary>
    Task<IReadOnlyList<UserRole>> GetUsersByRoleAsync(string role);

    /// <summary>
    /// Reconciles the employee's local roles against the IdP privilege list: adds roles granted by privileges
    /// and removes roles that no longer correspond to a held privilege. Called at login and during bulk sync.
    /// </summary>
    Task SyncFromProfileAsync(Guid edjeId, IEnumerable<string> privileges, string actor = "dev-sync");

    /// <summary>
    /// Ensures every supplied employee has the base EDJEr role. Returns the count of assignments newly created.
    /// Used by the full-directory sync worker.
    /// </summary>
    Task<int> EnsureBaseRoleForAllEmployeesAsync(IEnumerable<(Guid EdjeId, string Name)> employees);
}
