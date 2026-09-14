using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Platform.Data.Repositories;

/// <summary>EF Core repository for audit log entries with entity-scoped and date-range queries.</summary>
public class AuditLogRepository(LeapDbContext context) : Repository<AuditLog>(context), IAuditLogRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> GetByEntityAsync(string entityType, string entityId)
    {
        // AsNoTracking, matching BrowseAsync and GetDistinctEntityTypesAsync below. Audit rows are
        // append-only at the database level, so a tracked instance can only cause harm: EffectiveRoles
        // is a mutable collection, and a caller that sorts or edits it in place would make EF emit an
        // UPDATE that the audit_logs triggers reject, failing the request far from its cause.
        return await DbSet
            .AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> BrowseAsync(
        string? entityType, IReadOnlyList<string>? actorIds, DateTime? fromDate, DateTime? toDate, int page, int pageSize)
    {
        var query = DbSet.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        if (actorIds is not null)
        {
            // An empty list means the requested employee resolved to nothing. Falling through would
            // return every row, which presents as "the audit log filter is not working".
            if (actorIds.Count == 0)
            {
                return ([], 0);
            }

            // Materialize before Contains: an array is the form EF Core reliably translates to a
            // parameterized IN (...) / = ANY(...). Deliberately not element-capped — a cap silently
            // drops matches if actor resolution ever widens.
            var actorIdArray = actorIds.ToArray();
            query = query.Where(a => actorIdArray.Contains(a.Actor));
        }

        if (fromDate.HasValue)
        {
            query = query.Where(a => a.Timestamp >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(a => a.Timestamp <= toDate.Value);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync()
    {
        return await DbSet.AsNoTracking()
            .Select(a => a.EntityType)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();
    }

    /// <summary>Not supported — audit logs are append-only. Always throws <see cref="NotSupportedException"/>.</summary>
    public new Task<AuditLog?> UpdateAsync(int id, AuditLog entity)
    {
        throw new NotSupportedException("Audit logs are append-only and cannot be updated.");
    }

    /// <summary>Not supported — audit logs are append-only. Always throws <see cref="NotSupportedException"/>.</summary>
    public new Task<bool> DeleteAsync(int id)
    {
        throw new NotSupportedException("Audit logs are append-only and cannot be deleted.");
    }
}
