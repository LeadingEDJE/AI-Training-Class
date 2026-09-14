namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// ADR-007's retention asymmetry: purge audit entries past their window, never business records.
/// </summary>
/// <remarks>
/// One method, and deliberately no "purge business records" counterpart — the asymmetry is the whole
/// decision. Client assignment history and the assignment duration report both read the full history
/// and are specified as never truncated by retention, so business data ageing out would not fail
/// loudly; it would return quietly incomplete answers. Two tables are exempt for reasons their names
/// do not give away: <c>notification_log</c> is load-bearing for a business rule, because "already
/// notified" is derived from it and deleting a row makes a second coach email deliverable; and the
/// frozen legacy directory tables are business records regardless of age. Pruning either is a new
/// decision needing a new ADR, not a widening of this one.
/// </remarks>
public interface IAuditRetentionService
{
    /// <summary>
    /// Deletes audit entries older than the configured retention window and returns how many went.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The number of audit rows removed. Returned rather than logged internally so the caller — the
    /// scheduled job — can report it, and so a test can assert the pass did something rather than
    /// inferring it from the absence of rows.
    /// </returns>
    Task<int> PurgeExpiredAuditEntriesAsync(CancellationToken cancellationToken);
}
