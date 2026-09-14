using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>Empties the five Compass operational tables and records who did it.</summary>
/// <remarks>
/// See <see cref="ICompassDataResetService"/> for the contract and for why the two Compass lookup
/// tables are deliberately excluded. The counts come back from the clear itself: <c>TRUNCATE</c>
/// reports nothing and afterwards there is nothing left to count, so the numbers have to be taken
/// before the rows go and under the same lock, or a concurrent insert is destroyed without being
/// counted. Both are the repository's job; this service must not be able to get one without the other.
/// </remarks>
/// <param name="repository">Owns the SQL; a Compass service may not name a data context.</param>
/// <param name="auditService">Records who cleared what.</param>
/// <param name="currentUser">The caller, for the audit record.</param>
/// <param name="timeProvider">Clock, injected so the recorded instant is testable.</param>
/// <param name="logger">Diagnostics — a destructive action should be visible in the pod log too.</param>
public sealed class CompassDataResetService(
    ICompassDataResetRepository repository,
    IAuditService auditService,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider,
    ILogger<CompassDataResetService> logger) : ICompassDataResetService
{
    /// <summary>The audit entity type this service writes under.</summary>
    private const string AuditEntityType = "CompassData";

    /// <summary>The audit entity id — the schema, since no row survives to point at.</summary>
    private const string AuditEntityId = "compass";

    /// <inheritdoc />
    public async Task<CompassDataClearedResponse> ClearAllAsync(CancellationToken cancellationToken)
    {
        // One call, not a count followed by a clear: the repository takes the truncate's lock before
        // it counts, so the numbers below describe exactly the rows that were destroyed.
        var counts = await repository.ClearAsync(cancellationToken);

        var clearedAt = timeProvider.GetUtcNow().UtcDateTime;

        // Deliberately LogWarning, not LogInformation: a destructive administrative action should stand
        // out in a log that is mostly request noise, and this is the only durable trace if the audit
        // write below fails.
        logger.LogWarning(
            "Compass data cleared by {Actor}: {Total} rows removed across five tables "
                + "({BillableTimeCategories} billable time categories, {Sows} SOWs, "
                + "{ClientAssignments} assignments, {Employees} EDJErs, {Clients} clients).",
            currentUser.EdjeId,
            counts.Total,
            counts.BillableTimeCategories,
            counts.Sows,
            counts.ClientAssignments,
            counts.Employees,
            counts.Clients);

        await RecordAuditAsync(counts, clearedAt);

        return new CompassDataClearedResponse(
            counts.BillableTimeCategories,
            counts.Sows,
            counts.ClientAssignments,
            counts.Employees,
            counts.Clients,
            counts.Total,
            clearedAt);
    }

    private async Task RecordAuditAsync(CompassTableRowCounts counts, DateTime clearedAt)
    {
        var entry = new AuditEntry(
            EntityType: AuditEntityType,
            EntityId: AuditEntityId,
            Action: "clear",
            Actor: currentUser.EdjeId.ToString(),
            TriggeredBy: "DeveloperTools",
            Reason: "Compass data cleared via Developer Tools",
            Changes:
            [
                // Before/after per table, so the record answers "how much was destroyed" and not merely
                // "someone pressed the button".
                new FieldChange("billable_time_category", counts.BillableTimeCategories.ToString(), "0"),
                new FieldChange("sow", counts.Sows.ToString(), "0"),
                new FieldChange("client_assignment", counts.ClientAssignments.ToString(), "0"),
                new FieldChange("employee", counts.Employees.ToString(), "0"),
                new FieldChange("client", counts.Clients.ToString(), "0"),
                new FieldChange("total", counts.Total.ToString(), "0"),
                new FieldChange("clearedAtUtc", null, clearedAt.ToString("O")),
            ],
            EffectiveRoles: [.. currentUser.Privileges]);

        try
        {
            await auditService.LogAsync(entry);
        }
        catch (DbUpdateException ex)
        {
            // The data is already gone and committed. Failing the request now would tell the caller the
            // clear did not happen, which is the one thing that is definitely untrue — so the audit
            // failure is logged at Error and the response still reports what was removed.
            logger.LogError(
                ex,
                "Compass data was cleared by {Actor} but the audit entry could not be written.",
                currentUser.EdjeId);
        }
    }
}
