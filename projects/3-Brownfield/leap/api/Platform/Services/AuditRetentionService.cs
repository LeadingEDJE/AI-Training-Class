using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// ADR-007's retention asymmetry: purges audit entries past the configured window and touches nothing
/// else.
/// </summary>
/// <remarks>
/// One table by design: audit entries are the only rows with a retention policy; everything else is a
/// business record, the frozen legacy directory tables included. <c>notification_log</c> is exempt
/// because "already notified" is derived from it — deleting a row resurrects a notification and the
/// next edit sends a second coach email; <c>AuditRetentionServiceTests</c> asserts it survives.
/// <c>audit_logs</c> is append-only behind the <c>20260821133200_AllowAuditRetentionPurge</c> trigger,
/// permitting DELETE only while <c>leap.audit_retention_purge</c> is <c>on</c>; this is its only
/// setter, in an explicit transaction because <c>SET LOCAL</c> has no effect outside one and must not
/// outlive it. <c>ExecuteDeleteAsync</c> bypasses the change tracker, so no <c>SaveChangesAsync</c>.
/// </remarks>
public class AuditRetentionService(
    LeapDbContext context,
    IOptions<AuditRetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<AuditRetentionService> logger) : IAuditRetentionService
{
    /// <summary>
    /// The transaction-local flag <c>audit_logs_prevent_change()</c> checks before allowing a DELETE.
    /// </summary>
    /// <remarks>
    /// Must match the migration exactly. A typo here does not fail the build or the tests-that-do-not-
    /// delete; it makes the purge refuse silently, which is why
    /// <c>AuditRetentionServiceTests</c> asserts rows actually disappear rather than only that the call
    /// returned.
    /// </remarks>
    private const string PurgeFlag = "leap.audit_retention_purge";

    /// <inheritdoc />
    public async Task<int> PurgeExpiredAuditEntriesAsync(CancellationToken cancellationToken)
    {
        var years = options.Value.AuditEntryYears;
        if (years <= 0)
        {
            // Belt and braces, NOT the primary gate: AuditRetentionOptionsValidator rejects this at
            // startup, so a configured value can never reach here. What can is a service constructed
            // outside configuration binding -- a test, or a caller passing Options.Create(...). Refusing
            // is the only response that cannot destroy the audit trail, and it is logged loudly because
            // a silent no-op would look like a working job.
            logger.LogError(
                "Audit retention is configured as {Years} years, which is not a usable window. "
                    + "No audit entries were purged.",
                years);
            return 0;
        }

        // UtcNow, matching what AuditService stamps on Timestamp. The business date's America/New_York
        // calendar is the right anchor for "is this assignment current today" and the wrong one here:
        // a retention window is a duration, not a calendar day, so a timezone would only add a few
        // hours of ambiguity at the boundary for nothing.
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddYears(-years);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // A constant, not an interpolation: this is DDL-adjacent SQL and the flag name must be a
        // literal rather than anything a caller could influence.
        await context.Database.ExecuteSqlRawAsync(
            $"SET LOCAL {PurgeFlag} = 'on'",
            cancellationToken);

        // Strictly older than the cutoff, so a row exactly AT it is retained — "kept for two years,
        // then purged" gives the row its full second year.
        var purged = await context.AuditLogs
            .Where(entry => entry.Timestamp < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        if (purged > 0)
        {
            logger.LogInformation(
                "Purged {Purged} audit entries older than {Cutoff:o} ({Years}-year retention window).",
                purged,
                cutoff,
                years);
        }

        return purged;
    }
}
