using System.Text.Json.Serialization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// Adds or updates a client. Captures exactly AC-21 and AC-22's field set.
/// </summary>
/// <remarks>
/// <see cref="JsonUnmappedMemberHandling.Disallow"/> is load-bearing. FR-021 requires that the client
/// record never capture an active/inactive flag and AC-42 that no client-deactivation action exist,
/// but System.Text.Json's default is to discard unrecognised members, so a caller could POST
/// <c>"isActive": false</c> and receive <c>201 Created</c>. The attribute refuses the request with a
/// 400 instead, at the cost of rejecting every unknown member rather than only status-shaped ones.
/// One request type serves POST and PUT (AC-21, AC-23); categories are not part of it and are written
/// individually (<see cref="CreateBillableTimeCategoryRequest"/>).
/// </remarks>
/// <param name="ClientName">Required, and unique across clients.</param>
/// <param name="MsaSignedDate">Master Service Agreement signed date. Optional.</param>
/// <param name="NdaSignedDate">Non-Disclosure Agreement signed date. Optional.</param>
/// <param name="IsInternal">The internal-EDJE ("beach") indicator.</param>
/// <param name="InvoiceFrequencyTypeId">
/// The invoicing cadence default. Optional, and must name an active type — the server refuses an
/// inactive one even though the selection list omits it (FR-023, FR-041).
/// </param>
/// <param name="LegacyTpsId">
/// The identifier this record carried in the legacy TPS directory. Only the migration principal
/// may supply it (<see cref="CompassLegacyProvenance"/>); any other caller sending a value is
/// refused rather than having it quietly dropped, on create and on update alike. Absent for
/// everything a person creates. Immutable once set: an authorised caller may re-send it on an
/// update, but it is not re-applied.
/// </param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record CompassClientRequest(
    string ClientName,
    DateOnly? MsaSignedDate,
    DateOnly? NdaSignedDate,
    bool IsInternal,
    int? InvoiceFrequencyTypeId,
    string? LegacyTpsId = null
);

/// <summary>
/// Adds a billable time category to a client.
/// </summary>
/// <remarks>
/// Carries no active flag: a new category is created active. No acceptance criterion asks for adding one
/// that is immediately unofferable, and AC-23 describes retiring as an edit. Mirrors
/// <c>CreateCompassLookupRequest</c>, which made the same choice for the same reason.
/// </remarks>
/// <param name="CategoryName">The display name, unique among this client's categories (FR-025).</param>
/// <param name="LegacyTpsId">
/// The identifier this record carried in the legacy TPS directory. Only the migration principal
/// may supply it (<see cref="CompassLegacyProvenance"/>); any other caller sending a value is
/// refused rather than having it quietly dropped. Absent for everything a person creates.
/// </param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record CreateBillableTimeCategoryRequest(
    string CategoryName,
    string? LegacyTpsId = null
);

/// <summary>
/// Renames a billable time category and/or changes whether it is offered.
/// </summary>
/// <remarks>
/// One request covers both, because AC-23 describes a single edit operation covering rename and
/// deactivate — which is why there is no separate activate/deactivate route and no <c>DELETE</c>.
/// </remarks>
/// <param name="CategoryName">The display name, which must not collide with a sibling's.</param>
/// <param name="IsActive">Whether the category is offered.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record UpdateBillableTimeCategoryRequest(string CategoryName, bool IsActive);
