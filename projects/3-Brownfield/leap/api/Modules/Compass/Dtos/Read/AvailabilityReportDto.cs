namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// The Availability Report — three chronological sections plus the date they were computed against
/// (AC-38, FR-009).
/// </summary>
/// <remarks>
/// Three typed section lists, not one row shape: the sections do not share a column set — §1 has three
/// columns, §2 and §3 six each (FR-010 to FR-012, mockup <c>RPT-3</c>). A uniform row type, the shape
/// <see cref="DashboardBreakdownRowDto"/> uses for the dashboard, would give §1 a client property —
/// the fourth column no acceptance criterion covers (FR-029). Every section is ordered earliest date
/// first (FR-009): §1 on the internal-assignment start, §2 on the assignment end date, §3 on the SOW
/// end date, so the rows to act on first are on top (FR-006). Coach is nullable in all three and a
/// coachless EDJEr is never filtered out (FR-013): this report is where an EDJEr whose coach was never
/// notified because they have none stays visible (J19), so a dropped row loses that control.
/// </remarks>
public sealed class AvailabilityReportDto
{
    /// <summary>
    /// The business date all three sections were computed against — one date per request, so the
    /// sections can never disagree about who is available (FR-027's rule, applied to this screen).
    /// </summary>
    public DateOnly AsOfDate { get; init; }

    /// <summary>§1 — Currently Available EDJErs. Same population as the beach tile (FR-005, FR-010).</summary>
    public IReadOnlyList<AvailableEdjerRowDto> CurrentlyAvailable { get; init; } = [];

    /// <summary>§2 — Confirmed Rollouts. Same population as the rollout tile (FR-004, FR-011).</summary>
    public IReadOnlyList<ConfirmedRolloutRowDto> ConfirmedRollouts { get; init; } = [];

    /// <summary>§3 — Unconfirmed SOWs expiring within 90 days (FR-003, FR-012, BR-5).</summary>
    public IReadOnlyList<UnconfirmedSowRowDto> UnconfirmedSows { get; init; } = [];
}

/// <summary>One §1 row — an EDJEr currently on the beach (FR-010).</summary>
/// <remarks>
/// Four display columns, and deliberately no client property. The mockup's
/// <c>EDJE Client Assignment Start</c> is a single date column — the internal-assignment start — not a
/// client column plus a date; FR-029 once read it as four, which was a misread of that file, and no
/// fourth column may be built from it. <see cref="DaysAvailable"/> is a later, owner-requested
/// addition and is not the column FR-029 misread — it has no client property either. One row per
/// EDJEr, on their earliest current internal-assignment start, so this section's row count equals the
/// beach tile's distinct-EDJEr count (SC-003).
/// </remarks>
public sealed class AvailableEdjerRowDto
{
    /// <summary>The EDJEr's identifier. A key for the row, not a rendered column.</summary>
    public int EmployeeId { get; init; }

    /// <summary>The EDJEr's display name.</summary>
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>When they went on the beach — the earliest of their current internal-assignment starts.</summary>
    public DateOnly? InternalAssignmentStartDate { get; init; }

    /// <summary>
    /// Whole days from <see cref="InternalAssignmentStartDate"/> to the as-of date — how long this
    /// EDJEr has been on the beach. Its own column, immediately after the date it is calculated from.
    /// </summary>
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

    /// <summary>
    /// The client of the assignment carrying <see cref="AssignmentEndDate"/>. A list, because two
    /// assignments can tie on that latest date and naming only one would drop the other.
    /// </summary>
    public IReadOnlyList<DashboardBreakdownClientDto> Clients { get; init; } = [];

    /// <summary>
    /// The LATEST end date across their current assignments — when they actually become available
    /// (FR-011), not the earliest of several engagements.
    /// </summary>
    public DateOnly? AssignmentEndDate { get; init; }

    /// <summary>Whole days from the as-of date to <see cref="AssignmentEndDate"/>.</summary>
    public int? DaysUntilRollout { get; init; }

    /// <summary>The EDJEr's coach, or null when they have none (FR-013).</summary>
    public string? CoachName { get; init; }
}

/// <summary>One §3 row — a SOW expiring within 90 days with no follow-on (FR-012, BR-5).</summary>
/// <remarks>
/// One row per SOW, not per EDJEr: the unit of the expiring-SOW population is the SOW (FR-003), so an
/// EDJEr with two exposed SOWs appears twice, correctly.
/// </remarks>
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

    /// <summary>
    /// The SOW this row is about — row identity, not a rendered column (see
    /// <see cref="DashboardBreakdownRowDto.SowId"/>). Null only if the source row lacked one.
    /// </summary>
    public int? SowId { get; init; }

    /// <summary>The SOW's end date, or null if the source row had none.</summary>
    /// <remarks>
    /// Nullable, matching §1 and §2 — a missing date is NOT coalesced to today. This section is
    /// read for triage, so an invented "expires today / 0 days" is not a neutral placeholder: it is the
    /// most urgent value the screen can show, and it would sort to the top. Null renders as an empty
    /// cell, which keeps the row visible (FR-013's principle) without asserting something false.
    /// </remarks>
    public DateOnly? SowEndDate { get; init; }

    /// <summary>Whole days from the as-of date to <see cref="SowEndDate"/>, or null with it.</summary>
    /// <remarks>
    /// Nullable for <see cref="SowEndDate"/>'s reason, and it travels WITH it: coalescing the two
    /// independently once allowed a row to show a fabricated date beside a real day count, two values
    /// that cannot both be true of the same SOW.
    /// </remarks>
    public int? DaysUntilExpiration { get; init; }

    /// <summary>The EDJEr's coach, or null when they have none (FR-013).</summary>
    public string? CoachName { get; init; }
}
