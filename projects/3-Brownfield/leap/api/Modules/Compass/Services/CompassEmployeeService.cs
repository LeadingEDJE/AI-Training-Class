using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// EDJEr configuration — the module's second write path, and its first audited one.
/// </summary>
/// <remarks>
/// Audited, unlike the lookup service: FR-017 and Principle VIII require every EDJEr write to be
/// attributable with the actor's effective roles, while FR-008 and AC-NFR-3 place lookups outside the
/// trail, and <c>CompassLookupServiceTests</c> and <c>CompassEmployeeServiceTests</c> assert both
/// structurally. The save happens before the audit call, against <c>AuditService.LogAsync</c>'s own
/// suggestion, for two reasons: a create needs the identity key for <c>AuditEntry.EntityId</c>, which
/// EF assigns at save, and <see cref="ICompassUnitOfWork"/> is what translates a lost uniqueness race
/// into <see cref="CompassDuplicateKeyException"/> rather than a provider failure and a bare 500. The
/// cost is that an audit failure after a successful write leaves it unaudited — the lesser risk.
/// </remarks>
public class CompassEmployeeService(
    ICompassEmployeeRepository employees,
    ICompassUnitOfWork unitOfWork,
    IAuditService auditService,
    ICurrentUserContext currentUser,
    IEdjerDeactivationGuard deactivationGuard
) : ICompassEmployeeService
{
    /// <summary>What the audit trail calls an EDJEr, so every entry for one is findable together.</summary>
    private const string AuditEntityType = "CompassEmployee";

    /// <summary>The subject <see cref="CompassAuditReason"/> composes its reasons from.</summary>
    private const string AuditSubject = "EDJEr";

    /// <summary>Who the change came through.</summary>
    /// <remarks>
    /// Resolved per write rather than held constant: a bulk migration holds the Compass root role and is
    /// otherwise indistinguishable from an administrator, which Principle VIII forbids. See
    /// <see cref="CompassAuditTrigger"/>.
    /// </remarks>
    private string AuditTriggeredBy => CompassAuditTrigger.For(currentUser.EdjeId);

    private const int MaxNameLength = 100;
    private const int MaxEmailLength = 255;

    // reads

    /// <inheritdoc />
    /// <remarks>
    /// The coach's name is resolved from THIS SAME read rather than a second query: the collection
    /// already holds every EDJEr, coaches included, so a lookup by id against it costs nothing extra
    /// (issue #659) — the same N+1-avoidance reasoning as <c>EmployeeTypeName</c>.
    /// </remarks>
    public async Task<IReadOnlyList<CompassEdjerSummaryDto>> GetEdjersAsync(
        CancellationToken cancellationToken
    )
    {
        var rows = await employees.GetAllWithTypeNameAsync(cancellationToken);
        var namesById = rows.ToDictionary(
            row => row.Employee.Id,
            row => CompassDisplayName.For(row.Employee)
        );

        return
        [
            .. rows.Select(row => new CompassEdjerSummaryDto(
                row.Employee.Id,
                row.Employee.FirstName,
                row.Employee.LastName,
                row.Employee.Email,
                row.EmployeeTypeName,
                row.Employee.IsActive,
                row.Employee.HireDate,
                row.Employee.StateOfResidence,
                row.Employee.CoachEmployeeId is { } coachId
                    ? namesById.GetValueOrDefault(coachId)
                    : null
            )),
        ];
    }

    /// <inheritdoc />
    public async Task<CompassEdjerDto?> GetEdjerAsync(int id, CancellationToken cancellationToken)
    {
        var employee = await employees.GetByIdAsync(id, cancellationToken);
        return employee is null ? null : ToDto(employee);
    }

    // writes

    /// <inheritdoc />
    public Task<CompassWrite<CompassEdjerDto>> CreateEdjerAsync(
        CompassEdjerRequest request,
        CancellationToken cancellationToken
    ) =>
        unitOfWork.ExecuteAtomicallyAsync(
            ct => CreateEdjerAsyncCoreAsync(request, ct),
            result => result.Status == CompassWriteStatus.Success,
            cancellationToken);

    private async Task<CompassWrite<CompassEdjerDto>> CreateEdjerAsyncCoreAsync(
        CompassEdjerRequest request,
        CancellationToken cancellationToken
    )
    {
        var validation = await ValidateAsync(request, existing: null, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var employee = new Employee
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            HireDate = request.HireDate,
            Email = NormaliseEmail(request.Email),
            EmployeeTypeId = request.EmployeeTypeId,
            CoachEmployeeId = request.CoachEmployeeId,
            StateOfResidence = NormaliseState(request.StateOfResidence),
            IsActive = request.IsActive,
            TimesheetRequired = request.TimesheetRequired,
            CanSubmitUnder40 = request.CanSubmitUnder40,
            IncludeInPayroll = request.IncludeInPayroll,
            // Absent means the caller said nothing about the zone, which only a client older than
            // FR-8.1 does — it takes the same Eastern the column itself defaults to, rather than a
            // second spelling of that decision. Validated above, so anything present is one of the six.
            Timezone = NormaliseTimezone(request.Timezone) ?? UsTimeZones.Default,
            // Same shape, same reason: absent means the caller said nothing, and the ruling on
            // issue #502 is that everybody starts on the delivery team. Written explicitly rather
            // than left to the column default, so an application create does not depend on it.
            IsDeliveryTeam = request.IsDeliveryTeam ?? true,
            LegacyTpsId = CompassLegacyProvenance.Normalise(request.LegacyTpsId),
        };

        await employees.AddAsync(employee, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (CompassDuplicateKeyException exception)
        {
            // The email pre-check above is a check-then-act: a concurrent caller can commit this same
            // address between it and this write, and ux_employee_email_ci then rejects ours. The losing
            // writer takes the same path as the sequential duplicate rather than escaping as a 500.
            // compass.employee carries a second unique index, ux_employee_legacy_tps_id, which nothing
            // pre-checks, so assuming the email index would report a provenance collision as an email one.
            return CompassLegacyProvenance.IsProvenanceCollision(exception)
                ? DuplicateProvenance(request.LegacyTpsId)
                : DuplicateEmail(request.Email);
        }

        await LogAsync(
            employee.Id,
            "create",
            CompassAuditReason.Created(AuditSubject),
            CreationChanges(employee)
        );

        return CompassWrite<CompassEdjerDto>.Succeeded(ToDto(employee));
    }

    /// <inheritdoc />
    public Task<CompassWrite<CompassEdjerDto>> UpdateEdjerAsync(
        int id,
        CompassEdjerRequest request,
        CancellationToken cancellationToken
    ) =>
        unitOfWork.ExecuteAtomicallyAsync(
            ct => UpdateEdjerAsyncCoreAsync(id, request, ct),
            result => result.Status == CompassWriteStatus.Success,
            cancellationToken);

    private async Task<CompassWrite<CompassEdjerDto>> UpdateEdjerAsyncCoreAsync(
        int id,
        CompassEdjerRequest request,
        CancellationToken cancellationToken
    )
    {
        var employee = await employees.GetByIdAsync(id, cancellationToken);
        if (employee is null)
        {
            return CompassWrite<CompassEdjerDto>.NotFound();
        }

        var validation = await ValidateAsync(request, employee, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        // The deactivation guard fires only when this request asks for the transition active →
        // inactive, which is what AC-19 describes. The request-shaped half of that condition stays
        // here, because only this method knows what was asked for; the state-shaped half — is this
        // EDJEr deactivatable at all — belongs to IEdjerDeactivationGuard, which the blockers route
        // reads without coming through here. Deriving it in both places would be two answers to one.
        if (request.IsActive is false)
        {
            // The entity overload, not the id one: `employee` is loaded above and has not yet had the
            // request applied to it, which is exactly what the guard needs. Passing the id instead
            // re-reads the same row on every isActive:false submission.
            var verdict = await deactivationGuard.EvaluateAsync(employee, cancellationToken);
            if (verdict.Status == EdjerDeactivationStatus.Blocked)
            {
                return CompassWrite<CompassEdjerDto>.Blocked(
                    $"{CompassDisplayName.For(employee)} still holds "
                        + $"{verdict.BlockingAssignments.Count} assignment(s) with no end date. End-date "
                        + "them first, then retry the deactivation. No assignment is ended automatically.",
                    verdict.BlockingAssignments
                );
            }
        }

        var changes = UpdateChanges(employee, request);

        employee.FirstName = request.FirstName.Trim();
        employee.LastName = request.LastName.Trim();
        employee.HireDate = request.HireDate;
        employee.Email = NormaliseEmail(request.Email);
        employee.EmployeeTypeId = request.EmployeeTypeId;
        employee.CoachEmployeeId = request.CoachEmployeeId;
        employee.StateOfResidence = NormaliseState(request.StateOfResidence);
        employee.IsActive = request.IsActive;
        employee.TimesheetRequired = request.TimesheetRequired;
        employee.CanSubmitUnder40 = request.CanSubmitUnder40;
        employee.IncludeInPayroll = request.IncludeInPayroll;

        // Assigned ONLY when the request carried one. Every other field above is overwritten
        // unconditionally, and doing that here would let a caller that predates FR-8.1 reset a
        // deliberately-chosen Pacific to Eastern by saving some unrelated field — the wire-optional
        // parameter's whole point is that absent means "unchanged", not "make it the default".
        if (NormaliseTimezone(request.Timezone) is { } timezone)
        {
            employee.Timezone = timezone;
        }

        // Same shape, same reason. The migration tool's coach pass re-PUTs the whole original create
        // payload, so an unconditional assignment would reset a hand-made correction to false back
        // to the blanket true nobody has reviewed (FR-006).
        if (request.IsDeliveryTeam is { } isDeliveryTeam)
        {
            employee.IsDeliveryTeam = isDeliveryTeam;
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (CompassDuplicateKeyException exception)
        {
            // Same race as the create path, and the same two indexes — see the create path's remark.
            return CompassLegacyProvenance.IsProvenanceCollision(exception)
                ? DuplicateProvenance(request.LegacyTpsId)
                : DuplicateEmail(request.Email);
        }

        await LogAsync(employee.Id, "update", CompassAuditReason.Updated(AuditSubject), changes);

        return CompassWrite<CompassEdjerDto>.Succeeded(ToDto(employee));
    }

    // validation

    /// <summary>
    /// Every rule that can refuse a write, in the order that produces the most useful message.
    /// </summary>
    /// <param name="request">The submitted configuration.</param>
    /// <param name="existing">
    /// The stored record on an update, or null on a create. It is what lets an edit keep a classification
    /// that was retired after the EDJEr was classified by it (FR-007) — see the employee-type rule.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rejection, or null when the request is acceptable.</returns>
    private async Task<CompassWrite<CompassEdjerDto>?> ValidateAsync(
        CompassEdjerRequest request,
        Employee? existing,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            return Invalid("A first name is required.");
        }

        if (request.FirstName.Trim().Length > MaxNameLength)
        {
            return Invalid($"A first name may be at most {MaxNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            return Invalid("A last name is required.");
        }

        if (request.LastName.Trim().Length > MaxNameLength)
        {
            return Invalid($"A last name may be at most {MaxNameLength} characters.");
        }

        if (request.HireDate == default)
        {
            return Invalid("A hire date is required.");
        }

        var email = NormaliseEmail(request.Email);
        if (email.Length == 0)
        {
            return Invalid("An email address is required.");
        }

        if (email.Length > MaxEmailLength)
        {
            return Invalid($"An email address may be at most {MaxEmailLength} characters.");
        }

        if (!IsPlausibleEmail(email))
        {
            return Invalid("That email address is not usable.");
        }

        if (!UsStateCodes.IsValid(request.StateOfResidence?.Trim()))
        {
            return Invalid("A state of residence must be one of the 50 US states or DC.");
        }

        // FR-8.1. Three outcomes, not two, and the middle one is the reason this is not a one-liner:
        // null is a caller that predates the field and is accepted (see CompassEdjerRequest.Timezone),
        // blank is a form submitted with nothing chosen and is REFUSED, and anything else must be one
        // of the six. Folding blank into null would make the required field silently optional.
        if (request.Timezone is not null)
        {
            var timezone = request.Timezone.Trim();

            if (timezone.Length == 0)
            {
                return Invalid("A timezone is required.");
            }

            if (!UsTimeZones.IsValid(timezone))
            {
                return Invalid(
                    "A timezone must be one of the six supported US zones, as an IANA identifier."
                );
            }
        }

        // FR-005: only ACTIVE employee types may be chosen — the server refuses an inactive id even
        // though the selection list omits it. The exception is an edit that LEAVES an already-carried
        // classification alone: FR-007 says retiring a value must not rewrite the records using it, so an
        // edit to some other field must not be forced to re-select a now-retired type.
        var typeUnchanged = existing is not null && existing.EmployeeTypeId == request.EmployeeTypeId;

        // The second exception: a migration write may name a retired classification, because it reports
        // what the legacy directory recorded rather than choosing a classification now. `Intern` exists
        // for exactly this and is seeded inactive so no person can apply it to a new hire. Keying on
        // `legacyTpsId` is sound because the endpoint's CompassLegacyProvenance.MaySet already refused a
        // non-migration caller who supplied one, so the service needs no ClaimsPrincipal to know it.
        var isMigrationWrite = CompassLegacyProvenance.Normalise(request.LegacyTpsId) is not null;

        var classificationAcceptable =
            typeUnchanged
            || (
                isMigrationWrite
                    // Existence is still required -- see EmployeeTypeExistsAsync. Only the ACTIVE
                    // half of the check is waived.
                    ? await employees.EmployeeTypeExistsAsync(
                        request.EmployeeTypeId,
                        cancellationToken
                    )
                    : await employees.ActiveEmployeeTypeExistsAsync(
                        request.EmployeeTypeId,
                        cancellationToken
                    )
            );

        if (!classificationAcceptable)
        {
            return Invalid("That employee type does not exist or is no longer selectable.");
        }

        if (request.CoachEmployeeId is { } coachId)
        {
            // An EDJEr cannot coach themselves. `EdjerFormPage` omits the record from its own coach
            // picker, but FR-041 makes the client's filter a convenience and never the control. It is
            // not merely nonsensical: ICurrentUserContext scopes a Manager to direct reports, so a
            // self-reference puts a one-node cycle into any walk over that hierarchy, in another module.
            // Only reachable on update, hence reading `existing` rather than assuming there is an id.
            if (existing is not null && coachId == existing.Id)
            {
                return Invalid("An EDJEr cannot be their own coach.");
            }

            if (!await employees.ExistsAsync(coachId, cancellationToken))
            {
                return Invalid("That coach is not an existing EDJEr.");
            }

            // A FORMER EDJEr may not be nominated as a coach. Scoped to a CHANGE, not to
            // every inactive coach: an EDJEr whose coach was deactivated after the fact must stay
            // editable, and saving an unrelated field resends the same id. Refusing that would make the
            // record permanently unsaveable -- the server-side twin of the select desync the form
            // avoids by retaining the assigned coach as a labelled option.
            if (
                existing?.CoachEmployeeId != coachId
                && !await employees.ActiveEmployeeExistsAsync(coachId, cancellationToken)
            )
            {
                return Invalid("A former EDJEr cannot be assigned as a coach.");
            }
        }

        if (await employees.EmailExistsAsync(email, existing?.Id, cancellationToken))
        {
            return DuplicateEmail(request.Email);
        }

        return null;
    }

    /// <summary>
    /// Whether the address could be one at all — something before an <c>@</c> and something after it.
    /// </summary>
    /// <remarks>
    /// Deliberately shallow. Email is the identity BR-9 turns on, so a value that cannot be an address is
    /// worth a 400 rather than a row nobody can ever match — but full RFC validation rejects addresses
    /// that genuinely work, and no acceptance criterion asks for it. The domain is not checked either:
    /// AC-17 says unique, not corporate.
    /// </remarks>
    private static bool IsPlausibleEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at < email.Length - 1 && email.IndexOf('@', at + 1) < 0;
    }

    /// <summary>
    /// Lower-cased and trimmed — the same normalisation <c>ux_employee_email_ci</c> indexes.
    /// </summary>
    /// <remarks>
    /// Applied to what is STORED, not only to what is compared. Storing " Ada@Example.test " while the
    /// unique index reads <c>lower(btrim(email))</c> would leave the table holding a value no later
    /// exact-match lookup could find.
    /// </remarks>
    private static string NormaliseEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Upper-cased and trimmed, so the <c>char(2)</c> column holds one form.</summary>
    private static string NormaliseState(string? state) =>
        (state ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// The requested timezone, or null when the request said nothing about it.
    /// </summary>
    /// <remarks>
    /// Trimmed but not case-folded, unlike <see cref="NormaliseState"/>: IANA identifiers are
    /// case-sensitive, so there is no second spelling to normalise away — a differently-cased value is
    /// a different value, and <see cref="UsTimeZones.IsValid"/> has already refused it by the time
    /// this runs. Null and blank collapse to the same "said nothing" only AFTER validation, which is
    /// where blank is rejected; this method is never reached with one.
    /// </remarks>
    private static string? NormaliseTimezone(string? timezone)
    {
        var trimmed = timezone?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static CompassWrite<CompassEdjerDto> Invalid(string error) =>
        CompassWrite<CompassEdjerDto>.Invalid(error);

    /// <summary>
    /// The rejection for an address already in use.
    /// </summary>
    /// <remarks>
    /// Shared by the pre-check and the lost-race path deliberately: a caller who loses a race must get the
    /// same answer as one who was simply second, or the outcome would depend on timing they cannot see.
    /// It names the field, which FR-013 requires — and says nothing about WHO holds the address, because
    /// that would disclose an EDJEr's existence to an administrator who may not be entitled to see them.
    /// </remarks>
    private static CompassWrite<CompassEdjerDto> DuplicateProvenance(string? legacyTpsId) =>
        CompassWrite<CompassEdjerDto>.Duplicate(
            CompassLegacyProvenance.CollisionMessage(legacyTpsId, "EDJEr")
        );

    private static CompassWrite<CompassEdjerDto> DuplicateEmail(string? email) =>
        CompassWrite<CompassEdjerDto>.Duplicate(
            $"The email address '{NormaliseEmail(email)}' is already used by another EDJEr, "
                + "active or inactive."
        );

    // audit

    private string Actor => currentUser.EdjeId.ToString();

    /// <summary>
    /// The actor's whole privilege set.
    /// </summary>
    /// <remarks>
    /// Unfiltered, deliberately — not narrowed to a <c>Compass </c> prefix (owner decision, restated in
    /// <see cref="AuditEntry"/>'s remarks). What made a write possible may include a role from outside
    /// this module, and an audit trail that hides that answers a different question than the one an
    /// auditor asked.
    /// </remarks>
    private List<string> EffectiveRoles => [.. currentUser.Privileges];

    /// <summary>
    /// Writes one audit entry for this surface, supplying the four fields every EDJEr entry records
    /// identically: the entity type, the actor, who the change came through, and the effective roles.
    /// </summary>
    /// <remarks>
    /// The point is that <see cref="EffectiveRoles"/> cannot be forgotten. It is an optional
    /// parameter on <see cref="AuditEntry"/>, so a hand-built entry that omits it compiles and logs
    /// perfectly happily while silently failing FR-017. Routing every write through here makes the
    /// omission unrepresentable rather than merely discouraged.
    /// </remarks>
    /// <param name="entityId">The EDJEr the entry is about.</param>
    /// <param name="action">The audit action, <c>create</c> or <c>update</c>.</param>
    /// <param name="reason">A non-blank reason, composed by <see cref="CompassAuditReason"/>.</param>
    /// <param name="changes">The field-level changes the entry records.</param>
    private Task LogAsync(
        int entityId,
        string action,
        string reason,
        List<FieldChange> changes
    ) =>
        auditService.LogAsync(
            new AuditEntry(
                AuditEntityType,
                entityId.ToString(),
                action,
                Actor,
                AuditTriggeredBy,
                reason,
                changes,
                EffectiveRoles: EffectiveRoles
            )
        );

    /// <summary>Every field of a new EDJEr, as a creation has no "before".</summary>
    private static List<FieldChange> CreationChanges(Employee employee) =>
        [
            new(nameof(Employee.FirstName), null, employee.FirstName),
            new(nameof(Employee.LastName), null, employee.LastName),
            new(nameof(Employee.HireDate), null, employee.HireDate.ToString("O")),
            new(nameof(Employee.Email), null, employee.Email),
            new(nameof(Employee.EmployeeTypeId), null, employee.EmployeeTypeId.ToString()),
            new(nameof(Employee.CoachEmployeeId), null, employee.CoachEmployeeId?.ToString()),
            new(nameof(Employee.StateOfResidence), null, employee.StateOfResidence),
            new(nameof(Employee.IsActive), null, employee.IsActive.ToString()),
            new(nameof(Employee.TimesheetRequired), null, employee.TimesheetRequired.ToString()),
            new(nameof(Employee.CanSubmitUnder40), null, employee.CanSubmitUnder40.ToString()),
            new(nameof(Employee.IncludeInPayroll), null, employee.IncludeInPayroll.ToString()),
            new(nameof(Employee.Timezone), null, employee.Timezone),
            new(nameof(Employee.IsDeliveryTeam), null, employee.IsDeliveryTeam.ToString()),
        ];

    /// <summary>
    /// Only the fields an update actually changes.
    /// </summary>
    /// <remarks>
    /// Computed BEFORE the entity is mutated, because afterwards there is no "before" left to read.
    /// Unchanged fields are omitted rather than recorded as no-ops: a change list where ten of eleven
    /// entries say nothing happened makes the one that did harder to find.
    /// </remarks>
    private static List<FieldChange> UpdateChanges(Employee before, CompassEdjerRequest after)
    {
        List<FieldChange> changes = [];

        Compare(nameof(Employee.FirstName), before.FirstName, after.FirstName.Trim());
        Compare(nameof(Employee.LastName), before.LastName, after.LastName.Trim());
        Compare(
            nameof(Employee.HireDate),
            before.HireDate.ToString("O"),
            after.HireDate.ToString("O")
        );
        Compare(nameof(Employee.Email), before.Email, NormaliseEmail(after.Email));
        Compare(
            nameof(Employee.EmployeeTypeId),
            before.EmployeeTypeId.ToString(),
            after.EmployeeTypeId.ToString()
        );
        Compare(
            nameof(Employee.CoachEmployeeId),
            before.CoachEmployeeId?.ToString(),
            after.CoachEmployeeId?.ToString()
        );
        Compare(
            nameof(Employee.StateOfResidence),
            before.StateOfResidence,
            NormaliseState(after.StateOfResidence)
        );
        Compare(nameof(Employee.IsActive), before.IsActive.ToString(), after.IsActive.ToString());
        Compare(
            nameof(Employee.TimesheetRequired),
            before.TimesheetRequired.ToString(),
            after.TimesheetRequired.ToString()
        );
        Compare(
            nameof(Employee.CanSubmitUnder40),
            before.CanSubmitUnder40.ToString(),
            after.CanSubmitUnder40.ToString()
        );
        Compare(
            nameof(Employee.IncludeInPayroll),
            before.IncludeInPayroll.ToString(),
            after.IncludeInPayroll.ToString()
        );

        // `?? before.Timezone` so an absent timezone compares equal and records nothing — it is the
        // update path's "leave it alone", and reporting a change the write did not make would be a
        // false entry in a trail FR-017 exists to make trustworthy.
        Compare(
            nameof(Employee.Timezone),
            before.Timezone,
            NormaliseTimezone(after.Timezone) ?? before.Timezone
        );

        // `?? before.IsDeliveryTeam` for the same reason, and it matters more here: a bare
        // `after.IsDeliveryTeam.ToString()` renders an absent value as "", which compares unequal to
        // "True" and fabricates an entry on every save by a client that omits the field.
        Compare(
            nameof(Employee.IsDeliveryTeam),
            before.IsDeliveryTeam.ToString(),
            (after.IsDeliveryTeam ?? before.IsDeliveryTeam).ToString()
        );

        return changes;

        void Compare(string field, string? previous, string? next)
        {
            if (!string.Equals(previous, next, StringComparison.Ordinal))
            {
                changes.Add(new FieldChange(field, previous, next));
            }
        }
    }

    private static CompassEdjerDto ToDto(Employee employee) =>
        new(
            employee.Id,
            employee.FirstName,
            employee.LastName,
            employee.HireDate,
            employee.Email,
            employee.EmployeeTypeId,
            employee.CoachEmployeeId,
            employee.StateOfResidence,
            employee.IsActive,
            employee.TimesheetRequired,
            employee.CanSubmitUnder40,
            employee.IncludeInPayroll,
            employee.Timezone,
            employee.IsDeliveryTeam
        );
}
