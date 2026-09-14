using System.Text.Json.Serialization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>The read-only employee detail (AC-8, AC-10, AC-11), projected for the viewer's tier.</summary>
/// <remarks>
/// Every nullable member here is a disclosure decision, not an optional field, and
/// <c>WhenWritingNull</c> makes a withheld one genuinely absent rather than present-and-null: a
/// client that can see the key can infer the field exists (FR-005). No edit affordance appears in
/// this shape for any tier (AC-8) — the Super Admin's edit path is Stream 2's.
/// </remarks>
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

    /// <summary>Whether this EDJEr is on the delivery team.</summary>
    /// <remarks>
    /// Top-level, and that is the disclosure decision: the same kind of ordinary directory attribute
    /// as <see cref="State"/>, so every tier that can open a record sees it (FR-004). Putting it in
    /// <see cref="TimeTrackingSettings"/> would withhold it from everyone below Compass Super Admin,
    /// and no exact-member pin on this type exists to catch that — the server-side baseline-viewer
    /// test is the guard.
    /// </remarks>
    public bool IsDeliveryTeam { get; init; }

    /// <summary>Whether the EDJEr is active — elevated tiers only; a baseline viewer can reach only active records.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsActive { get; init; }

    /// <summary>Every assignment held, current and ended (AC-8). Never truncated by retention (BR-16).</summary>
    public IReadOnlyList<EmployeeAssignmentDto> AssignmentHistory { get; init; } = [];

    /// <summary>
    /// Every EDJEr who lists this record's subject as their coach — the org-chart direction opposite
    /// <see cref="CoachId"/>. Empty rather than omitted: an absence of data, not a withholding.
    /// </summary>
    /// <remarks>
    /// Filtered by the same <c>EdjerVisibility</c> rule as every other listing (BR-1): a baseline
    /// viewer's direct reports are all-active, exactly like their Team Directory rows.
    /// </remarks>
    public IReadOnlyList<DirectReportDto> DirectReports { get; init; } = [];

    /// <summary>
    /// The three time-tracking flags — Compass Super Admin only (AC-10).
    /// </summary>
    /// <remarks>
    /// The row of the BR-1 matrix that "elevated means sees everything" intuition gets wrong: Admin,
    /// Ops and Sales are elevated for every other row and not for this one.
    /// </remarks>
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
    /// Not the client's: an EDJEr can be assigned to the same client more than once over time, so
    /// <see cref="ClientId"/> does not identify a row. This is also what the status lookup below
    /// joins on.
    /// </remarks>
    public int AssignmentId { get; init; }

    /// <summary>
    /// "Active" or "Inactive" — whether this assignment is current as of the business date
    /// (BR-7).
    /// </summary>
    /// <remarks>
    /// Derived, never stored, and never derived here: it comes from
    /// <c>IClientStatusDerivation.IsCurrent(today)</c>, the same single implementation client status
    /// uses, because BR-7 and BR-11 are the same predicate and <c>ClientStatusSingleDerivationTests</c>
    /// fails the build on a second one. A consumer renders this rather than comparing the dates itself.
    /// A <c>string</c>, not the <c>ClientStatus</c> enum (<c>ClientStatusNonGatingTests</c>): a held
    /// value could outlive the assignment it describes. Not <c>init</c>, because the derivation cannot
    /// run inside a SQL projection and is applied to the materialised rows afterwards.
    /// </remarks>
    public string Status { get; set; } = string.Empty;

    /// <summary>The client's identifier — AC-8's link into the client view.</summary>
    public int ClientId { get; init; }

    /// <summary>The client's name.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>
    /// Whether this row's client is an internal EDJE ("beach") client.
    /// </summary>
    /// <remarks>
    /// Internal work has no statements of work, so the row's "View SOWs" affordance leads nowhere.
    /// Per-row rather than per-record because an EDJEr's history mixes the two — a beach allocation
    /// sits beside real engagements — so the column stays and only the link goes. Not an entitlement,
    /// so not withheld: present for every tier, unlike <see cref="CanViewAssignment"/> beside it, which
    /// is a permission the server grants.
    /// </remarks>
    public bool IsInternal { get; init; }

    /// <summary>When the assignment began.</summary>
    public DateOnly StartDate { get; init; }

    /// <summary>When it ended, or <c>null</c> while open-ended.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>
    /// The assignment note — elevated tiers only (AC-11).
    /// </summary>
    /// <remarks>
    /// Withheld from a baseline viewer even on their OWN record: AC-11 grants a regular EDJEr their
    /// SOWs, not their notes. That asymmetry is easy to lose in the disclosing direction.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }

    /// <summary>SOWs — elevated tiers, or a baseline viewer on their own record (AC-11).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<EmployeeSowDto>? Sows { get; init; }

    /// <summary>
    /// Whether the viewer may open this assignment's own detail screen — elevated tiers only, mirroring
    /// <c>ClientAssignmentHistoryDto.CanViewSow</c> (AC-16/FR-025). Absent means not granted.
    /// </summary>
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

    /// <summary>Rate-increase indicator — elevated tiers only, even on the viewer's own record.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RateIncrease { get; init; }

    /// <summary>The SOW note — elevated tiers only, even on the viewer's own record.</summary>
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
