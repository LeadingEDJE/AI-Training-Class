namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// A Leading EDJE client, including internal "beach" clients. ERD table <c>compass.client</c>.
/// </summary>
/// <remarks>
/// Carries no stored status. Active/Inactive is derived from the client's assignments, so there is
/// deliberately no <c>is_active</c> column here — a stored flag could disagree with the assignments
/// it summarises. Do not add one.
///
/// Not to be confused with the timesheet module's <c>Client</c> (<c>public.clients</c>). See the
/// remarks on <see cref="Employee"/> for why both exist and how to name them from outside.
/// </remarks>
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
    /// <remarks>
    /// Optional because an engagement may override it: where
    /// <see cref="ClientAssignment.InvoiceFrequencyTypeId"/> is set it wins over this default, and
    /// where neither is set the effective cadence is "none set" rather than an error or a substituted
    /// value (FR-037, FR-039).
    /// </remarks>
    public int? InvoiceFrequencyTypeId { get; set; }

    /// <summary>
    /// The identifier this record carried in the legacy TPS directory, or <c>null</c> when it was
    /// created in Compass.
    /// </summary>
    /// <remarks>
    /// Permanent provenance: it answers "what did this record come from?" after the migration tool
    /// and its crosswalk file are gone, and lets a lost crosswalk be rebuilt by reading it back from
    /// the target. Unique when present, and null for everything a person creates — Postgres unique
    /// indexes are NULLS DISTINCT, so that uniqueness costs a hand-created record nothing. Only a
    /// migration run may set it.
    /// </remarks>
    public string? LegacyTpsId { get; set; }

    /// <summary>The default invoicing cadence, if set.</summary>
    public InvoiceFrequencyType? InvoiceFrequencyType { get; set; }

    /// <summary>Billable time categories offered by this client, consumed by the timesheet system.</summary>
    public ICollection<BillableTimeCategory> BillableTimeCategories { get; set; } = [];

    /// <summary>EDJEr assignments at this client, over time.</summary>
    public ICollection<ClientAssignment> ClientAssignments { get; set; } = [];
}
