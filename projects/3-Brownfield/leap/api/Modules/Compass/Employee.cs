namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// An EDJEr — a member of the Leading EDJE team. ERD table <c>compass.employee</c>.
/// </summary>
/// <remarks>
/// Not the timesheet module's same-named entity: that one backs <c>public.employees</c>, this one
/// <c>compass.employee</c>, and both exist during the transition — ADR-002 makes Compass the eventual
/// system of record, ADR-006 covers moving the timesheet directory entities. Name that sibling in
/// prose only, never as a qualified namespace token: <c>CompassBoundaryTests</c> scans raw source
/// text, so one in a comment fails the build. From outside, the simple name binds the wrong way — use
/// an import alias. Deliberately absent (AC-NFR-6): no termination
/// date and no billing rate; EDJErs are deactivated via <see cref="IsActive"/> and never deleted, so
/// <see cref="Email"/> is unique across active and inactive rows alike.
/// </remarks>
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

    /// <summary>
    /// Optional self-reference: this EDJEr's coach is another EDJEr. UI-required but DB-nullable,
    /// because the top of the org chart has no coach — the no-coach notification path exists for it.
    /// </summary>
    public int? CoachEmployeeId { get; set; }

    /// <summary>Active flag. No termination dates are tracked.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Two-letter US state code, required. Constrained to the 50 states plus DC by a database CHECK —
    /// AC-NFR-6 supports only US-based EDJErs.
    /// </summary>
    /// <remarks>
    /// Required, and the TPS extract does not change that: it carries no state for a minority of its
    /// EDJErs, and the owner decision is that those default to <c>OH</c> at migration time rather than
    /// the column becoming optional. The default is applied once, in
    /// <c>EmployeeRules.StateOfResidence</c>, and the load reports how many rows it touched, so the
    /// substitution is visible rather than silent. Nothing downstream handles an absent state.
    /// </remarks>
    public string StateOfResidence { get; set; } = string.Empty;

    /// <summary>
    /// The EDJEr's timezone, as an IANA identifier — required, one of <see cref="UsTimeZones.All"/>.
    /// </summary>
    /// <remarks>
    /// A directory attribute, not an OOTO one. Compass omitted it originally under Principle II — no
    /// speculative fields for a consumer that was not migrating — but TPS modelled it as a general
    /// employee attribute, so it belongs here. An IANA id (FR-8.6). No CHECK constrains it, unlike
    /// <see cref="StateOfResidence"/> (see <see cref="UsTimeZones"/>); the six are enforced on write.
    ///
    /// The initialiser is not what the write path relies on: <c>CompassEmployeeService</c> assigns
    /// this on every create, from the request or from <see cref="UsTimeZones.Default"/> (FR-8.1). It
    /// stays because removing it makes this a non-nullable reference with no initialiser (CS8618, an
    /// error here) unless it becomes <c>required</c>, which every test fixture would then have to set.
    /// </remarks>
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
    /// Compass models no job title, title category or employment history (issue #502), so what
    /// the sibling timesheet module derives from those tables is a plain flag here: nothing
    /// reads them to populate it, and nothing recomputes it. The default is <c>true</c> because
    /// both defaults are wrong for somebody and <c>true</c> is the one whose error a person
    /// notices — being treated as delivery raises a validation warning, while the reverse
    /// silently stops two rules applying. This initialiser is the weakest of the three
    /// mechanisms carrying that default; exceptions are corrected by hand afterwards (FR-015).
    /// </remarks>
    public bool IsDeliveryTeam { get; set; } = true;

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

    /// <summary>The type classifying this EDJEr.</summary>
    public EmployeeType? EmployeeType { get; set; }

    /// <summary>This EDJEr's coach, if any.</summary>
    public Employee? Coach { get; set; }

    /// <summary>EDJErs coached by this EDJEr.</summary>
    public ICollection<Employee> Coachees { get; set; } = [];

    /// <summary>This EDJEr's client engagements, past and present.</summary>
    public ICollection<ClientAssignment> ClientAssignments { get; set; } = [];
}
