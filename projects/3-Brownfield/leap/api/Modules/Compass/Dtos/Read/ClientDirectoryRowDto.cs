namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>One row of the Client Directory (AC-12).</summary>
/// <remarks>
/// Three fields, and the omissions are the design: AC-14 hides MSA/NDA dates, the internal flag and
/// the invoice default from four of the five tiers on the client view, so surfacing them in a
/// listing open to every authenticated viewer would defeat that. Unlike the employee surfaces this
/// shape does not vary by tier.
/// </remarks>
public sealed class ClientDirectoryRowDto
{
    /// <summary>The client's identifier, carrying AC-12's link into the client view.</summary>
    public int Id { get; init; }

    /// <summary>The client's name — the column AC-12's search matches against.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>
    /// The derived status: exactly <c>Active</c> or <c>Inactive</c> (AC-42, BR-11).
    /// </summary>
    /// <remarks>
    /// Binary, total and never null — a client with no assignments is <c>Inactive</c>, not blank.
    /// That totality is what lets the column sort and filter (FR-030). Computed by the single shared
    /// derivation, never stored.
    /// </remarks>
    public string Status { get; init; } = string.Empty;
}
