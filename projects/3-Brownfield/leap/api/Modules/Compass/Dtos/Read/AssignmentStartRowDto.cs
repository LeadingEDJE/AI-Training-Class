namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// One row of the Assignment Start lookup (AC-40).
/// </summary>
public sealed class AssignmentStartRowDto
{
    /// <summary>The assigned EDJEr's display name.</summary>
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>
    /// The EDJEr's employee type (e.g. "Full Time", "Part Time", "1099"), its own column following the
    /// name. Nullable on the wire even though the FK backing it is required in practice.
    /// </summary>
    public string? EmployeeType { get; init; }

    /// <summary>The client's name.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>The date the assignment started, inside the requested range at both ends.</summary>
    public DateOnly StartDate { get; init; }
}
