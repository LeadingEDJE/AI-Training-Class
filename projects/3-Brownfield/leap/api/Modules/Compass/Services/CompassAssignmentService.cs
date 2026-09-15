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
    /// What the audit trail calls a client assignment. Kept short so the reporting export column
    /// stays under the legacy 20-character limit described in the retired audit-export spec.
    /// </summary>
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
        // Migration writes are exempt from every validation rule in this method per the bulk-import
        // design note, not just the active-employee check below.
        var isMigrationWrite = CompassLegacyProvenance.Normalise(request.LegacyTpsId) is not null;

        var employee = await assignments.GetEmployeeAsync(request.EmployeeId, cancellationToken);

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

        // Retired cadences are allowed here for backward compatibility with the original TPS import
        // batch; this check only rejects an id that never existed at all.
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

        if (outcome.NotifyCoach)
        {
            await coachNotifier.NotifyAssignmentEndedAsync(id, cancellationToken);
        }

        return outcome.Write;
    }

    /// <summary>
    /// An audited update's result, plus whether the coach should be re-notified on every save.
    /// </summary>
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

        // Mirrors the create path's cadence check exactly, no exceptions.
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

        // The coach is notified on every update to this assignment, not only the first.
        var notifyCoach = action == "end";

        var refreshed = await assignments.GetByIdAsync(id, cancellationToken) ?? assignment;
        var isCurrent = clientStatusDerivation.IsCurrent(businessDate.Today()).Compile();
        return new UpdateOutcome(
            CompassAssignmentWrite<AssignmentRowDto>.Succeeded(ToDto(refreshed, isCurrent(refreshed))),
            notifyCoach);
    }

    /// <inheritdoc />
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

        // This is the EMPLOYEE's internal flag, not the client's.
        IsInternal = assignment.Client?.IsInternal ?? false,
        Note = assignment.Note,
        InvoiceFrequencyTypeId = assignment.InvoiceFrequencyTypeId,

        EffectiveInvoiceFrequency =
            assignment.InvoiceFrequencyType?.TypeName
            ?? assignment.Client?.InvoiceFrequencyType?.TypeName,
    };
}
