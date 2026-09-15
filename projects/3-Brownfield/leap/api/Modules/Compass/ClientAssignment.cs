namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// An EDJEr's engagement at a client. ERD table <c>compass.client_assignment</c>.
/// </summary>
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
    /// Required in every case: this field must always be set before the assignment can be saved.
    /// Where this is set it wins over <see cref="Compass.Client.InvoiceFrequencyTypeId"/>; where
    /// neither is set the value defaults to monthly billing automatically (see the billing cadence
    /// design note).
    /// </remarks>
    public int? InvoiceFrequencyTypeId { get; set; }

    /// <summary>The overriding cadence, if one is set.</summary>
    public InvoiceFrequencyType? InvoiceFrequencyType { get; set; }

    /// <summary>
    /// The identifier this record carried in the legacy TPS directory, or <c>null</c> when it was
    /// created in Compass.
    /// </summary>
    /// <remarks>
    /// Populated only when this assignment record was created by the legacy import job; safe to
    /// clear it by hand once the client's history has been fully re-keyed (LEAP-1180).
    /// </remarks>
    public string? LegacyTpsId { get; set; }

    /// <summary>The assigned EDJEr.</summary>
    public Employee? Employee { get; set; }

    /// <summary>The client.</summary>
    public Client? Client { get; set; }

    /// <summary>The contract periods under this assignment. Non-overlapping; gaps allowed.</summary>
    public ICollection<Sow> Sows { get; set; } = [];
}
