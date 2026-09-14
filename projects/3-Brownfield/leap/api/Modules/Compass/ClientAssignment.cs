namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// An EDJEr's engagement at a client. ERD table <c>compass.client_assignment</c>.
/// </summary>
/// <remarks>
/// The join that makes both directories, the dashboard and all three reports meaningful — and the
/// thing derived client status is computed from. An EDJEr may hold zero-to-many.
///
/// <see cref="EndDate"/> NULL means open-ended, and is what the directory's "current client
/// assigned" reads (<c>end_date IS NULL OR end_date &gt;= today</c>). A future end date is the
/// dashboard's "confirmed rollout". Both are queries over this table, not stored flags.
/// </remarks>
public class ClientAssignment
{
    /// <summary>Primary key. Mapped to the ERD column <c>client_assignment_id</c>.</summary>
    public int Id { get; set; }

    /// <summary>Required FK to the assigned <see cref="Employee"/>.</summary>
    public int EmployeeId { get; set; }

    /// <summary>Required FK to the <see cref="Client"/>.</summary>
    public int ClientId { get; set; }

    /// <summary>Required start date.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// Optional end date; NULL means open-ended. A database CHECK enforces
    /// <c>end_date &gt;= start_date</c> when present.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Optional free-text note. Elevated-visibility only, per the feature spec.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// This engagement's own invoicing cadence, overriding the client's default (FR-036, AC-28).
    /// </summary>
    /// <remarks>
    /// Nullable, and null is the ordinary case: most engagements bill the way their client does.
    /// Where this is set it wins over <see cref="Compass.Client.InvoiceFrequencyTypeId"/>; where
    /// neither is set the effective cadence is "none set" rather than an error or a substituted value
    /// (FR-037, FR-039).
    /// </remarks>
    public int? InvoiceFrequencyTypeId { get; set; }

    /// <summary>The overriding cadence, if one is set.</summary>
    public InvoiceFrequencyType? InvoiceFrequencyType { get; set; }

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

    /// <summary>The assigned EDJEr.</summary>
    public Employee? Employee { get; set; }

    /// <summary>The client.</summary>
    public Client? Client { get; set; }

    /// <summary>The contract periods under this assignment. Non-overlapping; gaps allowed.</summary>
    public ICollection<Sow> Sows { get; set; } = [];
}
