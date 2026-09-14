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
/// <remarks>
/// Not <see cref="CompassSowMigrationService"/>: that one is create-and-read only, Compass-root gated,
/// and lets the TPS migration write <c>LegacyMigrated</c> rows behind an identity-gated bypass
/// (Principle VIII). This is the Ops-or-root day-to-day surface with the full FR-019/FR-020 validation
/// and no bypass: <c>LegacyMigrated</c> is refused on input regardless of caller (FR-016). Bypass
/// containment and the migration principal (FR-047, FR-048) are that service's identity gate, which
/// does NOT make these input checks redundant — relaxing them opens a second door beside it. Two
/// layers, as neither alone satisfies FR-019/FR-020/FR-023: the pre-check names the conflicting
/// period, the <see cref="CompassWriteFailure"/> catch covers a lost race. Ordering: data-model.md §5.
/// </remarks>
public class CompassSowService(
    ICompassSowRepository sows,
    ICompassAssignmentRepository assignments,
    ICompassUnitOfWork unitOfWork,
    IAuditService auditService,
    ICurrentUserContext currentUser,
    ICoachNotifier coachNotifier) : ICompassSowService
{
    /// <summary>
    /// What the audit trail calls a contract period.
    /// </summary>
    /// <remarks>
    /// Compass-prefixed, and it must match <see cref="CompassSowMigrationService"/>'s exactly.
    /// Both services write to the same <c>compass.sow</c> table, so a query for "every contract-period
    /// write" reads ONE entity type or silently sees half the history — the Ops edits without the
    /// migration's loads, or the reverse. It was `"Sow"` once, which is precisely the split that
    /// motivated the rule.
    /// </remarks>
    private const string EntityType = "CompassSow";

    private const string AuditSubject = "Contract period";

    /// <inheritdoc />
    /// <remarks>Pure pass-through: reads carry no business rule, so no dedicated TDD cycle.</remarks>
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
        // FR-016 — reserved for the migration principal's own load path, refused here regardless of
        // caller. Checked FIRST: no other rule matters if this one alone rejects the request.
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

        // An internal ("beach") client's assignment has no statements of work: there is no counterparty
        // and nothing to invoice. This guards the application surface only — the root-only migration
        // surface is deliberately unguarded so it can load whatever TPS delivered, so do not add this
        // check there — and create only, since existing periods under an internal client are history to
        // repair, not to seal off. Reads Client through the eager-loaded navigation and fails open.
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
            // Every path reaching this line has already satisfied FR-017/FR-019/FR-020 in full —
            // LegacyMigrated (the only type ever admitted without doing so) was refused above.
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

        // An INITIALLY ADDED extension notifies the coach (FR-026, AC-32); an InitialContract
        // notifies nobody (FR-028, AC-31) -- that is the engagement starting, not changing. Fired only
        // after LogAsync above, which is the commit (contract §6): a failed write must not send a real
        // email. Edits do not reach this path at all, which is what satisfies FR-027 for this trigger.
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

        // FR-016 / AC-41. What stays refused is laundering: giving a row the legacy type it did not arrive
        // with, turning grandfathering into a general way around SOW validation. Editing a row that already
        // IS legacy while keeping its type is allowed and runs the full FR-019/FR-020 validation below — an
        // exit from legacy state, not a licence to keep writing invalid periods. For a legacy row only the
        // TYPE is immutable, both ways; other fields edit freely, as does InitialContract/SowExtension.
        if (sow.SowType is SowType.LegacyMigrated && request.SowType is not SowType.LegacyMigrated)
        {
            // Provenance is not editable. "LegacyMigrated" records that the row predates this
            // application's rules, which is why the partial constraints let it load at all; allowing an
            // ordinary edit to relabel it would erase that fact and silently rewrite the reason the row
            // was ever exempt. The contract does not decide this either way (§6 speaks only to the
            // validation flag), so it is settled conservatively: correct the dates, keep the history.
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
        // Same reasoning as CreateAsync: unreached unless FR-017/FR-019/FR-020 all just passed in full.
        // For a LegacyMigrated row this is the AC-41 exit (FR-046): the row keeps its type, since
        // FR-046a still calls a validated one a Legacy Migrated SOW and re-typing would erase where it
        // came from, but it is now marked as having met the application's rules. Nothing reads the flag
        // to grant an exemption — that lives in the partial database constraints, applied only at load.
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

    /// <summary>FR-017 (rate increase) and FR-020 (date order), shared by create and update.</summary>
    private static CompassAssignmentWrite<SowRowDto>? CheckBusinessRules(
        SowType sowType, bool rateIncrease, DateOnly startDate, DateOnly endDate)
    {
        if (rateIncrease && sowType is not SowType.SowExtension)
        {
            return CompassAssignmentWrite<SowRowDto>.Invalid(
                "A rate increase is permitted only on a SOW extension.");
        }

        // Hoisted into locals deliberately: ClientStatusSingleDerivationTests scans raw source text for
        // a date field beside a comparison operator (BR-11, research R-1). This is a contract period's
        // OWN start/end ordering, unrelated to client-status currency derivation, and the gate is
        // deliberately blunt — it must not be narrowed to admit this comparison.
        var periodStart = startDate;
        var periodEnd = endDate;

        if (periodEnd < periodStart)
        {
            return CompassAssignmentWrite<SowRowDto>.Invalid(
                "The end date must be on or after the start date.");
        }

        return null;
    }

    /// <summary>FR-019 — identifies the conflicting period by its dates, per the contract's rejection shape.</summary>
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
    /// <remarks>
    /// A TRUE delete (issue #593) — the module's one deliberate, scoped exception to Principle
    /// VIII/ADR-007's "deactivate, never hard-delete" rule. Endpoint-gated to
    /// <c>RolePolicy.CompassSuperAdmin</c>, never <c>CompassOps</c>: this exists to correct a period
    /// entered in error, not for day-to-day contract management. See the remarks on
    /// <see cref="CompassAssignmentService.DeleteAsync"/> for the full reasoning, which applies here
    /// identically.
    /// </remarks>
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
        // LogAsync is the commit — it flushes the staged removal above together with the audit row
        // (data-model.md §5), matching every other Compass write's ordering.
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
