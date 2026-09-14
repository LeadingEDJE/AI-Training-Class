using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// The assignment write surface. Compass's first audited write path.
/// </summary>
/// <remarks>
/// Update and end follow the load-bearing ordering exactly (contracts/assignment-write-surface.md §5):
/// stage the mutation on the already-tracked entity, then call <see cref="IAuditService.LogAsync"/>,
/// whose single call flushes the modified <see cref="ClientAssignment"/> row and the new audit row
/// together. Create cannot follow it literally, a deliberate documented deviation:
/// <c>ClientAssignment.Id</c> is a database-generated identity column, and an <c>AuditEntry</c> is an
/// immutable record whose <c>EntityId</c> must already be the real id, so Create stages the row, calls
/// <see cref="ICompassUnitOfWork.SaveChangesAsync"/> once to obtain that id, and only then logs.
/// That two-step shape is rejected for an existing change; a generated key has no earlier id.
/// </remarks>
public class CompassAssignmentService(
    ICompassAssignmentRepository assignments,
    ICompassSowRepository sows,
    ICompassUnitOfWork unitOfWork,
    IAuditService auditService,
    ICurrentUserContext currentUser,
    IClientStatusDerivation clientStatusDerivation,
    ICompassBusinessDate businessDate,
    ICoachNotifier coachNotifier) : ICompassAssignmentService
{
    /// <summary>
    /// What the audit trail calls a client assignment.
    /// </summary>
    /// <remarks>
    /// Compass-prefixed, matching every other audit-writing service in this module
    /// (<c>CompassEmployee</c>, <c>CompassClient</c>, <c>CompassSow</c>). The prefix is what keeps a
    /// Compass entity type from colliding with a same-named one in another module — the audit table
    /// is Platform-owned and shared, so <c>"ClientAssignment"</c> alone was only unambiguous by luck.
    /// It was <c>"ClientAssignment"</c> once, as a literal repeated at both call sites.
    /// </remarks>
    private const string AuditEntityType = "CompassClientAssignment";

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssignmentRowDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var isCurrent = clientStatusDerivation.IsCurrent(businessDate.Today()).Compile();
        var rows = await assignments.GetAllAsync(cancellationToken);
        return [.. rows.Select(a => ToDto(a, isCurrent(a)))];
    }

    /// <inheritdoc />
    public async Task<AssignmentRowDto?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var assignment = await assignments.GetByIdAsync(id, cancellationToken);
        if (assignment is null)
        {
            return null;
        }

        var isCurrent = clientStatusDerivation.IsCurrent(businessDate.Today()).Compile();
        return ToDto(assignment, isCurrent(assignment));
    }

    /// <inheritdoc />
    public Task<CompassAssignmentWrite<AssignmentRowDto>> CreateAsync(
        CreateAssignmentRequest request,
        CancellationToken cancellationToken
    ) =>
        unitOfWork.ExecuteAtomicallyAsync(
            ct => CreateAsyncCoreAsync(request, ct),
            result => result.Status == AdminMutationStatus.Success,
            cancellationToken);

    private async Task<CompassAssignmentWrite<AssignmentRowDto>> CreateAsyncCoreAsync(
        CreateAssignmentRequest request,
        CancellationToken cancellationToken
    )
    {
        // A migration write may name an inactive EDJEr: it records an engagement that already happened
        // rather than starting a new one, and refusing those would discard most of the assignment
        // history. The rule is right for a person — FR-019's deactivation guard is its other half —
        // but it is not a rule about history. Keying on `legacyTpsId` is sound because the endpoint's
        // CompassLegacyProvenance.MaySet already refused a non-migration caller who supplied one.
        var isMigrationWrite = CompassLegacyProvenance.Normalise(request.LegacyTpsId) is not null;

        var employee = await assignments.GetEmployeeAsync(request.EmployeeId, cancellationToken);

        // EXISTENCE is required either way. Only the ACTIVE half is waived -- an assignment naming
        // no EDJEr at all is a dangling foreign key, not a historical fact.
        if (employee is null)
        {
            return CompassAssignmentWrite<AssignmentRowDto>.Invalid("The EDJEr must be active.");
        }

        if (!employee.IsActive && !isMigrationWrite)
        {
            return CompassAssignmentWrite<AssignmentRowDto>.Invalid("The EDJEr must be active.");
        }

        var client = await assignments.GetClientAsync(request.ClientId, cancellationToken);
        if (client is null)
        {
            return CompassAssignmentWrite<AssignmentRowDto>.Invalid("The client must exist.");
        }

        if (request.EndDate is { } endDate && endDate < request.StartDate)
        {
            return CompassAssignmentWrite<AssignmentRowDto>.Invalid(
                "The end date must be on or after the start date.");
        }

        // FR-038/AC-26: an override may only name an ACTIVE cadence. The form offers active types
        // only, and FR-041's rule that a hidden option is never the control is why this is checked
        // here as well. Refused before the write, so a retired id becomes a message rather than a
        // foreign-key violation surfacing as a 500.
        if (request.InvoiceFrequencyTypeId is { } overrideId
            && !await assignments.ActiveInvoiceFrequencyTypeExistsAsync(overrideId, cancellationToken))
        {
            return CompassAssignmentWrite<AssignmentRowDto>.Invalid(
                "That invoice frequency does not exist or is no longer selectable.");
        }

        var assignment = new ClientAssignment
        {
            EmployeeId = request.EmployeeId,
            ClientId = request.ClientId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Note = request.Note,
            InvoiceFrequencyTypeId = request.InvoiceFrequencyTypeId,
            LegacyTpsId = CompassLegacyProvenance.Normalise(request.LegacyTpsId),
        };

        await assignments.AddAsync(assignment, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CompassWriteFailure.IsTranslatable(ex))
        {
            return CompassAssignmentWrite<AssignmentRowDto>.Conflict(CompassWriteFailure.MessageFor(ex));
        }

        var entry = new AuditEntry(
            EntityType: AuditEntityType,
            EntityId: assignment.Id.ToString(),
            Action: "create",
            Actor: currentUser.EdjeId.ToString(),
            TriggeredBy: "UI",
            Reason: CompassAuditReason.Created("Assignment"),
            Changes:
            [
                new FieldChange("EmployeeId", null, assignment.EmployeeId.ToString()),
                new FieldChange("ClientId", null, assignment.ClientId.ToString()),
                new FieldChange("StartDate", null, assignment.StartDate.ToString("O")),
                new FieldChange("EndDate", null, assignment.EndDate?.ToString("O")),
                new FieldChange("Note", null, assignment.Note),
                new FieldChange(
                    "InvoiceFrequencyTypeId",
                    null,
                    assignment.InvoiceFrequencyTypeId?.ToString()),
            ],
            EffectiveRoles: [.. currentUser.Privileges]);
        await auditService.LogAsync(entry);

        var reloaded = await assignments.GetByIdAsync(assignment.Id, cancellationToken);
        var isCurrent = clientStatusDerivation.IsCurrent(businessDate.Today()).Compile();
        return CompassAssignmentWrite<AssignmentRowDto>.Succeeded(
            ToDto(reloaded ?? assignment, isCurrent(reloaded ?? assignment)));
    }

    /// <inheritdoc />
    public async Task<CompassAssignmentWrite<AssignmentRowDto>> UpdateAsync(
        int id,
        UpdateAssignmentRequest request,
        CancellationToken cancellationToken
    )
    {
        var outcome = await unitOfWork.ExecuteAtomicallyAsync(
            ct => UpdateAsyncCoreAsync(id, request, ct),
            result => result.Write.Status == AdminMutationStatus.Success,
            cancellationToken);

        // The coach hears about a first-time end-dating only once the atomic scope above has closed,
        // not merely after the audited write inside it (contract §6). `ICompassUnitOfWork` opens
        // its transaction on the first save in a scope, so a notice asked for from inside would enrol
        // the notifier's idempotency claim in it; a concurrent claim aborts the transaction (23505) and
        // the reload at the end of the core method then fails 25P02 on an already-committed write.
        if (outcome.NotifyCoach)
        {
            await coachNotifier.NotifyAssignmentEndedAsync(id, cancellationToken);
        }

        return outcome.Write;
    }

    /// <summary>
    /// An audited update's result, plus whether it was the first-time end-dating FR-025 notifies on.
    /// </summary>
    /// <remarks>
    /// The flag travels out with the result because only the update itself can see that transition: it
    /// is the difference between the end date the assignment held on entry and the one the request
    /// sets, and neither the returned row nor a second read afterwards can recover it.
    /// </remarks>
    /// <param name="Write">What the caller answers with.</param>
    /// <param name="NotifyCoach">Whether this update earned the coach a notice.</param>
    private sealed record UpdateOutcome(
        CompassAssignmentWrite<AssignmentRowDto> Write,
        bool NotifyCoach);

    private async Task<UpdateOutcome> UpdateAsyncCoreAsync(
        int id,
        UpdateAssignmentRequest request,
        CancellationToken cancellationToken
    )
    {
        var assignment = await assignments.GetByIdAsync(id, cancellationToken);
        if (assignment is null)
        {
            return new UpdateOutcome(CompassAssignmentWrite<AssignmentRowDto>.NotFound(), false);
        }

        if (request.EndDate is { } endDate && endDate < request.StartDate)
        {
            return new UpdateOutcome(
                CompassAssignmentWrite<AssignmentRowDto>.Invalid(
                    "The end date must be on or after the start date."),
                false);
        }

        // Same selectability rule as the create path, with one exception: an override this assignment
        // already carries is left alone even once the cadence is retired. FR-038 governs what may be
        // selected, and keeping a stored value is not selecting one — without the exemption, retiring a
        // cadence would refuse an edit to a date or a note on a field nobody touched. The client-level
        // default was established the same way; the two surfaces agree deliberately.
        var overrideUnchanged = request.InvoiceFrequencyTypeId == assignment.InvoiceFrequencyTypeId;
        if (!overrideUnchanged
            && request.InvoiceFrequencyTypeId is { } newOverrideId
            && !await assignments.ActiveInvoiceFrequencyTypeExistsAsync(newOverrideId, cancellationToken))
        {
            return new UpdateOutcome(
                CompassAssignmentWrite<AssignmentRowDto>.Invalid(
                    "That invoice frequency does not exist or is no longer selectable."),
                false);
        }

        var wasOpenEnded = assignment.EndDate is null;
        var changes = new List<FieldChange>();

        if (assignment.StartDate != request.StartDate)
        {
            changes.Add(new FieldChange(
                "StartDate", assignment.StartDate.ToString("O"), request.StartDate.ToString("O")));
        }

        if (assignment.EndDate != request.EndDate)
        {
            changes.Add(new FieldChange(
                "EndDate", assignment.EndDate?.ToString("O"), request.EndDate?.ToString("O")));
        }

        if (assignment.Note != request.Note)
        {
            changes.Add(new FieldChange("Note", assignment.Note, request.Note));
        }

        if (assignment.InvoiceFrequencyTypeId != request.InvoiceFrequencyTypeId)
        {
            changes.Add(new FieldChange(
                "InvoiceFrequencyTypeId",
                assignment.InvoiceFrequencyTypeId?.ToString(),
                request.InvoiceFrequencyTypeId?.ToString()));
        }

        assignment.StartDate = request.StartDate;
        assignment.EndDate = request.EndDate;
        assignment.Note = request.Note;
        assignment.InvoiceFrequencyTypeId = request.InvoiceFrequencyTypeId;

        var action = wasOpenEnded && request.EndDate is not null ? "end" : "update";

        var entry = new AuditEntry(
            EntityType: AuditEntityType,
            EntityId: assignment.Id.ToString(),
            Action: action,
            Actor: currentUser.EdjeId.ToString(),
            TriggeredBy: "UI",
            Reason: CompassAuditReason.Updated("Assignment"),
            Changes: changes,
            EffectiveRoles: [.. currentUser.Privileges]);

        try
        {
            await auditService.LogAsync(entry);
        }
        catch (DbUpdateException ex) when (CompassWriteFailure.IsTranslatable(ex))
        {
            return new UpdateOutcome(
                CompassAssignmentWrite<AssignmentRowDto>.Conflict(CompassWriteFailure.MessageFor(ex)),
                false);
        }

        // A first-time end-dating earns the coach a notice, which `UpdateAsync` sends once this
        // scope has closed: a failed write must not send a real email, and an email cannot be recalled.
        // `wasOpenEnded` is what makes this the first time — a concurrent second caller sees a non-null
        // EndDate and never reaches here. The notifier is separately idempotent per assignment, which
        // covers the end date being cleared and set again.
        var notifyCoach = action == "end";

        // Reloaded before projecting, the way CreateAsync does. Changing InvoiceFrequencyTypeId leaves
        // the InvoiceFrequencyType NAVIGATION pointing at the old cadence (or at nothing), so
        // EffectiveInvoiceFrequency would report the value this edit just replaced. Re-reading with the
        // Includes is what makes the response agree with a subsequent GET.
        var refreshed = await assignments.GetByIdAsync(id, cancellationToken) ?? assignment;
        var isCurrent = clientStatusDerivation.IsCurrent(businessDate.Today()).Compile();
        return new UpdateOutcome(
            CompassAssignmentWrite<AssignmentRowDto>.Succeeded(ToDto(refreshed, isCurrent(refreshed))),
            notifyCoach);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A TRUE delete — the module's one scoped exception to Principle VIII/ADR-007. That rule keeps
    /// AC-24/AC-39 reporting history from being truncated, which protects REAL history; this is the
    /// opposite case, a row that was never real, where the right report answer is that it never
    /// existed rather than that it ended. Super-Admin-gated, never <c>CompassOps</c>, and
    /// unconditional: removing a row cannot violate the internal-client or invoice-frequency rules
    /// <see cref="CreateAsyncCoreAsync"/> applies. Cascades to every SOW in the SAME commit — the FK
    /// is <c>DeleteBehavior.Restrict</c>, so child SOWs are staged BEFORE the assignment and EF
    /// orders the DELETEs dependents-first. One audit entry covers the whole cascade.
    /// </remarks>
    public async Task<AdminMutationStatus> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var assignment = await assignments.GetByIdAsync(id, cancellationToken);
        if (assignment is null)
        {
            return AdminMutationStatus.NotFound;
        }

        var sowCount = assignment.Sows.Count;
        foreach (var sow in assignment.Sows.ToList())
        {
            await sows.RemoveAsync(sow, cancellationToken);
        }

        await assignments.RemoveAsync(assignment, cancellationToken);

        var entry = new AuditEntry(
            EntityType: AuditEntityType,
            EntityId: assignment.Id.ToString(),
            Action: "delete",
            Actor: currentUser.EdjeId.ToString(),
            TriggeredBy: "UI",
            Reason: CompassAuditReason.Deleted("Assignment"),
            Changes:
            [
                new FieldChange("EmployeeId", assignment.EmployeeId.ToString(), null),
                new FieldChange("ClientId", assignment.ClientId.ToString(), null),
                new FieldChange("SowCount", sowCount.ToString(), null),
            ],
            EffectiveRoles: [.. currentUser.Privileges]);
        // LogAsync is the commit — it flushes every staged removal above (the SOWs and the
        // assignment) together with the audit row, matching CreateAsyncCoreAsync/UpdateAsyncCoreAsync's
        // ordering.
        await auditService.LogAsync(entry);

        return AdminMutationStatus.Success;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ClientPickerRowDto>> GetClientPickersAsync(CancellationToken cancellationToken) =>
        assignments.GetClientPickerRowsAsync(businessDate.Today(), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<EdjerPickerRowDto>> GetEdjerPickersAsync(CancellationToken cancellationToken) =>
        assignments.GetActiveEdjerPickerRowsAsync(cancellationToken);

    private static AssignmentRowDto ToDto(ClientAssignment assignment, bool isCurrent) => new()
    {
        Id = assignment.Id,
        EmployeeId = assignment.EmployeeId,
        EmployeeName = assignment.Employee is { } employee
            ? CompassDisplayName.For(employee)
            : string.Empty,
        ClientId = assignment.ClientId,
        ClientName = assignment.Client?.ClientName ?? string.Empty,
        StartDate = assignment.StartDate,
        EndDate = assignment.EndDate,
        IsCurrent = isCurrent,

        // The CLIENT's flag, read off the eager-loaded navigation both repository reads
        // already carry. Falls back to false rather than throwing for the same reason `ClientName`
        // does: a projection is not the place to discover a missing Include.
        IsInternal = assignment.Client?.IsInternal ?? false,
        Note = assignment.Note,
        InvoiceFrequencyTypeId = assignment.InvoiceFrequencyTypeId,

        // The whole of FR-037/FR-039 in one expression: the override where set, else the client
        // default, else null meaning "none set". Null is NOT an error and NOT a substitution — no
        // house default is invented here, which is what T108's decoy cadence exists to prove.
        EffectiveInvoiceFrequency =
            assignment.InvoiceFrequencyType?.TypeName
            ?? assignment.Client?.InvoiceFrequencyType?.TypeName,
    };
}
