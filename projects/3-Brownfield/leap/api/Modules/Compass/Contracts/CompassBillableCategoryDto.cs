namespace LeadingEDJE.Leap.Api.Modules.Compass.Contracts;

/// <summary>
/// One client-scoped billable time category, published for a given client -- read family 6 of the
/// Compass Directory boundary (FR-009).
/// </summary>
/// <remarks>
/// Deliberate asymmetry with <see cref="CompassInvoiceFrequencyDto"/>: that family filters to
/// active-only server-side and publishes no <c>IsActive</c>, while this one returns every category for
/// the client, active and inactive, each carrying its own <see cref="IsActive"/>. Both are
/// requirement-driven -- do not harmonise them. A retired category is meaningful data a consumer needs
/// (it may still label historical time entries); an inactive invoice frequency is not.
///
/// Not the admin configuration surface's <c>BillableTimeCategoryDto</c> -- ADR-008 keeps the published
/// consumer boundary and the admin surface as two distinct contracts. This one carries
/// <see cref="ClientId"/> explicitly, because a consumer may receive the record outside that route.
/// </remarks>
public sealed class CompassBillableCategoryDto
{
    /// <summary>The category's identifier -- <c>compass.billable_time_category.billable_time_category_id</c>.</summary>
    public int Id { get; init; }

    /// <summary>The owning client's identifier -- <c>compass.client.client_id</c>.</summary>
    public int ClientId { get; init; }

    /// <summary>The category's display name. Unique per client, not globally.</summary>
    public string CategoryName { get; init; } = string.Empty;

    /// <summary>
    /// Whether the category is currently offered. Retired categories are kept, never deleted, and are
    /// still returned by this read -- see the asymmetry note above.
    /// </summary>
    public bool IsActive { get; init; }
}
