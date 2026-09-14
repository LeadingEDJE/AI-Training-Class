using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Platform.Data.Repositories;

/// <summary>EF Core repository for Slack and email notification delivery records.</summary>
public class NotificationLogRepository(LeapDbContext context) : INotificationLogRepository
{
    private readonly DbSet<NotificationLog> _dbSet = context.NotificationLogs;

    /// <inheritdoc />
    public async Task<NotificationLog> AddAsync(NotificationLog log)
    {
        await _dbSet.AddAsync(log);
        return log;
    }

    /// <inheritdoc />
    public async Task<NotificationLog?> GetByIdAsync(long id) =>
        await _dbSet.FirstOrDefaultAsync(n => n.Id == id);

    /// <inheritdoc />
    public async Task<NotificationLog?> GetByIdempotencyKeyAsync(
        string employeeId, string notificationType, DateOnly periodWeekStart) =>
        await _dbSet.FirstOrDefaultAsync(n =>
            n.EmployeeId == employeeId &&
            n.NotificationType == notificationType &&
            n.PeriodWeekStart == periodWeekStart);

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationLog>> GetRecentAsync(int count, int offset) =>
        await _dbSet
            .OrderByDescending(n => n.CreatedAt)
            .Skip(offset)
            .Take(count)
            .ToListAsync();

    /// <inheritdoc />
    public async Task UpdateStatusAsync(long id, string status, string? errorMessage)
    {
        var entity = await _dbSet.FirstOrDefaultAsync(n => n.Id == id);
        if (entity != null)
        {
            entity.Status = status;
            entity.ErrorMessage = errorMessage;
            if (string.Equals(status, "Sent", StringComparison.OrdinalIgnoreCase))
            {
                entity.SentAt = DateTime.UtcNow;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationLog>> GetFailedForRetryAsync() =>
        await _dbSet
            .Where(n => n.Status == "Failed")
            .OrderBy(n => n.CreatedAt)
            .ToListAsync();

    /// <inheritdoc />
    /// <remarks>
    /// One conditional statement, not a tracked read-modify-save: Postgres row-locks two concurrent
    /// UPDATEs on the same row, so the second re-evaluates <c>retry_count = 0</c> against the winner's
    /// committed 1 and matches nothing. ExecuteUpdateAsync bypasses the change tracker, so this owns no
    /// SaveChangesAsync boundary — the same shape AuditRetentionService uses for its bulk delete.
    /// </remarks>
    public async Task<bool> TryClaimForRetryAsync(long id) =>
        await _dbSet
            .Where(n => n.Id == id && n.RetryCount == 0)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.RetryCount, 1)) > 0;

    /// <inheritdoc />
    public async Task ReleaseRetryClaimAsync(long id) =>
        await _dbSet
            .Where(n => n.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.RetryCount, 0));
}
