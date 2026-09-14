namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>One client named by a dashboard breakdown row, with the id its link needs.</summary>
/// <remarks>
/// A row carries a LIST of these rather than a single id/name pair because two of the four
/// categories are keyed to one row per EDJEr, and an EDJEr can hold more than one assignment tied on
/// the date that keys it — two internal clients started the same day, or two engagements ending on
/// the same day. Naming only one of them would silently drop the other from a sales screen; emitting
/// a row per tied assignment would make the breakdown disagree with the tile above it, which counts
/// distinct EDJErs (SC-003).
/// </remarks>
public sealed class DashboardBreakdownClientDto
{
    /// <summary>The client's identifier, so the row can link to the read-only client view.</summary>
    public int Id { get; init; }

    /// <summary>
    /// The client's name.
    /// </summary>
    /// <remarks>
    /// Initialised rather than left bare: an auto-property with no initializer emits no
    /// field-initializer IL, and a DTO composed only of those gets no Cobertura entry at all — which
    /// the per-file coverage gate reports as "No test coverage found" and no test can fix. The
    /// sibling <see cref="SalesDashboardDto"/> is excluded for exactly that reason; this type stays
    /// measurable instead.
    /// </remarks>
    public string Name { get; init; } = string.Empty;
}
