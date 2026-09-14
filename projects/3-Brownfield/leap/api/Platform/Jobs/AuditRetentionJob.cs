using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Quartz;

namespace LeadingEDJE.Leap.Api.Platform.Jobs;

/// <summary>
/// Runs ADR-007's audit purge on a schedule.
/// </summary>
/// <remarks>
/// Deliberately thin: it resolves a service and calls one method. The behaviour and every exemption
/// live in <see cref="IAuditRetentionService"/>, where an integration test can reach them; a Quartz
/// <c>IJob</c> is awkward to test directly because it wants an <c>IJobExecutionContext</c>.
/// A thrown exception is swallowed after logging, on purpose, so the failure lands in the application
/// log rather than only the scheduler's own output. Retention is housekeeping: a failed pass costs one
/// night of unpurged rows and the next pass fixes it, so it must never take the host down.
/// </remarks>
public class AuditRetentionJob(IServiceScopeFactory scopeFactory) : IJob
{
    /// <summary>Runs a single retention pass.</summary>
    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = scopeFactory.CreateScope();
        var retention = scope.ServiceProvider.GetRequiredService<IAuditRetentionService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AuditRetentionJob>>();

        try
        {
            var purged = await retention.PurgeExpiredAuditEntriesAsync(context.CancellationToken);
            logger.LogInformation("Audit retention pass complete; {Purged} entries purged.", purged);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit retention pass failed. The next scheduled pass will retry.");
        }
    }
}
