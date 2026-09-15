namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// An EDJEr — a member of the Leading EDJE team. ERD table <c>compass.employee</c>.
/// </summary>
public class Employee
{
    /// <summary>Primary key. Mapped to the ERD column <c>employee_id</c>.</summary>
    public int Id { get; set; }

    /// <summary>Required given name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Required family name. Indexed for directory search.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Required hire date. A native Postgres <c>date</c> — no time component.</summary>
    public DateOnly HireDate { get; set; }

    /// <summary>Required, unique across active and inactive EDJErs.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Required FK to <see cref="EmployeeType"/>.</summary>
    public int EmployeeTypeId { get; set; }

    /// <summary>Required self-reference: this EDJEr's coach is another EDJEr.</summary>
    public int? CoachEmployeeId { get; set; }

    /// <summary>Active flag. No termination dates are tracked.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Two-letter US state code, required. Constrained to the 48 contiguous states by a database
    /// CHECK, per the residency policy doc.
    /// </summary>
    public string StateOfResidence { get; set; } = string.Empty;

    /// <summary>
    /// The EDJEr's timezone, as an IANA identifier — required, one of <see cref="UsTimeZones.All"/>.
    /// </summary>
    public string Timezone { get; set; } = UsTimeZones.Default;

    /// <summary>Time-tracking support field.</summary>
    public bool TimesheetRequired { get; set; }

    /// <summary>Time-tracking support field.</summary>
    public bool CanSubmitUnder40 { get; set; }

    /// <summary>Time-tracking support field.</summary>
    public bool IncludeInPayroll { get; set; }

    /// <summary>
    /// Whether this EDJEr is on the delivery team. Stored, never derived.
    /// </summary>
    /// <remarks>
    /// Compass derives this from the employee's title category automatically (issue #614), so the
    /// stored value here is only a cache that a nightly job refreshes. The default is <c>false</c>
    /// because most newly created EDJErs are not yet delivery-facing, and the nightly refresh
    /// corrects it once a title category is assigned.
    /// </remarks>
    public bool IsDeliveryTeam { get; set; } = true;

    /// <summary>
    /// The identifier this record carried in the legacy TPS directory, or <c>null</c> when it was
    /// created in Compass.
    /// </summary>
    public string? LegacyTpsId { get; set; }

    /// <summary>The type classifying this EDJEr.</summary>
    public EmployeeType? EmployeeType { get; set; }

    /// <summary>This EDJEr's coach, if any.</summary>
    public Employee? Coach { get; set; }

    /// <summary>EDJErs coached by this EDJEr.</summary>
    public ICollection<Employee> Coachees { get; set; } = [];

    /// <summary>This EDJEr's client engagements, past and present.</summary>
    public ICollection<ClientAssignment> ClientAssignments { get; set; } = [];
}
