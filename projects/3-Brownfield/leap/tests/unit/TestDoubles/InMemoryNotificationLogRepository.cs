#pragma warning disable CS1591 // Phase 39 D-02: intentionally undocumented — test double.

using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
namespace LeadingEDJE.Leap.Api.Tests.TestDoubles;

public class InMemoryNotificationLogRepository : INotificationLogRepository
{
    private readonly List<NotificationLog> _entities = [];
    private long _nextId = 1;

    // CreatedAt is stamped only when the caller LEFT IT UNSET. Overwriting it unconditionally, as this
    // did, made NotificationRetryJob's `CreatedAt > UtcNow.AddMinutes(-30)` window impossible to
    // exercise through this double -- every stored row was "just now" -- and silently discarded the
    // value two tests in NotificationRetryJobTests already set, so those passed for a reason other
    // than the one they read as. `AuditableEntity.CreatedAt` has no initialiser, so `default` is an
    // unambiguous "not supplied".
    public Task<NotificationLog> AddAsync(NotificationLog log)
    {
        log.Id = _nextId++;
        if (log.CreatedAt == default)
        {
            log.CreatedAt = DateTime.UtcNow;
        }

        log.UpdatedAt = DateTime.UtcNow;
        _entities.Add(log);
        return Task.FromResult(log);
    }

    public Task<NotificationLog?> GetByIdAsync(long id)
    {
        var entity = _entities.FirstOrDefault(e => e.Id == id);
        return Task.FromResult(entity);
    }

    public Task<NotificationLog?> GetByIdempotencyKeyAsync(string employeeId, string notificationType, DateOnly periodWeekStart)
    {
        var entity = _entities.FirstOrDefault(e =>
            string.Equals(e.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.NotificationType, notificationType, StringComparison.OrdinalIgnoreCase) &&
            e.PeriodWeekStart == periodWeekStart);
        return Task.FromResult(entity);
    }

    public Task<IReadOnlyList<NotificationLog>> GetRecentAsync(int count, int offset)
    {
        var results = _entities
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(count);
        return Task.FromResult<IReadOnlyList<NotificationLog>>(results.ToList());
    }

    public Task UpdateStatusAsync(long id, string status, string? errorMessage)
    {
        var entity = _entities.FirstOrDefault(e => e.Id == id);
        if (entity != null)
        {
            entity.Status = status;
            entity.ErrorMessage = errorMessage;
            entity.UpdatedAt = DateTime.UtcNow;
            if (string.Equals(status, "Sent", StringComparison.OrdinalIgnoreCase))
            {
                entity.SentAt = DateTime.UtcNow;
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NotificationLog>> GetFailedForRetryAsync()
    {
        var results = _entities.Where(e =>
            string.Equals(e.Status, "Failed", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<IReadOnlyList<NotificationLog>>(results.ToList());
    }

    // Single-threaded stand-in for the real repo's atomic conditional UPDATE. Atomicity across
    // replicas is a database property, proven by NotificationRetryJobPersistenceTests against real
    // PostgreSQL; here it only has to claim once.
    public Task<bool> TryClaimForRetryAsync(long id)
    {
        var entity = _entities.FirstOrDefault(e => e.Id == id);
        if (entity is null || entity.RetryCount != 0)
        {
            return Task.FromResult(false);
        }

        entity.RetryCount = 1;
        return Task.FromResult(true);
    }

    public Task ReleaseRetryClaimAsync(long id)
    {
        var entity = _entities.FirstOrDefault(e => e.Id == id);
        if (entity is not null)
        {
            entity.RetryCount = 0;
        }

        return Task.CompletedTask;
    }
}
