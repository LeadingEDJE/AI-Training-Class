namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>One row of `GET /pickers/clients`.</summary>
public sealed class ClientPickerRowDto
{
    /// <summary>The client's identifier.</summary>
    public int Id { get; init; }

    /// <summary>The client's name.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>
    /// The derived status, used to filter and disable inactive clients from the picker before they
    /// reach the dropdown.
    /// </summary>
    public string DerivedStatus { get; init; } = string.Empty;
}
