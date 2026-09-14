namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// One client as the configuration API returns it — AC-21's field set, plus its billable categories.
/// </summary>
/// <remarks>
/// Status travels outward only: it is derived from the client's assignments (FR-021, FR-035), never
/// stored and never settable, so <see cref="CompassClientRequest"/> has no such member and a request
/// supplying one is refused. <c>CompassAdminClientEndpointsTests</c> pins the stored-status
/// vocabulary out of this type by reflection. The billable categories travel with the client because
/// AC-NFR-3 puts them inside its audited scope; they are addressed individually only for writes.
/// </remarks>
/// <param name="Id">The client's identity key.</param>
/// <param name="ClientName">The unique client name.</param>
/// <param name="MsaSignedDate">Master Service Agreement signed date. Optional.</param>
/// <param name="NdaSignedDate">Non-Disclosure Agreement signed date. Optional.</param>
/// <param name="IsInternal">
/// The internal-EDJE ("beach") indicator — an assignment to a client with this set is what the
/// dashboard's "on the beach" tile counts.
/// </param>
/// <param name="InvoiceFrequencyTypeId">
/// The client-level invoicing cadence default. Optional, and only an ACTIVE type may be selected
/// (FR-023).
/// </param>
/// <param name="BillableTimeCategories">
/// Every category this client offers, active and inactive. Inactive ones are included because an
/// administrator who cannot see a retired category cannot reactivate one.
/// </param>
/// <param name="Status">
/// "Active", "Inactive" or "Former", computed for this response from the client's assignments
/// (BR-11) — never stored, never settable, and never a filter.
/// <para>
/// A string, not the <c>ClientStatus</c> enum, and that is fenced rather than stylistic.
/// <c>ClientStatusNonGatingTests.NoProductionTypeAnywhere_HoldsADerivedStatus</c> fails the build if any
/// production member HOLDS the enum, because a held value can outlive the assignments it summarises
/// (FR-035). The read surface's <c>ClientDirectoryRowDto.Status</c> is a string for the same reason, so
/// both Compass surfaces publish the same wire format. The enum lives inside the derivation; everything
/// outward of it speaks the published word.
/// </para>
/// </param>
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
