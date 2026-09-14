using LeadingEDJE.Leap.Api.Platform.Domain;

namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// Data access for absorbed <see cref="Person"/> records. Standalone rather than <c>IRepository&lt;T&gt;</c>
/// because that is int-<c>Id</c>-shaped while absorbed entities use a Guid PK. No method persists.
/// </summary>
public interface IPersonRepository
{
    /// <summary>Returns every person.</summary>
    Task<IReadOnlyList<Person>> GetAllAsync();

    /// <summary>Returns the tracked person with the given id, or null. Tracked so the service can mutate and save.</summary>
    Task<Person?> GetByIdAsync(Guid id);

    /// <summary>Returns the person with the given EdjeId, or null. Used to resolve an impersonation target.</summary>
    Task<Person?> GetByEdjeIdAsync(Guid edjeId);

    /// <summary>
    /// Returns true if any OTHER person already owns <paramref name="email"/> case-insensitively.
    /// Mirrors the <c>ix_people_email_lower</c> functional unique index (the DB backstop).
    /// </summary>
    Task<bool> EmailExistsAsync(string email, Guid? excludeId);

    /// <summary>Stages a new person for insertion (no save).</summary>
    Task<Person> AddAsync(Person person);
}
