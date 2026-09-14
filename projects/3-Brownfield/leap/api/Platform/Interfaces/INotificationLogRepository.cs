
using LeadingEDJE.Leap.Api.Platform.Domain;
namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>Data access for notification delivery records with idempotency checks and retry queries.</summary>
public interface INotificationLogRepository
{
    /// <summary>Inserts a new notification log row (attempt record).</summary>
    Task<NotificationLog> AddAsync(NotificationLog log);

    /// <summary>Returns the log row with the given id, or null.</summary>
    Task<NotificationLog?> GetByIdAsync(long id);

    /// <summary>
    /// Returns an existing log for the (<paramref name="employeeId"/>, <paramref name="notificationType"/>, <paramref name="periodWeekStart"/>) idempotency key, or null.
    /// Used to suppress duplicate Slack/email dispatches within a single payroll week.
    /// </summary>
    Task<NotificationLog?> GetByIdempotencyKeyAsync(string employeeId, string notificationType, DateOnly periodWeekStart);

    /// <summary>Returns the most-recent <paramref name="count"/> log rows, skipping <paramref name="offset"/>, for the admin audit UI.</summary>
    Task<IReadOnlyList<NotificationLog>> GetRecentAsync(int count, int offset);

    /// <summary>Updates a log row's delivery status (e.g. <c>sent</c>, <c>failed</c>) and optional error message.</summary>
    Task UpdateStatusAsync(long id, string status, string? errorMessage);

    /// <summary>Returns every failed log row eligible for automatic retry by the notification retry worker.</summary>
    Task<IReadOnlyList<NotificationLog>> GetFailedForRetryAsync();

    /// <summary>
    /// Atomically claims a row's single retry: sets <c>retry_count</c> to 1 only if it is still 0, and
    /// returns whether this caller won.
    /// </summary>
    /// <remarks>
    /// The deduplication seam for the multi-replica retry job (#599). Every replica reads the same
    /// <c>retry_count = 0</c>, so the claim must be one conditional statement the database serialises —
    /// only the winner sends.
    /// </remarks>
    Task<bool> TryClaimForRetryAsync(long id);

    /// <summary>
    /// Releases a retry claim by setting <c>retry_count</c> back to 0, so a row whose outcome could not
    /// be recorded is picked up again rather than stranded.
    /// </summary>
    /// <remarks>
    /// The claim commits before the send (#599), so if the outcome save then fails the row is left
    /// claimed with a status that never caught up. Releasing it restores the pre-claim self-heal: the
    /// next pass reclaims and retries, which can re-send -- an acceptable duplicate against a permanent
    /// ledger lie.
    /// </remarks>
    Task ReleaseRetryClaimAsync(long id);
}
