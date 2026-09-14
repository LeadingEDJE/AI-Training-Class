namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// One client as the administration list renders them.
/// </summary>
/// <remarks>
/// Carries the cadence's name rather than its id, because the list renders it and would otherwise
/// need a second request per row; <see cref="ClientDto"/> carries the id because the form populates
/// its select from the active-only lookup list. Status is derived per request, never stored (see
/// <see cref="ClientDto"/>), and derived set-wise in one query for the whole collection: per-row
/// derivation is the N+1 that AC-NFR-4's p95 target forbids.
/// </remarks>
/// <param name="Id">The client's identity key.</param>
/// <param name="ClientName">The unique client name.</param>
/// <param name="IsInternal">The internal-EDJE ("beach") indicator.</param>
/// <param name="InvoiceFrequencyTypeName">
/// The client-level invoicing cadence default's display name, or null when the client has no default.
/// Null is an ordinary state, not an incomplete one (AC-22 makes the default optional).
/// </param>
/// <param name="Status">
/// "Active" or "Inactive", computed from this client's assignments (BR-11). A string rather than the
/// enum — see <see cref="ClientDto"/>, and the fence that requires it.
/// </param>
public sealed record ClientSummaryDto(
    int Id,
    string ClientName,
    bool IsInternal,
    string? InvoiceFrequencyTypeName,
    string Status
);
