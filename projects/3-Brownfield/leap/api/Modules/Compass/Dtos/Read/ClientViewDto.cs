using System.Text.Json.Serialization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>The client view — AC-13, AC-14, AC-15, AC-16.</summary>
/// <remarks>
/// <see cref="ClientName"/> sits outside every optional section, and that IS AC-13. AC-14
/// hides the Client Details panel from four of the five tiers, and that panel is where a name would
/// naturally live — so nesting it there would leave those viewers on a nameless page.
/// </remarks>
public sealed class ClientViewDto
{
    /// <summary>The client's identifier.</summary>
    public int Id { get; init; }

    /// <summary>The client's name — served to every tier (AC-13).</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>
    /// The derived status, from the single shared derivation (AC-42, BR-11).
    /// </summary>
    /// <remarks>Must equal what the Client Directory shows for the same client (SC-005).</remarks>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Whether this is the internal EDJE client ("beach") — served to every tier, unlike the
    /// rest of <see cref="ClientDetails"/>.
    /// </summary>
    /// <remarks>
    /// An internal client has no contracts, so <see cref="ClientAssignmentHistoryDto.CanViewSow"/> is
    /// withheld for its assignments regardless of tier — the SOW column has nothing to link to. That
    /// column is a client-side conditional, not a server entitlement, so it needs this flag at every
    /// tier to decide whether to render at all, the same reason <see cref="ClientName"/> sits outside
    /// the Super-Admin-only panel (AC-13).
    /// </remarks>
    public bool IsInternal { get; init; }

    /// <summary>Every EDJEr ever assigned to this client, scoped to the viewer (AC-15).</summary>
    public IReadOnlyList<ClientAssignmentHistoryDto> AssignmentHistory { get; init; } = [];

    /// <summary>Client configuration — Compass Super Admin only (AC-14), absent otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClientDetailsDto? ClientDetails { get; init; }
}

/// <summary>One EDJEr's engagement with this client (AC-15).</summary>
public sealed class ClientAssignmentHistoryDto
{
    /// <summary>
    /// The assignment's own identifier — links a history row into its detail screen
    /// (<c>/compass/client-directory/{clientId}/assignments/{assignmentId}</c>), per AC-2.
    /// </summary>
    public int AssignmentId { get; init; }

    /// <summary>
    /// "Active" or "Inactive" — whether this assignment is current as of the business date
    /// (BR-7, AC-24).
    /// </summary>
    /// <remarks>
    /// Derived, never stored, and never derived here: it comes from
    /// <c>IClientStatusDerivation.IsCurrent(today)</c>, the same implementation client status uses,
    /// because BR-7 and BR-11 are the same predicate and <c>ClientStatusSingleDerivationTests</c> fails
    /// the build on a second one. Distinct from <see cref="EmployeeIsActive"/>: this is the
    /// assignment's currency, that is the EDJEr's stored flag, so a departed EDJEr can hold an
    /// assignment this reports Active — which is why AC-24's "and status" needs it. Always present,
    /// unlike the disclosures withheld beside it. A <c>string</c>, not the <c>ClientStatus</c> enum
    /// (<c>ClientStatusNonGatingTests</c>), and not <c>init</c>: the derivation cannot run in SQL.
    /// </remarks>
    public string Status { get; set; } = string.Empty;

    /// <summary>The EDJEr's identifier, linking back into their detail.</summary>
    public int EmployeeId { get; init; }

    /// <summary>The EDJEr's display name.</summary>
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>When the assignment began.</summary>
    public DateOnly StartDate { get; init; }

    /// <summary>When it ended, or <c>null</c> while open-ended.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Whether the EDJEr is active (AC-15) — absent for a baseline viewer, whose rows are all active.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EmployeeIsActive { get; init; }

    /// <summary>
    /// Whether the viewer may open this assignment's SOWs — elevated tiers only (AC-16).
    /// </summary>
    /// <remarks>
    /// An entitlement the server grants, not a client-side conditional: absent means not granted, so
    /// an affordance the server withheld cannot be rendered from this payload at all.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CanViewSow { get; init; }
}

/// <summary>Client configuration (AC-14, Super Admin only).</summary>
public sealed class ClientDetailsDto
{
    /// <summary>When the MSA was signed, if it has been.</summary>
    public DateOnly? MsaSignedDate { get; init; }

    /// <summary>When the NDA was signed, if it has been.</summary>
    public DateOnly? NdaSignedDate { get; init; }

    /// <summary>Whether this is the internal EDJE client ("beach").</summary>
    public bool IsInternal { get; init; }

    /// <summary>The client-level invoice-frequency default, if one is set.</summary>
    public string? InvoiceFrequency { get; init; }

    /// <summary>The client's billable time categories.</summary>
    public IReadOnlyList<string> BillableTimeCategories { get; init; } = [];
}
