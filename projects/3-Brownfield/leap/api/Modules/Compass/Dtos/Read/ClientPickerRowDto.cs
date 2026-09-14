namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// One row of `GET /pickers/clients`. Must include every client, one with zero assignments included —
/// the O6 named regression test (FR-009–FR-012, BR-11).
/// </summary>
public sealed class ClientPickerRowDto
{
    /// <summary>The client's identifier.</summary>
    public int Id { get; init; }

    /// <summary>The client's name.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>
    /// The derived status, context only — never used to filter, disable or reject a selection: a
    /// brand-new client with zero assignments derives Inactive and must still be selectable (FR-012).
    /// </summary>
    public string DerivedStatus { get; init; } = string.Empty;
}
