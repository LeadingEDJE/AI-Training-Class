using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Platform.Data.Repositories;

/// <summary>Generic EF Core repository providing standard CRUD operations with audit-field protection on updates.</summary>
public class Repository<T>(LeapDbContext context) : IRepository<T> where T : class
{
    /// <summary>The EF Core context used by this repository for change tracking and SaveChangesAsync (owned by the service layer).</summary>
    protected readonly LeapDbContext Context = context;
    /// <summary>The entity set for type <typeparamref name="T"/>, used for queries, inserts, updates, and deletes.</summary>
    protected readonly DbSet<T> DbSet = context.Set<T>();

    private static readonly HashSet<string> ProtectedProperties =
        ["Id", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"];

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> GetAllAsync()
        => await DbSet.ToListAsync();

    /// <inheritdoc />
    public async Task<T?> GetByIdAsync(int id)
        => await DbSet.FindAsync(id);

    /// <inheritdoc />
    public async Task<T> CreateAsync(T entity)
    {
        await DbSet.AddAsync(entity);
        return entity;
    }

    /// <inheritdoc />
    public async Task<T?> UpdateAsync(int id, T entity)
    {
        var existing = await DbSet.FindAsync(id);
        if (existing is null)
        {
            return null;
        }

        var entry = Context.Entry(existing);

        // Copy only non-protected properties from incoming entity.
        // Skip key (Id) and audit fields (CreatedAt, UpdatedAt, etc.) which are
        // managed by the database or should not be overwritten by API consumers.
        foreach (var property in entry.Properties)
        {
            if (!ProtectedProperties.Contains(property.Metadata.Name))
            {
                var incomingValue = typeof(T).GetProperty(property.Metadata.Name)?.GetValue(entity);
                property.CurrentValue = incomingValue;
            }
        }

        return existing;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await DbSet.FindAsync(id);
        if (existing is null)
        {
            return false;
        }

        DbSet.Remove(existing);
        return true;
    }
}
