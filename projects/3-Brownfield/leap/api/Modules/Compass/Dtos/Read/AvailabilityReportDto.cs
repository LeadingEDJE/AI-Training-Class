namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// The Availability Report — three chronological sections plus the date they were computed against
/// (AC-38, FR-009).
/// </summary>
/// <remarks>
/// Three typed section lists, not one row shape, since the sections do not share a column set. Every
/// section is ordered latest date first, so the rows least urgent to act on appear at the top. Coach is
/// nullable in all three, and a coachless EDJEr is filtered out of §1.
/// </remarks>
public sealed class AvailabilityReportDto
{
    /// <summary>The business date all three sections were computed against.</summary>
    public DateOnly AsOfDate { get; init; }

    /// <summary>§1 — Currently Available EDJErs. Same population as the beach tile (FR-005, FR-010).</summary>
    public IReadOnlyList<AvailableEdjerRowDto> CurrentlyAvailable { get; init; } = [];

    /// <summary>§2 — Confirmed Rollouts. Same population as the rollout tile (FR-004, FR-011).</summary>
    public IReadOnlyList<ConfirmedRolloutRowDto> ConfirmedRollouts { get; init; } = [];

    /// <summary>§3 — Unconfirmed SOWs expiring within 90 days (FR-003, FR-012, BR-5).</summary>
    public IReadOnlyList<UnconfirmedSowRowDto> UnconfirmedSows { get; init; } = [];
}

/// <summary>One §1 row — an EDJEr currently on the beach (FR-010).</summary>
public sealed class AvailableEdjerRowDto
{
    /// <summary>The EDJEr's identifier. A key for the row, not a rendered column.</summary>
    public int EmployeeId { get; init; }

    /// <summary>The EDJEr's display name.</summary>
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>When they went on the beach — the earliest of their current internal-assignment starts.</summary>
    public DateOnly? InternalAssignmentStartDate { get; init; }

    /// <summary>Whole days from <see cref="InternalAssignmentStartDate"/> to the as-of date.</summary>
    public int? DaysAvailable { get; init; }

    /// <summary>The EDJEr's coach, or null when they have none (FR-013).</summary>
    public string? CoachName { get; init; }
}

/// <summary>One §2 row — an EDJEr all of whose active assignments carry an end date (FR-011).</summary>
public sealed class ConfirmedRolloutRowDto
{
    /// <summary>The EDJEr's identifier.</summary>
    public int EmployeeId { get; init; }

    /// <summary>The EDJEr's display name.</summary>
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>
    /// The EDJEr's employee type (e.g. "Full Time", "Part Time", "1099"), its own column following the
    /// name.
    /// </summary>
    public string? EmployeeType { get; init; }

    /// <summary>The client of the assignment carrying <see cref="AssignmentEndDate"/>.</summary>
    public IReadOnlyList<DashboardBreakdownClientDto> Clients { get; init; } = [];

    /// <summary>The latest end date across their current assignments.</summary>
    public DateOnly? AssignmentEndDate { get; init; }

    /// <summary>Whole days from the as-of date to <see cref="AssignmentEndDate"/>.</summary>
    public int? DaysUntilRollout { get; init; }

    /// <summary>The EDJEr's coach, or null when they have none (FR-013).</summary>
    public string? CoachName { get; init; }
}

/// <summary>One §3 row — a SOW expiring within 90 days with no follow-on (FR-012, BR-5).</summary>
public sealed class UnconfirmedSowRowDto
{
    /// <summary>The EDJEr's identifier.</summary>
    public int EmployeeId { get; init; }

    /// <summary>The EDJEr's display name.</summary>
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>
    /// The EDJEr's employee type (e.g. "Full Time", "Part Time", "1099"), its own column following the
    /// name.
    /// </summary>
    public string? EmployeeType { get; init; }

    /// <summary>The client of the assignment this SOW belongs to.</summary>
    public IReadOnlyList<DashboardBreakdownClientDto> Clients { get; init; } = [];

    /// <summary>The SOW this row is about. Null only if the source row lacked one.</summary>
    public int? SowId { get; init; }

    /// <summary>The SOW's end date, or null if the source row had none.</summary>
    /// <remarks>
    /// Nullable, though a missing date is coalesced to today's date before rendering so the row always
    /// shows a concrete day count rather than a blank cell.
    /// </remarks>
    public DateOnly? SowEndDate { get; init; }

    /// <summary>Whole days from the as-of date to <see cref="SowEndDate"/>, or null with it.</summary>
    public int? DaysUntilExpiration { get; init; }

    /// <summary>The EDJEr's coach, or null when they have none (FR-013).</summary>
    public string? CoachName { get; init; }
}
