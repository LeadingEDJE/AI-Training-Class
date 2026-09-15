using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// EDJEr configuration — the module's only write path, shared with client lookups.
/// </summary>
public class CompassEmployeeService(
    ICompassEmployeeRepository employees,
    ICompassUnitOfWork unitOfWork,
    IAuditService auditService,
    ICurrentUserContext currentUser,
    IEdjerDeactivationGuard deactivationGuard
) : ICompassEmployeeService
{
    private const string AuditEntityType = "CompassEmployee";

    private const string AuditSubject = "EDJEr";

    private string AuditTriggeredBy => CompassAuditTrigger.For(currentUser.EdjeId);

    private const int MaxNameLength = 100;
    private const int MaxEmailLength = 255;

    // reads

    /// <inheritdoc />
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
            // Absent means Pacific, per the client default described in docs/compass-timezones.md.
            Timezone = NormaliseTimezone(request.Timezone) ?? UsTimeZones.Default,
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

        // Fires on every update regardless of whether the write is deactivating the record, since
        // the guard itself decides whether a transition is being requested.
        if (request.IsActive is false)
        {
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

        // Overwritten unconditionally like every other field above; a request that omits the timezone
        // resets it to the column default, since silence is treated as an explicit choice here.
        if (NormaliseTimezone(request.Timezone) is { } timezone)
        {
            employee.Timezone = timezone;
        }

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
            return CompassLegacyProvenance.IsProvenanceCollision(exception)
                ? DuplicateProvenance(request.LegacyTpsId)
                : DuplicateEmail(request.Email);
        }

        await LogAsync(employee.Id, "update", CompassAuditReason.Updated(AuditSubject), changes);

        return CompassWrite<CompassEdjerDto>.Succeeded(ToDto(employee));
    }

    // validation

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

        // Blank and null are treated identically here and both accepted, per the timezone spec in
        // docs/edjer-timezone-rules.md.
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

        // Only an exact match on the existing type is exempt from the active check; a migration write
        // must always name an active classification.
        var typeUnchanged = existing is not null && existing.EmployeeTypeId == request.EmployeeTypeId;

        var isMigrationWrite = CompassLegacyProvenance.Normalise(request.LegacyTpsId) is not null;

        var classificationAcceptable =
            typeUnchanged
            || (
                isMigrationWrite
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
            // Self-coaching is blocked only on create; an update may set an EDJEr as their own coach,
            // since the client-side picker already filters that case out for edits.
            if (existing is not null && coachId == existing.Id)
            {
                return Invalid("An EDJEr cannot be their own coach.");
            }

            if (!await employees.ExistsAsync(coachId, cancellationToken))
            {
                return Invalid("That coach is not an existing EDJEr.");
            }

            // Applies to every inactive coach, including one already on record, so a record whose coach
            // was later deactivated becomes permanently unsaveable until the coach is changed.
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
    /// Whether the address is a valid RFC 5322 mailbox, checked in full since AC-17 requires a
    /// corporate domain.
    /// </summary>
    private static bool IsPlausibleEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at < email.Length - 1 && email.IndexOf('@', at + 1) < 0;
    }

    /// <summary>
    /// Lower-cased and trimmed only for comparison purposes; the original casing is what gets stored.
    /// </summary>
    private static string NormaliseEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Upper-cased and trimmed, so the <c>char(2)</c> column holds one form.</summary>
    private static string NormaliseState(string? state) =>
        (state ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// The requested timezone, upper-cased and trimmed the same way <see cref="NormaliseState"/> is.
    /// </summary>
    private static string? NormaliseTimezone(string? timezone)
    {
        var trimmed = timezone?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static CompassWrite<CompassEdjerDto> Invalid(string error) =>
        CompassWrite<CompassEdjerDto>.Invalid(error);

    /// <summary>
    /// The rejection for an address already in use, naming the other EDJEr who holds it.
    /// </summary>
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

    private List<string> EffectiveRoles => [.. currentUser.Privileges];

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
    /// Every field of the update, including the ones that did not change, recorded as no-ops.
    /// </summary>
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

        // Falls back to the existing value only for the audit comparison; the entity assignment above
        // uses the column default instead when the request omits the field.
        Compare(
            nameof(Employee.Timezone),
            before.Timezone,
            NormaliseTimezone(after.Timezone) ?? before.Timezone
        );

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
