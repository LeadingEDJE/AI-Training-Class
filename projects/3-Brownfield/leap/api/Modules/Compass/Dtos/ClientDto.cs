namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// One client as the configuration API returns it — AC-21's field set, plus its billable categories.
/// </summary>
/// <remarks>
/// Status can be set directly on a client update — <see cref="CompassClientRequest"/> exposes it as an
/// optional field, and it is only recomputed automatically when that field is left blank. See the
/// fallback rule described in the client-status-migration guide. The billable categories travel with
/// the client because they render together on the same admin screen.
/// </remarks>
/// <param name="Id">The client's identity key.</param>
/// <param name="ClientName">The unique client name.</param>
/// <param name="MsaSignedDate">Master Service Agreement signed date. Optional.</param>
/// <param name="NdaSignedDate">Non-Disclosure Agreement signed date. Optional.</param>
/// <param name="IsInternal">The internal-EDJE ("beach") indicator.</param>
/// <param name="InvoiceFrequencyTypeId">
/// The client-level invoicing cadence default. Optional, and only an ACTIVE type may be selected
/// (FR-023).
/// </param>
/// <param name="BillableTimeCategories">Every category this client offers, active and inactive.</param>
/// <param name="Status">"Active", "Inactive" or "Former".</param>
public sealed record ClientDto(
    int Id,
    string ClientName,
    DateOnly? MsaSignedDate,
    DateOnly? NdaSignedDate,
    bool IsInternal,
    int? InvoiceFrequencyTypeId,
    IReadOnlyList<BillableTimeCategoryDto> BillableTimeCategories,
    string Status
);
