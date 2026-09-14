namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// One row of `GET /pickers/edjers` (contract §3). Active EDJErs only (FR-003).
/// </summary>
public sealed class EdjerPickerRowDto
{
    /// <summary>The EDJEr's identifier.</summary>
    public int Id { get; init; }

    /// <summary>The EDJEr's display name.</summary>
    public string DisplayName { get; init; } = string.Empty;
}
