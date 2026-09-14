namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// Generic repository contract providing standard CRUD operations for any entity type. Implementations
/// do not call <c>SaveChangesAsync</c>; the service layer owns persistence boundaries.
/// </summary>
public interface IRepository<T> where T : class
{
    /// <summary>Returns every entity of type <typeparamref name="T"/>.</summary>
    Task<IReadOnlyList<T>> GetAllAsync();

    /// <summary>Returns the entity with the given id, or null if none exists.</summary>
    Task<T?> GetByIdAsync(int id);

    /// <summary>Adds <paramref name="entity"/> to the change tracker and returns the tracked instance.</summary>
    Task<T> CreateAsync(T entity);

    /// <summary>Updates the entity with the given id, or returns null if no such entity exists.</summary>
    Task<T?> UpdateAsync(int id, T entity);

    /// <summary>Removes the entity with the given id. Returns false if no such entity exists.</summary>
    Task<bool> DeleteAsync(int id);
}
