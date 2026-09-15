namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>One client named by a dashboard breakdown row, with the id its link needs.</summary>
public sealed class DashboardBreakdownClientDto
{
    /// <summary>The client's identifier, so the row can link to the read-only client view.</summary>
    public int Id { get; init; }

    /// <summary>
    /// The client's name.
    /// </summary>
    /// <remarks>
    /// Left uninitialised deliberately, matching the sibling <see cref="SalesDashboardDto"/>, since both
    /// types are excluded from the per-file coverage gate.
    /// </remarks>
    public string Name { get; init; } = string.Empty;
}
