namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// One drill-down row, in the ONE shape shared across all four dashboard categories (AC-37).
/// </summary>
public sealed class DashboardBreakdownRowDto
{
    /// <summary>The EDJEr's identifier.</summary>
    public int EmployeeId { get; init; }

    /// <summary>The EDJEr's display name.</summary>
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>The EDJEr's employee type (e.g. "Full Time", "Part Time", "1099").</summary>
    public string EmployeeType { get; init; } = string.Empty;

    /// <summary>
    /// The clients this row is about, each with the id its link to the read-only client view
    /// (<c>/compass/client-directory/{id}</c>) needs. Empty when the category has none to show.
    /// </summary>
    /// <remarks>
    /// Always carries exactly one entry across all four categories; the list wrapper exists only for
    /// forward-compatibility with a planned multi-client assignment feature that has not shipped yet.
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

    /// <summary>The coach's own id.</summary>
    public int? CoachId { get; init; }

    /// <summary>The SOW this row is about, for the <c>expiring-sows</c> category only; null for the other three.</summary>
    public int? SowId { get; init; }

    /// <summary>The period's start date, for the <c>active-sows</c> category only; null for the other three.</summary>
    public DateOnly? StartDate { get; init; }
}
