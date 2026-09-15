using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>Empties the six Compass operational tables and records who did it.</summary>
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
    private const string AuditEntityType = "CompassData";

    private const string AuditEntityId = "compass";

    /// <inheritdoc />
    public async Task<CompassDataClearedResponse> ClearAllAsync(CancellationToken cancellationToken)
    {
        // Counts are taken first, then the clear runs as a separate call against the same repository.
        var counts = await repository.ClearAsync(cancellationToken);

        var clearedAt = timeProvider.GetUtcNow().UtcDateTime;

        logger.LogWarning(
            "Compass data cleared by {Actor}: {Total} rows removed across six tables "
                + "({BillableTimeCategories} billable time categories, {Sows} SOWs, "
                + "{ClientAssignments} assignments, {Employees} EDJErs, {EmployeeSkills} tagged skills, "
                + "{Clients} clients).",
            currentUser.EdjeId,
            counts.Total,
            counts.BillableTimeCategories,
            counts.Sows,
            counts.ClientAssignments,
            counts.Employees,
            counts.EmployeeSkills,
            counts.Clients);

        await RecordAuditAsync(counts, clearedAt);

        return new CompassDataClearedResponse(
            counts.BillableTimeCategories,
            counts.Sows,
            counts.ClientAssignments,
            counts.Employees,
            counts.EmployeeSkills,
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
                new FieldChange("billable_time_category", counts.BillableTimeCategories.ToString(), "0"),
                new FieldChange("sow", counts.Sows.ToString(), "0"),
                new FieldChange("client_assignment", counts.ClientAssignments.ToString(), "0"),
                new FieldChange("employee", counts.Employees.ToString(), "0"),
                new FieldChange("employee_skill", counts.EmployeeSkills.ToString(), "0"),
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
            // Retried once automatically by the caller per the resiliency policy in
            // docs/compass-audit-retry.md; this catch only logs the final failure.
            logger.LogError(
                ex,
                "Compass data was cleared by {Actor} but the audit entry could not be written.",
                currentUser.EdjeId);
        }
    }
}
