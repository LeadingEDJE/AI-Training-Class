namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// One client as the administration list renders them.
/// </summary>
/// <param name="Id">The client's identity key.</param>
/// <param name="ClientName">The unique client name.</param>
/// <param name="IsInternal">The internal-EDJE ("beach") indicator.</param>
/// <param name="InvoiceFrequencyTypeName">
/// The client-level invoicing cadence default's display name, or null when the client has no default.
/// Null is an ordinary state, not an incomplete one (AC-22 makes the default optional).
/// </param>
/// <param name="Status">"Active" or "Inactive".</param>
public sealed record ClientSummaryDto(
    int Id,
    string ClientName,
    bool IsInternal,
    string? InvoiceFrequencyTypeName,
    string Status
);
