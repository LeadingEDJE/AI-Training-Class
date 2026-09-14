namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// One drill-down row, in the ONE shape shared across all four dashboard categories (AC-37).
/// </summary>
/// <remarks>
/// <see cref="Date"/> and <see cref="DaysUntil"/> are labelled per category by the frontend — a SOW
/// end date for the expiring-SOWs breakdown, an assignment end date for confirmed rollouts, an
/// internal-assignment start date for the beach breakdown. <see cref="Clients"/> may be empty and
/// <see cref="CoachName"/> and its companion <see cref="CoachId"/> are nullable together: an EDJEr
/// with no coach appears with an empty coach field rather than being filtered out (FR-008) — the
/// dashboard's compensating control for the coach notification Stream 3 skips.
/// </remarks>
public sealed class DashboardBreakdownRowDto
{
    /// <summary>The EDJEr's identifier.</summary>
    public int EmployeeId { get; init; }

    /// <summary>The EDJEr's display name.</summary>
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>
    /// The EDJEr's employee type (e.g. "Full Time", "Part Time", "1099"). Populated for all four
    /// categories; the frontend simply has no column for it on the <c>beach</c> grid.
    /// </summary>
    /// <remarks>
    /// Two consumers read this: the Sales Dashboard renders it on the active-sows, expiring-SOWs and
    /// confirmed-rollouts grids, and the Availability Report in its Confirmed Rollouts and Unconfirmed
    /// SOWs sections. It is non-nullable-empty rather than nullable, matching
    /// <c>TeamDirectoryRowDto.EmployeeType</c> and <c>EmployeeDetailDto.EmployeeType</c> —
    /// <c>Employee.EmployeeTypeId</c> is a required FK, so an absent type is not a reachable state.
    /// </remarks>
    public string EmployeeType { get; init; } = string.Empty;

    /// <summary>
    /// The clients this row is about, each with the id its link to the read-only client view
    /// (<c>/compass/client-directory/{id}</c>) needs. Empty when the category has none to show.
    /// </summary>
    /// <remarks>
    /// A list, not a single pair, because a row is per EDJEr in two of the four categories. Confirmed
    /// rollouts is keyed to an EDJEr's latest assignment end date and beach to their earliest
    /// internal-assignment start date, and either can tie across two assignments. The tile counts
    /// distinct EDJErs (FR-004, FR-005), so one row per tied assignment would make the breakdown's row
    /// count disagree with the tile above it, which SC-003 requires to match; naming only one of the
    /// tied clients would silently drop the other from a sales screen. The other two categories are
    /// per-assignment and per-SOW and always carry exactly one entry (AC-37).
    /// </remarks>
    public IReadOnlyList<DashboardBreakdownClientDto> Clients { get; init; } = [];

    /// <summary>
    /// The category-specific date — the MAX SOW end date across an active assignment's SOWs
    /// (<c>null</c> if it has none), an expiring SOW's own end date, an assignment end, or an
    /// internal-assignment start.
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>Whole days from the "as of" date to <see cref="Date"/>; null where not applicable.</summary>
    public int? DaysUntil { get; init; }

    /// <summary>
    /// The <c>beach</c> category's counterpart to <see cref="DaysUntil"/> — whole days from
    /// <see cref="Date"/> to the "as of" date; null for the other three, whose date lies ahead.
    /// </summary>
    public int? DaysAvailable { get; init; }

    /// <summary>The EDJEr's coach, or null when they have none (FR-008).</summary>
    public string? CoachName { get; init; }

    /// <summary>
    /// The coach's own id — the drill-in into their record at <c>/compass/team-directory/{id}</c>.
    /// Null exactly when <see cref="CoachName"/> is.
    /// </summary>
    /// <remarks>
    /// Resolved through the <c>Employee.Coach</c> navigation rather than the <c>CoachEmployeeId</c>
    /// FK, so the two cannot disagree: a consumer renders the link only when it has BOTH, and an id
    /// present beside an absent name would silently render a link with no text.
    /// </remarks>
    public int? CoachId { get; init; }

    /// <summary>
    /// The SOW this row is about, for the <c>expiring-sows</c> category only; null for the other three
    /// (issue #633 made <c>active-sows</c> per-ASSIGNMENT, which can own several SOWs at once, so it no
    /// longer has a single SOW to name here).
    /// </summary>
    /// <remarks>
    /// Row IDENTITY, not a rendered column. <c>expiring-sows</c> is one row per SOW, so without this a
    /// consumer has nothing stable to key on: <see cref="EmployeeId"/> repeats when an EDJEr holds two
    /// exposed SOWs, and <see cref="Date"/> plus a client still collides for two SOWs on the same client
    /// ending the same day. The other three categories are per-assignment or per-EDJEr and carry null.
    /// </remarks>
    public int? SowId { get; init; }

    /// <summary>
    /// The period's start date, for the <c>active-sows</c> category only; null for the other three. It
    /// is the client ASSIGNMENT'S start date (issue #633 — was the SOW's own), shown alongside
    /// <see cref="Date"/>'s max-SOW-end date.
    /// </summary>
    public DateOnly? StartDate { get; init; }
}
