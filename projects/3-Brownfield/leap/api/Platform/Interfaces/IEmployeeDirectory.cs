namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// Read access to the employee directory for Platform's cross-cutting services: audit attribution and
/// user-role administration. Platform-owned, so Platform depends on no module.
/// </summary>
/// <remarks>
/// This is not the published Compass directory contract (<c>IDirectory</c>), which is a wider read
/// surface for out-of-module consumers; this is a narrow Platform-owned seam over data Platform already
/// reads (<c>people</c>). No caching, no memoisation, no ordering guarantee: a caller needing the whole
/// directory loads it once at the call site.
/// </remarks>
public interface IEmployeeDirectory
{
    /// <summary>
    /// Returns every employee in the directory. Never null; empty when the directory is empty. Order
    /// is unspecified — every caller builds a lookup or filters, so none may depend on it.
    /// </summary>
    Task<IReadOnlyList<EmployeeDirectoryEntry>> GetAllEmployeesAsync();
}

/// <summary>
/// What Platform knows about a person: an identifier, a display name, an optional EDJE identity, an
/// optional email address, and the name parts.
/// </summary>
/// <param name="Id">The directory's own employee identifier. Audit rows may be stored under this.</param>
/// <param name="Name">Display name, used for audit actor names and the role-administration listing.</param>
/// <param name="EdjeId">The EDJE identity, when the person has one. Audit rows may be stored under this instead of <paramref name="Id"/>.</param>
/// <param name="Email">Stored email address, when there is one.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
public sealed record EmployeeDirectoryEntry(
    string Id,
    string Name,
    Guid? EdjeId,
    string? Email,
    string FirstName,
    string LastName);
