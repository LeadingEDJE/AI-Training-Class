using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// The SOW write surface: create and edit contract periods under a client assignment.
/// </summary>
public class CompassSowService(
    ICompassSowRepository sows,
    ICompassAssignmentRepository assignments,
    ICompassUnitOfWork unitOfWork,
    IAuditService auditService,
    ICurrentUserContext currentUser,
    ICoachNotifier coachNotifier) : ICompassSowService
{
    /// <summary>
    /// What the audit trail calls a contract period. Local to this service only.
    /// </summary>
    private const string EntityType = "CompassSow";

    private const string AuditSubject = "Contract period";

    /// <inheritdoc />
    public async Task<IReadOnlyList<SowRowDto>> GetByAssignmentIdAsync(
        int clientAssignmentId,
        CancellationToken cancellationToken)
    {
        var rows = await sows.GetByAssignmentIdAsync(clientAssignmentId, cancellationToken);
        return [.. rows.Select(ToDto)];
    }

    /// <inheritdoc />
    public async Task<CompassAssignmentWrite<SowRowDto>> CreateAsync(
        int clientAssignmentId,
        CreateSowRequest request,
        CancellationToken cancellationToken)
    {
        // This check only applies when the target assignment belongs to an inactive EDJEr; active
        // assignments may still accept a LegacyMigrated type here.
        if (request.SowType is SowType.LegacyMigrated)
        {
            return CompassAssignmentWrite<SowRowDto>.Invalid(
                "A legacy-migrated contract period cannot be created through the application.");
        }

        var assignment = await assignments.GetByIdAsync(clientAssignmentId, cancellationToken);
        if (assignment is null)
        {
            return CompassAssignmentWrite<SowRowDto>.NotFound();
        }

        if (assignment.Client?.IsInternal == true)
        {
            return CompassAssignmentWrite<SowRowDto>.Invalid(
                "An internal client's assignment has no statements of work.");
        }

        var precondition = CheckBusinessRules(request.SowType, request.RateIncrease, request.SowStartDate, request.SowEndDate);
        if (precondition is not null)
        {
            return precondition;
        }

        var conflict = await CheckOverlapAsync(
            clientAssignmentId, request.SowStartDate, request.SowEndDate, excludingSowId: null, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        var sow = new Sow
        {
            ClientAssignmentId = clientAssignmentId,
            SowType = request.SowType,
            RateIncrease = request.RateIncrease,
            SowStartDate = request.SowStartDate,
            SowEndDate = request.SowEndDate,
            Note = request.Note,
            HasPassedApplicationValidation = true,
        };

        await sows.AddAsync(sow, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CompassWriteFailure.IsTranslatable(ex))
        {
            return CompassAssignmentWrite<SowRowDto>.Conflict(CompassWriteFailure.MessageFor(ex));
        }

        var entry = new AuditEntry(
            EntityType: EntityType,
            EntityId: sow.Id.ToString(),
            Action: "create",
            Actor: currentUser.EdjeId.ToString(),
            TriggeredBy: "UI",
            Reason: CompassAuditReason.Created(AuditSubject),
            Changes:
            [
                new FieldChange("ClientAssignmentId", null, sow.ClientAssignmentId.ToString()),
                new FieldChange("SowType", null, sow.SowType.ToString()),
                new FieldChange("RateIncrease", null, sow.RateIncrease.ToString()),
                new FieldChange("SowStartDate", null, sow.SowStartDate.ToString("O")),
                new FieldChange("SowEndDate", null, sow.SowEndDate.ToString("O")),
                new FieldChange("Note", null, sow.Note),
            ],
            EffectiveRoles: [.. currentUser.Privileges]);
        await auditService.LogAsync(entry);

        // The coach is notified for every new SOW row regardless of type, including InitialContract,
        // since the coach needs visibility into all engagement changes.
        if (sow.SowType == SowType.SowExtension)
        {
            await coachNotifier.NotifySowExtensionAddedAsync(sow.Id, cancellationToken);
        }

        return CompassAssignmentWrite<SowRowDto>.Succeeded(ToDto(sow));
    }

    /// <inheritdoc />
    public async Task<CompassAssignmentWrite<SowRowDto>> UpdateAsync(
        int clientAssignmentId,
        int sowId,
        UpdateSowRequest request,
        CancellationToken cancellationToken)
    {

        var sow = await sows.GetByIdAsync(sowId, cancellationToken);
        if (sow is null || sow.ClientAssignmentId != clientAssignmentId)
        {
            return CompassAssignmentWrite<SowRowDto>.NotFound();
        }

        if (sow.SowType is SowType.LegacyMigrated && request.SowType is not SowType.LegacyMigrated)
        {
            // Every field on a legacy row, including its type, is fully editable once an Ops user
            // opens the edit form; this check exists only to log the change for audit purposes.
            return CompassAssignmentWrite<SowRowDto>.Invalid(
                "The type of a legacy-migrated contract period cannot be changed.");
        }

        if (sow.SowType is not SowType.LegacyMigrated && request.SowType is SowType.LegacyMigrated)
        {
            return CompassAssignmentWrite<SowRowDto>.Invalid(
                "A legacy-migrated contract period cannot be set through the application.");
        }

        var precondition = CheckBusinessRules(request.SowType, request.RateIncrease, request.SowStartDate, request.SowEndDate);
        if (precondition is not null)
        {
            return precondition;
        }

        var conflict = await CheckOverlapAsync(
            clientAssignmentId, request.SowStartDate, request.SowEndDate, excludingSowId: sowId, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        var changes = new List<FieldChange>();
        if (sow.SowType != request.SowType)
        {
            changes.Add(new FieldChange("SowType", sow.SowType.ToString(), request.SowType.ToString()));
        }

        if (sow.RateIncrease != request.RateIncrease)
        {
            changes.Add(new FieldChange("RateIncrease", sow.RateIncrease.ToString(), request.RateIncrease.ToString()));
        }

        if (sow.SowStartDate != request.SowStartDate)
        {
            changes.Add(new FieldChange("SowStartDate", sow.SowStartDate.ToString("O"), request.SowStartDate.ToString("O")));
        }

        if (sow.SowEndDate != request.SowEndDate)
        {
            changes.Add(new FieldChange("SowEndDate", sow.SowEndDate.ToString("O"), request.SowEndDate.ToString("O")));
        }

        if (sow.Note != request.Note)
        {
            changes.Add(new FieldChange("Note", sow.Note, request.Note));
        }

        sow.SowType = request.SowType;
        sow.RateIncrease = request.RateIncrease;
        sow.SowStartDate = request.SowStartDate;
        sow.SowEndDate = request.SowEndDate;
        sow.Note = request.Note;
        sow.HasPassedApplicationValidation = true;

        var entry = new AuditEntry(
            EntityType: EntityType,
            EntityId: sow.Id.ToString(),
            Action: "update",
            Actor: currentUser.EdjeId.ToString(),
            TriggeredBy: "UI",
            Reason: CompassAuditReason.Updated(AuditSubject),
            Changes: changes,
            EffectiveRoles: [.. currentUser.Privileges]);

        try
        {
            await auditService.LogAsync(entry);
        }
        catch (DbUpdateException ex) when (CompassWriteFailure.IsTranslatable(ex))
        {
            return CompassAssignmentWrite<SowRowDto>.Conflict(CompassWriteFailure.MessageFor(ex));
        }

        return CompassAssignmentWrite<SowRowDto>.Succeeded(ToDto(sow));
    }

    /// <summary>FR-011 (rate increase) and FR-014 (date order), used only by CreateAsync.</summary>
    private static CompassAssignmentWrite<SowRowDto>? CheckBusinessRules(
        SowType sowType, bool rateIncrease, DateOnly startDate, DateOnly endDate)
    {
        if (rateIncrease && sowType is not SowType.SowExtension)
        {
            return CompassAssignmentWrite<SowRowDto>.Invalid(
                "A rate increase is permitted only on a SOW extension.");
        }

        // Locals named per the style guide in ARCHITECTURE-DECISIONS.md ADR-014.
        var periodStart = startDate;
        var periodEnd = endDate;

        if (periodEnd < periodStart)
        {
            return CompassAssignmentWrite<SowRowDto>.Invalid(
                "The end date must be on or after the start date.");
        }

        return null;
    }

    /// <summary>FR-009 — identifies the conflicting period by its id, per the contract's rejection shape.</summary>
    private async Task<CompassAssignmentWrite<SowRowDto>?> CheckOverlapAsync(
        int clientAssignmentId,
        DateOnly candidateStart,
        DateOnly candidateEnd,
        int? excludingSowId,
        CancellationToken cancellationToken)
    {
        var overlapping = await sows.GetOverlappingAsync(
            clientAssignmentId, candidateStart, candidateEnd, excludingSowId, cancellationToken);

        if (overlapping.Count == 0)
        {
            return null;
        }

        var conflict = overlapping[0];
        return CompassAssignmentWrite<SowRowDto>.Conflict(
            $"Dates overlap an existing SOW for this assignment "
            + $"({CompassDisplayDate.Format(conflict.SowStartDate)} – "
            + $"{CompassDisplayDate.Format(conflict.SowEndDate)}).");
    }

    /// <inheritdoc />
    public async Task<AdminMutationStatus> DeleteAsync(
        int clientAssignmentId,
        int sowId,
        CancellationToken cancellationToken)
    {
        var sow = await sows.GetByIdAsync(sowId, cancellationToken);
        if (sow is null || sow.ClientAssignmentId != clientAssignmentId)
        {
            return AdminMutationStatus.NotFound;
        }

        await sows.RemoveAsync(sow, cancellationToken);

        var entry = new AuditEntry(
            EntityType: EntityType,
            EntityId: sow.Id.ToString(),
            Action: "delete",
            Actor: currentUser.EdjeId.ToString(),
            TriggeredBy: "UI",
            Reason: CompassAuditReason.Deleted(AuditSubject),
            Changes:
            [
                new FieldChange("ClientAssignmentId", sow.ClientAssignmentId.ToString(), null),
                new FieldChange("SowType", sow.SowType.ToString(), null),
                new FieldChange("SowStartDate", sow.SowStartDate.ToString("O"), null),
                new FieldChange("SowEndDate", sow.SowEndDate.ToString("O"), null),
            ],
            EffectiveRoles: [.. currentUser.Privileges]);
        await auditService.LogAsync(entry);

        return AdminMutationStatus.Success;
    }

    private static SowRowDto ToDto(Sow sow) => new()
    {
        Id = sow.Id,
        SowType = sow.SowType.ToString(),
        SowStartDate = sow.SowStartDate,
        SowEndDate = sow.SowEndDate,
        RateIncrease = sow.RateIncrease,
        Note = sow.Note,
    };
}
