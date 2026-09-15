using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>Data access for a Compass lookup, extending the shared repository base.</summary>
/// <typeparam name="TLookup">The lookup entity.</typeparam>
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
    /// <remarks>Uses <c>AsNoTracking</c>; rename and retire operate on a freshly loaded copy.</remarks>
    public async Task<TLookup?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await context.Set<TLookup>().FirstOrDefaultAsync(lookup => lookup.Id == id, cancellationToken);

    /// <inheritdoc />
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
