namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// A contract period (SOW) under a client assignment. ERD table <c>compass.sow</c>.
/// </summary>
/// <remarks>
/// Non-overlapping within one assignment; gaps are allowed. On PostgreSQL 16 (ADR-003) that is
/// enforced declaratively, by an exclusion constraint over a <c>daterange</c> scoped to
/// <see cref="ClientAssignmentId"/>. <see cref="SowType"/> replaces the ERD's <c>is_extension</c>
/// boolean so FR-051's <see cref="Compass.SowType.LegacyMigrated"/> has somewhere to live.
///
/// Both date rules are partial, keyed on the type (FR-053): the exclusion constraint and the
/// end-on-or-after-start CHECK carry a <c>WHERE</c> clause skipping that type, so a TPS load is
/// admitted as recorded while everything Compass creates stays protected. Do not drop the constraints
/// instead; the exemption is load-time only, per <see cref="HasPassedApplicationValidation"/>.
/// </remarks>
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
    /// The initializer is explicit rather than relying on <c>default</c>: a new SOW is an Initial
    /// Contract until it is made an extension, which is domain meaning worth stating outright.
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
    /// <remarks>
    /// Defaults to <c>false</c>, and the initializer is explicit so nobody tidies it to <c>true</c>. A
    /// newly constructed period has not been validated yet — validation happens on save — so
    /// <c>false</c> is the only honest and only fail-safe starting value; the opposite would let a row
    /// assert a check it never passed.
    ///
    /// Setting it is the service layer's job: the owning service sets it <c>true</c> on a successful
    /// save, and a <see cref="Compass.SowType.LegacyMigrated"/> row stays <c>false</c> until its first
    /// edit through the application satisfies both rules in full. That service does not exist yet —
    /// until it does, anything writing a SOW must set this itself, as <c>CompassDirectorySeeder</c> does.
    /// </remarks>
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
    /// <remarks>
    /// Permanent provenance: it answers "what did this record come from?" after the migration tool
    /// and its crosswalk file are gone, and lets a lost crosswalk be rebuilt by reading it back from
    /// the target. Unique when present, and null for everything a person creates — Postgres unique
    /// indexes are NULLS DISTINCT, so that uniqueness costs a hand-created record nothing. Only a
    /// migration run may set it.
    /// </remarks>
    public string? LegacyTpsId { get; set; }

    /// <summary>The owning assignment.</summary>
    public ClientAssignment? ClientAssignment { get; set; }
}
