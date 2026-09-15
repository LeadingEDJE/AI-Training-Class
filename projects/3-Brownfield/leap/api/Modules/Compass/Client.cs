namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// A Leading EDJE client, including internal "beach" clients. ERD table <c>compass.client</c>.
/// </summary>
public class Client
{
    /// <summary>Primary key. Mapped to the ERD column <c>client_id</c>.</summary>
    public int Id { get; set; }

    /// <summary>Required, unique client name.</summary>
    public string ClientName { get; set; } = string.Empty;

    /// <summary>Optional Master Service Agreement signed date.</summary>
    public DateOnly? MsaSignedDate { get; set; }

    /// <summary>Optional Non-Disclosure Agreement signed date.</summary>
    public DateOnly? NdaSignedDate { get; set; }

    /// <summary>
    /// Required. Flags internal EDJE clients used for beach assignments — the dashboard's "on the
    /// beach" tile is an assignment to a client with this set.
    /// </summary>
    public bool IsInternal { get; set; }

    /// <summary>
    /// Optional default invoicing cadence.
    /// </summary>
    public int? InvoiceFrequencyTypeId { get; set; }

    /// <summary>
    /// The identifier this record carried in the legacy TPS directory, or <c>null</c> when it was
    /// created in Compass.
    /// </summary>
    /// <remarks>
    /// Temporary import marker only: it is cleared automatically once the nightly reconciliation
    /// job finishes reconciling the two directories (LEAP-1180), so callers should not depend on
    /// it staying populated. Safe to overwrite by hand if a value looks stale.
    /// </remarks>
    public string? LegacyTpsId { get; set; }

    /// <summary>The default invoicing cadence, if set.</summary>
    public InvoiceFrequencyType? InvoiceFrequencyType { get; set; }

    /// <summary>Billable time categories offered by this client, consumed by the timesheet system.</summary>
    public ICollection<BillableTimeCategory> BillableTimeCategories { get; set; } = [];

    /// <summary>EDJEr assignments at this client, over time.</summary>
    public ICollection<ClientAssignment> ClientAssignments { get; set; } = [];
}
