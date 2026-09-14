namespace LeadingEDJE.Leap.Api.Modules.Compass.Contracts;

/// <summary>
/// A published invoice frequency type — read family 7 of the Compass Directory boundary (FR-010).
/// </summary>
/// <remarks>
/// No <c>IsActive</c>: the boundary returns only active invoice frequency types
/// (<see cref="IDirectory.GetInvoiceFrequenciesAsync"/> filters at the query), so every row is active
/// by construction. Publishing a column that is always <c>true</c> would invite a consumer to filter
/// on it and be surprised when the filter starts doing something.
///
/// Deliberate asymmetry with <c>CompassBillableCategoryDto</c>, which returns every row with its own
/// flag; both are requirement-driven, so do not harmonise them. This is also not the admin surface's
/// <c>InvoiceFrequencyTypeDto</c> — ADR-008 keeps those two contracts distinct. Do not merge them.
/// </remarks>
public sealed class CompassInvoiceFrequencyDto
{
    /// <summary>The invoice frequency type's identifier — <c>compass.invoice_frequency_type.invoice_frequency_type_id</c>.</summary>
    public int Id { get; init; }

    /// <summary>The frequency's name, e.g. "Monthly".</summary>
    public string TypeName { get; init; } = string.Empty;
}
