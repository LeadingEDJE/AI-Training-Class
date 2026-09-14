namespace LeadingEDJE.Leap.Api.Modules.Compass.Contracts;

/// <summary>
/// A published client, with a status derived at read time — read family 2 of the Compass Directory
/// boundary (FR-005).
/// </summary>
/// <remarks>
/// <c>compass.client</c> has no <c>IsActive</c> column, by requirement: status is derived at read
/// time through the single shared <see cref="Interfaces.IClientStatusDerivation"/>, which this
/// boundary calls rather than forking or caching. Zero assignments derives "Inactive" and is still
/// fully returnable — status restricts nothing (FR-034). A string, not the <c>ClientStatus</c> enum:
/// <c>ClientStatusNonGatingTests.NoProductionTypeAnywhere_HoldsADerivedStatus</c> fails the build on
/// any production member holding one, and <c>ClientStatusCrossSurfaceAgreementTests</c> pins every
/// surface to the same wire format. Do not reuse the admin surface's <c>ClientDto</c> (ADR-008): it
/// publishes a raw FK, nests categories, and binds this contract to admin changes (FR-024).
/// </remarks>
public sealed class CompassClientDto
{
    /// <summary>The client's identifier — <c>compass.client.client_id</c>.</summary>
    public int Id { get; init; }

    /// <summary>The unique client name.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>Master Service Agreement signed date. Optional.</summary>
    public DateOnly? MsaSignedDate { get; init; }

    /// <summary>Non-Disclosure Agreement signed date. Optional.</summary>
    public DateOnly? NdaSignedDate { get; init; }

    /// <summary>
    /// The internal-EDJE ("beach") indicator — an assignment to a client with this set is what the
    /// dashboard's "on the beach" tile counts.
    /// </summary>
    public bool IsInternal { get; init; }

    /// <summary>
    /// The client's default invoicing cadence name, resolved through <c>InvoiceFrequencyTypeId</c>.
    /// </summary>
    /// <remarks>
    /// Nullable, and "none set" is a real, reportable state rather than withheld data — do not invent
    /// a house default here.
    /// </remarks>
    public string? InvoiceFrequency { get; init; }

    /// <summary>
    /// Derived per response from the client's assignments (BR-11); never stored, settable or null.
    /// A closed set of three: "Former" once every assignment has ended, "Inactive" if none ever did.
    /// </summary>
    public string Status { get; init; } = string.Empty;
}
