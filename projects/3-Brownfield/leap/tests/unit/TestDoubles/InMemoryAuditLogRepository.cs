#pragma warning disable CS1591 // Phase 39 D-02: intentionally undocumented — test double.
using System.Reflection;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Tests.TestDoubles;

public class InMemoryAuditLogRepository : IAuditLogRepository
{
    private readonly List<AuditLog> _entities = [];
    private long _nextId = 1;

    public Task<IReadOnlyList<AuditLog>> GetAllAsync()
    {
        return Task.FromResult<IReadOnlyList<AuditLog>>(_entities.ToList());
    }

    public Task<AuditLog?> GetByIdAsync(int id)
    {
        AuditLog? entity = _entities.FirstOrDefault(e => e.Id == id);
        return Task.FromResult(entity);
    }

    public Task<AuditLog> CreateAsync(AuditLog entity)
    {
        entity.Id = _nextId++;
        _entities.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<AuditLog?> UpdateAsync(int id, AuditLog entity)
    {
        throw new NotSupportedException("Audit logs are append-only and cannot be updated.");
    }

    public Task<bool> DeleteAsync(int id)
    {
        throw new NotSupportedException("Audit logs are append-only and cannot be deleted.");
    }

    public Task<IReadOnlyList<AuditLog>> GetByEntityAsync(string entityType, string entityId)
    {
        var results = _entities
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
        return Task.FromResult<IReadOnlyList<AuditLog>>(results);
    }

    public Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> BrowseAsync(
        string? entityType, IReadOnlyList<string>? actorIds, DateTime? fromDate, DateTime? toDate, int page, int pageSize)
    {
        var query = _entities.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        if (actorIds is not null)
        {
            query = actorIds.Count > 0
                ? query.Where(a => actorIds.Contains(a.Actor))
                : Enumerable.Empty<AuditLog>();
        }

        if (fromDate.HasValue)
        {
            query = query.Where(a => a.Timestamp >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(a => a.Timestamp <= toDate.Value);
        }

        var all = query.OrderByDescending(a => a.Timestamp).ToList();
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult<(IReadOnlyList<AuditLog>, int)>((items, all.Count));
    }

    public Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync()
    {
        var types = _entities.Select(a => a.EntityType).Distinct().OrderBy(t => t).ToList();
        return Task.FromResult<IReadOnlyList<string>>(types);
    }
}
