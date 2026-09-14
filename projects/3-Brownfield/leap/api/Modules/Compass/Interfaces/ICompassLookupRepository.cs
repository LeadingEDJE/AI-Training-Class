namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Data access for one kind of Compass lookup.
/// </summary>
/// <typeparam name="TLookup">The lookup entity, e.g. <c>EmployeeType</c>.</typeparam>
/// <remarks>
/// Deliberately not derived from <c>IRepository&lt;T&gt;</c>: that interface carries
/// <c>DeleteAsync</c>, and a lookup is retired by clearing its active flag, never removed — other
/// records reference it by id (Principle VIII, FR-007). Declaring only what the service needs keeps
/// the delete path absent from the contract rather than present and merely unused. No method persists;
/// <c>SaveChangesAsync</c> belongs to the service layer (Principle III).
/// </remarks>
public interface ICompassLookupRepository<TLookup>
    where TLookup : class, ICompassLookup
{
    /// <summary>Every lookup value, or only the active ones.</summary>
    /// <param name="activeOnly">
    /// When true, restricts to selectable values — what the EDJEr and client configuration forms
    /// request. The admin screen requests everything.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<TLookup>> GetAllAsync(bool activeOnly, CancellationToken cancellationToken);

    /// <summary>The tracked lookup with this id, or null.</summary>
    Task<TLookup?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Whether another lookup already carries this name, compared case-insensitively.
    /// </summary>
    /// <param name="typeName">The candidate name.</param>
    /// <param name="excludingId">
    /// The lookup being edited, excluded from the search so renaming a value to its own current name
    /// is not a collision with itself. Null when creating.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> NameExistsAsync(
        string typeName,
        int? excludingId,
        CancellationToken cancellationToken
    );

    /// <summary>Stages a new lookup for insertion. Does not persist.</summary>
    Task AddAsync(TLookup lookup, CancellationToken cancellationToken);
}
