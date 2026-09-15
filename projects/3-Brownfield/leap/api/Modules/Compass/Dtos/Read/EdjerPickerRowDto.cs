namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>One row of `GET /pickers/edjers`.</summary>
public sealed class EdjerPickerRowDto
{
    /// <summary>The EDJEr's identifier.</summary>
    public int Id { get; init; }

    /// <summary>The EDJEr's display name.</summary>
    public string DisplayName { get; init; } = string.Empty;
}
