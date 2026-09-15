namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>One row of the Client Assignment Duration report (FR-014, AC-39).</summary>
/// <remarks>
/// The grain is the individual assignment record, not the EDJEr-client pair. An EDJEr who left a
/// client and returned appears as two separate rows.
/// </remarks>
public sealed class AssignmentDurationRowDto
{
    /// <summary>The EDJEr's identifier.</summary>
    public int EmployeeId { get; init; }

    /// <summary>The EDJEr's display name.</summary>
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>
    /// The EDJEr's employee type (e.g. "Full Time", "Part Time", "1099"), its own column following the
    /// name. Nullable on the wire even though the FK backing it is required in practice.
    /// </summary>
    public string? EmployeeType { get; init; }

    /// <summary>The client's identifier.</summary>
    public int ClientId { get; init; }

    /// <summary>The client's name.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>The EDJEr's coach, or null when they have none.</summary>
    public string? CoachName { get; init; }

    /// <summary>
    /// Total tenure in whole days, summed across every started assignment for this pair.
    /// </summary>
    public int TotalDays { get; init; }

    /// <summary>The mockup's rendering of <see cref="TotalDays"/>, e.g. "3 yrs 4 mos (1,238 days)".</summary>
    public string DurationDisplay { get; init; } = string.Empty;
}
