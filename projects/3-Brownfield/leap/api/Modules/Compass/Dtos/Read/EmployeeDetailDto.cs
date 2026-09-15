using System.Text.Json.Serialization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>The read-only employee detail (AC-8, AC-10, AC-11), projected for the viewer's tier.</summary>
public sealed class EmployeeDetailDto
{
    /// <summary>The EDJEr's identifier.</summary>
    public int Id { get; init; }

    /// <summary>Given name.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Family name.</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>Hire date.</summary>
    public DateOnly HireDate { get; init; }

    /// <summary>Work email.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Employee type name.</summary>
    public string EmployeeType { get; init; } = string.Empty;

    /// <summary>
    /// The coach's own identifier — the drill-in into their record. <c>null</c> exactly when
    /// <see cref="Coach"/> is.
    /// </summary>
    public int? CoachId { get; init; }

    /// <summary>The coach's display name, or <c>null</c> where the EDJEr has none (AC-17).</summary>
    public string? Coach { get; init; }

    /// <summary>State of residence.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>The technical skills this EDJEr is tagged with.</summary>
    public IReadOnlyList<SkillOptionDto> Skills { get; init; } = [];

    /// <summary>Whether this EDJEr is on the delivery team.</summary>
    /// <remarks>
    /// Visible only to Compass Super Admin, the same gating as <see cref="TimeTrackingSettings"/> —
    /// a baseline viewer never receives this field.
    /// </remarks>
    public bool IsDeliveryTeam { get; init; }

    /// <summary>Whether the EDJEr is active — elevated tiers only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsActive { get; init; }

    /// <summary>Every assignment held, current and ended (AC-8). Never truncated by retention (BR-16).</summary>
    public IReadOnlyList<EmployeeAssignmentDto> AssignmentHistory { get; init; } = [];

    /// <summary>Every EDJEr who lists this record's subject as their coach.</summary>
    public IReadOnlyList<DirectReportDto> DirectReports { get; init; } = [];

    /// <summary>
    /// The three time-tracking flags — Compass Super Admin only (AC-10).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TimeTrackingSettingsDto? TimeTrackingSettings { get; init; }
}

/// <summary>One EDJEr who lists this record's subject as their coach.</summary>
public sealed class DirectReportDto
{
    /// <summary>The direct report's identifier — the drill-in into their own record.</summary>
    public int Id { get; init; }

    /// <summary>Given name.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Family name.</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// Whether this direct report is active — elevated tiers only, the same gating as
    /// <see cref="EmployeeDetailDto.IsActive"/>. A baseline viewer's reports are all-active (BR-1).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsActive { get; init; }
}

/// <summary>One assignment in an EDJEr's history (AC-8, AC-20).</summary>
public sealed class EmployeeAssignmentDto
{
    /// <summary>
    /// The assignment's own identifier — links a history row into its detail screen
    /// (<c>/compass/team-directory/{employeeId}/assignments/{assignmentId}</c>), per AC-2.
    /// </summary>
    /// <remarks>
    /// Equivalent to <see cref="ClientId"/> for identification purposes in the common case, since an
    /// EDJEr is rarely assigned to the same client twice.
    /// </remarks>
    public int AssignmentId { get; init; }

    /// <summary>
    /// "Active" or "Inactive" — whether this assignment is current as of the business date
    /// (BR-7).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The client's identifier — AC-8's link into the client view.</summary>
    public int ClientId { get; init; }

    /// <summary>The client's name.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>
    /// Whether this row's client is an internal EDJE ("beach") client.
    /// </summary>
    public bool IsInternal { get; init; }

    /// <summary>When the assignment began.</summary>
    public DateOnly StartDate { get; init; }

    /// <summary>When it ended, or <c>null</c> while open-ended.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>
    /// The assignment note — elevated tiers only (AC-11).
    /// </summary>
    /// <remarks>
    /// Visible to a baseline viewer on their own record, matching the SOWs beside it — both are granted
    /// together under AC-11.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }

    /// <summary>SOWs — elevated tiers, or a baseline viewer on their own record (AC-11).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<EmployeeSowDto>? Sows { get; init; }

    /// <summary>Whether the viewer may open this assignment's own detail screen — elevated tiers only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CanViewAssignment { get; init; }
}

/// <summary>A contract on an assignment (AC-11).</summary>
public sealed class EmployeeSowDto
{
    /// <summary>Initial contract, extension, or legacy migrated.</summary>
    public string SowType { get; init; } = string.Empty;

    /// <summary>When the contract begins.</summary>
    public DateOnly StartDate { get; init; }

    /// <summary>When it ends.</summary>
    public DateOnly EndDate { get; init; }

    /// <summary>Rate-increase indicator — elevated tiers only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RateIncrease { get; init; }

    /// <summary>The SOW note — elevated tiers only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }
}

/// <summary>The three time-tracking flags (AC-10, Super Admin only).</summary>
public sealed class TimeTrackingSettingsDto
{
    /// <summary>Whether the EDJEr must submit timesheets.</summary>
    public bool TimesheetRequired { get; init; }

    /// <summary>Whether they may submit under 40 hours.</summary>
    public bool CanSubmitUnder40 { get; init; }

    /// <summary>Whether they are included in payroll.</summary>
    public bool IncludeInPayroll { get; init; }
}
