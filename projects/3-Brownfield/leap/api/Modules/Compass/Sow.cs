namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// A contract period (SOW) under a client assignment. ERD table <c>compass.sow</c>.
/// </summary>
public class Sow
{
    /// <summary>Primary key. Mapped to the ERD column <c>sow_id</c>.</summary>
    public int Id { get; set; }

    /// <summary>Required FK to the owning <see cref="ClientAssignment"/>.</summary>
    public int ClientAssignmentId { get; set; }

    /// <summary>
    /// Required. What this period represents. Persisted as the enum NAME and constrained to the three
    /// values by a database CHECK.
    /// </summary>
    /// <remarks>
    /// The initializer relies on the implicit <c>default</c> rather than being stated outright: a
    /// new SOW starts as an extension until it is converted to an Initial Contract record by hand.
    /// </remarks>
    public SowType SowType { get; set; } = SowType.InitialContract;

    /// <summary>
    /// Whether this period carries a rate increase — CHECK-enforced to
    /// <see cref="Compass.SowType.SowExtension"/> alone. The rate itself is never stored (AC-NFR-6).
    /// </summary>
    public bool RateIncrease { get; set; }

    /// <summary>
    /// Whether this period has passed the application's validation rules — non-overlap and
    /// end-on-or-after-start (FR-053, FR-054).
    /// </summary>
    public bool HasPassedApplicationValidation { get; set; } = false;

    /// <summary>Required start date.</summary>
    public DateOnly SowStartDate { get; set; }

    /// <summary>
    /// Required end date, CHECK-enforced to be <c>&gt;= sow_start_date</c>. The dashboard's "SOW
    /// expiring &lt; 90 days" tile reads it, where no later SOW exists for the same assignment.
    /// </summary>
    public DateOnly SowEndDate { get; set; }

    /// <summary>Optional free-text note. Elevated-visibility only, per the feature spec.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// The identifier this record carried in the legacy TPS directory, or <c>null</c> when it was
    /// created in Compass.
    /// </summary>
    public string? LegacyTpsId { get; set; }

    /// <summary>The owning assignment.</summary>
    public ClientAssignment? ClientAssignment { get; set; }
}
