using System.Text.Json.Serialization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Contracts;

/// <summary>
/// The one contract the Compass directory boundary returns — from both transports.
/// </summary>
/// <remarks>
/// The in-process <see cref="IDirectory"/> and the versioned HTTP endpoint return this same type, so
/// a later extraction is a transport swap rather than a rewrite. Do not add a second, endpoint-only
/// shape. Every member is derived from the Compass employee record, never speculative; a contract
/// test pins the member set, and a rename or type change is breaking (FR-024).
///
/// No migration-provenance member belongs here: <c>legacy_tps_id</c> is absent from every
/// application-facing payload, and the route exposing the raw map is gated on principal identity so a
/// Compass Super Admin cannot read it — this type's HTTP twin is role-gated, so publishing it here
/// would reach that withheld audience. <c>CompassEmployeeDtoTests</c> fails if one reappears.
/// </remarks>
public sealed class CompassEmployeeDto
{
    /// <summary>
    /// The Compass employee's identifier — <c>compass.employee.employee_id</c>.
    /// </summary>
    /// <remarks>
    /// An <c>int</c> by owner decision D-2: Compass records are referenced across the platform by
    /// their native integer keys. It was a <c>Guid</c> while the boundary resolved against the legacy
    /// <c>public.employees</c>, whose preserved uuid primary key has no counterpart in the Compass
    /// store.
    /// </remarks>
    public int Id { get; init; }

    /// <summary>The employee's display name, assembled from the given and family names.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Whether the employee record is active.</summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// The employee's email address — unique across active and inactive EDJErs alike (BR-9).
    /// </summary>
    /// <remarks>
    /// The email-keyed reads are unusable without it: <see cref="IDirectory.GetEmployeesByEmailAsync"/>
    /// answers for a set of addresses and omits the ones matching nothing, so a caller cannot map a
    /// returned record back to its request without the key.
    ///
    /// Not speculative, which is the Principle II test: stored, required and unique on
    /// <c>compass.employee</c>, and already published to any authenticated EDJEr by
    /// <c>TeamDirectoryRowDto.Email</c>, so it discloses no new class of data to this type's narrower
    /// Compass-admin callers. It traces to FR-8.4, and it is the attribute a consumer correlates a
    /// Compass record with a legacy one on. Not tier-gated, unlike <see cref="TimeTracking"/>.
    /// </remarks>
    public string Email { get; init; } = string.Empty;

    /// <summary>The employee's hire date.</summary>
    public DateOnly HireDate { get; init; }

    /// <summary>The employee type's name, resolved through <c>EmployeeTypeId</c>.</summary>
    public string EmployeeType { get; init; } = string.Empty;

    /// <summary>The employee's state of residence.</summary>
    public string StateOfResidence { get; init; } = string.Empty;

    /// <summary>
    /// The employee's timezone, as an IANA identifier (FR-8.4, FR-8.6).
    /// </summary>
    /// <remarks>
    /// Published as the stored IANA id, not a resolved <c>TimeZoneInfo</c>: it crosses the transport
    /// unchanged and means the same thing on a host whose zone database differs. The accepted values
    /// are <c>UsTimeZones.All</c>, widenable without a schema change, so nothing here enumerates them.
    ///
    /// Not tier-gated, unlike <see cref="TimeTracking"/>, though route-level authorization is
    /// unchanged: FR-8.4's consumer is OOTO over the in-process transport, where it holds no Compass
    /// privilege and resolves to the Baseline tier, so a tier gate would withhold the field from the
    /// only caller it exists for. Reading it here is how OOTO stops reading the frozen
    /// <c>public.employees</c>; it is not an invitation to add a second OOTO-shaped field.
    /// </remarks>
    public string Timezone { get; init; } = string.Empty;

    /// <summary>Whether the EDJEr is on the delivery team.</summary>
    /// <remarks>
    /// Not tier-gated, unlike <see cref="TimeTracking"/>. It is an ordinary directory attribute in
    /// the same class as <see cref="StateOfResidence"/> and <see cref="Timezone"/>, so every caller
    /// that reaches this read gets it. That is a statement about the payload, not about access —
    /// route-level authorization is unchanged, and the HTTP twin still requires the Compass admin
    /// policy. The value is stored on <c>compass.employee</c> and defaults to <c>true</c>; the
    /// Timesheet module's same-named value is derived from Delivery-category job titles, falling
    /// back to employment records when the title is blank, and is a different thing.
    /// </remarks>
    public bool IsDeliveryTeam { get; init; }

    /// <summary>The employee's coach, or <c>null</c> when they have none.</summary>
    public CompassEmployeeCoachDto? Coach { get; init; }

    /// <summary>
    /// The three time-tracking flags — Compass Super Admin only (BR-1). Absent, not null, for
    /// every other tier — a caller who cannot see this must not be able to infer it exists.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompassEmployeeTimeTrackingDto? TimeTracking { get; init; }
}

/// <summary>
/// An employee's coach — id and display name only, never a nested profile (FR-008), so the recursion
/// cannot follow the coach's coach indefinitely.
/// </summary>
public sealed class CompassEmployeeCoachDto
{
    /// <summary>The coach's identifier.</summary>
    public int Id { get; init; }

    /// <summary>The coach's display name.</summary>
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>The three time-tracking flags (BR-1, Super Admin only).</summary>
public sealed class CompassEmployeeTimeTrackingDto
{
    /// <summary>Whether the employee must submit timesheets.</summary>
    public bool TimesheetRequired { get; init; }

    /// <summary>Whether they may submit under 40 hours.</summary>
    public bool CanSubmitUnder40 { get; init; }

    /// <summary>Whether they are included in payroll.</summary>
    public bool IncludeInPayroll { get; init; }
}
