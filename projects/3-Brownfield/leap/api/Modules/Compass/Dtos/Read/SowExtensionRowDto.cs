namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// One row of the SOW Extension Report.
/// </summary>
public sealed class SowExtensionRowDto
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

    /// <summary>
    /// The start date of the extension <see cref="Sow"/> period, inside the requested range at both
    /// ends.
    /// </summary>
    public DateOnly ExtensionStartDate { get; init; }
}
