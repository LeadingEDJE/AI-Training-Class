using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>
/// Data access for a Compass lookup, written once and closed over each lookup entity.
/// </summary>
/// <typeparam name="TLookup">The lookup entity.</typeparam>
/// <remarks>
/// It does not extend <c>Repository&lt;T&gt;</c>: none of the base class's members match the
/// signatures this lookup surface needs — every read takes a cancellation token and the collection
/// read takes an active-only filter — so inheritance buys no reuse, while bringing a public
/// <c>DeleteAsync</c> that a lookup must never have, since a lookup is retired by clearing its
/// active flag rather than removed (Principle VIII, FR-007).
///
/// Reaches data through <c>context.Set&lt;T&gt;()</c> rather than a <c>DbSet</c> property (Option 2).
/// No method persists; that boundary is the service's
/// (Principle III), reached through <see cref="ICompassUnitOfWork"/>.
/// </remarks>
public class CompassLookupRepository<TLookup>(LeapDbContext context)
    : ICompassLookupRepository<TLookup>
    where TLookup : class, ICompassLookup
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TLookup>> GetAllAsync(
        bool activeOnly,
        CancellationToken cancellationToken
    ) =>
        await context
            .Set<TLookup>()
            .AsNoTracking()
            .Where(lookup => !activeOnly || lookup.IsActive)
            .OrderBy(lookup => lookup.TypeName)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Tracked deliberately — the service mutates what this returns and then saves. An
    /// <c>AsNoTracking</c> read here would make every rename and retire silently do nothing.
    /// </remarks>
    public async Task<TLookup?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await context.Set<TLookup>().FirstOrDefaultAsync(lookup => lookup.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Compared case-insensitively via <c>ToLower()</c>, which Npgsql translates to SQL <c>lower()</c>
    /// and the in-memory provider evaluates directly — so the same comparison holds under the
    /// integration suite and the endpoint tests. <c>ILike</c> would be the more natural Postgres
    /// spelling but is provider-specific and throws under the in-memory provider.
    /// </remarks>
    public async Task<bool> NameExistsAsync(
        string typeName,
        int? excludingId,
        CancellationToken cancellationToken
    )
    {
        var candidate = typeName.Trim().ToLowerInvariant();
        return await context
            .Set<TLookup>()
            .AsNoTracking()
            .AnyAsync(
                lookup => lookup.Id != excludingId && lookup.TypeName.ToLower() == candidate,
                cancellationToken
            );
    }

    /// <inheritdoc />
    public async Task AddAsync(TLookup lookup, CancellationToken cancellationToken) =>
        await context.Set<TLookup>().AddAsync(lookup, cancellationToken);
}
