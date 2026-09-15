namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// The <c>compass</c> <c>table.column</c> pairs published by the LEAP ERD's data dictionary, so a
/// test can walk them against the live Postgres catalog.
/// </summary>
/// <remarks>
/// <para>
/// Source. <c>docs/design/edje-compass-erd.docx</c>, revision of 2026-08-10, which supersedes
/// the 2026-07-22 revision. That earlier revision was a design document written for MySQL 8.0
/// before the schema existed; this one was regenerated from the shipped Postgres schema in
/// PR #199, so where the two disagree the current document is correct. Transcribing the older
/// revision was not possible: it still modelled <c>sow.is_extension</c> as a boolean, a column
/// PR #186 had already replaced.
/// </para>
/// <para>
/// <c>sow</c> needs no special-casing, and that is a change. Task 002-T008 warns that
/// <c>sow</c> is "spec-governed, not ERD-governed" and calls it "the single most likely source of a
/// false failure", because PR #186 departed from the ERD on that one table. PR #199 folded that
/// departure back into the document: the Data Dictionary now lists <c>sow_type</c> and
/// <c>has_passed_application_validation</c> as columns, and <c>is_extension</c> survives only in the
/// prose recording its removal. The ERD and the schema agree, so this list is a straight
/// transcription with no carve-out. A future divergence should be repaired in the ERD, not worked
/// around here.
/// </para>
/// <para>
/// Scope: the Data Dictionary section only. The document holds three other tables — the
/// Relationships table (parent/child cardinality), the Constraints and Indexes table (live database
/// object names), and a closing pointer table. None of them describe columns. This matters more than
/// it sounds: those tables sit after the last <c>compass.sow</c> heading, so a transcription
/// that attributes rows to the most recent heading silently files every constraint and index name as
/// a <c>sow</c> column. A trial extraction did exactly that and produced 27 columns for <c>sow</c>,
/// among them <c>ck_employee_state_of_residence_us</c> and <c>ix_employee_last_name</c>. The Data
/// Dictionary tables are the seven whose first header cell reads "Column".
/// </para>
/// <para>
/// Two things inside those seven tables are deliberately not columns. The <c>client</c> table
/// ends with a <c>(no status column)</c> row, an annotation recording that Active/Inactive is derived
/// from a client's assignments rather than stored (BR-11, AC-42); its absence is already asserted by
/// <c>CompassSchemaFromErdTests.Client_HasNoStoredStatusColumn</c>. And the <c>★</c> against
/// <c>sow_type</c> and <c>has_passed_application_validation</c> marks them as new since the previous
/// revision, so it is stripped from the name.
/// </para>
/// <para>
/// Five of these rows are a documented departure, not a transcription (feature 010) — eight
/// in total, counting <c>client_assignment.invoice_frequency_type_id</c>, which feature 006 US6 added
/// and annotated separately at its own entry, and <c>employee.timezone</c> and
/// <c>employee.is_delivery_team</c>, which issue #420 and issue #502 added and which annotate at their
/// own entries too. The <c>legacy_tps_id</c> on <c>employee</c>,
/// <c>client</c>, <c>client_assignment</c>, <c>billable_time_category</c> and <c>sow</c> postdates the
/// 2026-08-10 revision and appears in no Data Dictionary row. They are listed anyway because this
/// list is what
/// <c>CompassColumns_MatchTheErdDataDictionaryExactly</c> compares the live catalog against in BOTH
/// directions — omitting them would fail the walk as "undocumented columns" and leave the only
/// completeness gate on the Compass schema permanently red. <c>employee.timezone</c> and
/// <c>employee.is_delivery_team</c> are in the same position: PRD v9 FR-8 requires the first and the
/// issue #502 ruling requires the second, and the 2026-08-10 ERD revision predates both.
/// </para>
/// <para>
/// This is the same situation <c>sow_type</c> was in between PR #186 and PR #199, and it wants the
/// same resolution: the ERD should be regenerated from the shipped schema, and this note deleted
/// when it is. Repair the document rather than growing the carve-out — the paragraph above says
/// so about <c>sow</c>, and it applies here. Until then, a reader comparing this file to the
/// document will find eight extra rows, and this paragraph is why.
/// </para>
/// <para>
/// Cross-check. The count below reconciles independently against the module's
/// <c>HasColumnName</c> calls across the eight files in
/// <c>api/Modules/Compass/Data/Configurations/</c>, and per table against each entity's mapped
/// property count once navigation properties are set aside — <c>Employee</c>'s 19 properties are 15
/// columns and 4 navigations, <c>Sow</c>'s 9 are 8 and 1, and so on. Two independent readings of the
/// same shape agreeing is the point; neither is the assertion. The assertion is
/// <c>CompassColumns_MatchTheErdDataDictionaryExactly</c>, which compares this list against the
/// running catalog both ways, so a column missing from either side fails it. When re-deriving the
/// list, take only the seven dictionary tables in section 6 and stop: the relationship, foreign key,
/// constraint, index and view tables that follow describe no columns, and an extraction that files
/// rows under the nearest preceding heading turns index names into <c>sow</c> columns. If document
/// and schema diverge, regenerate the document from the catalog rather than adding an exception.
/// </remarks>
internal static class ErdDataDictionary
{
    /// <summary>
    /// The number of <c>table.column</c> pairs the ERD's Data Dictionary publishes.
    /// </summary>
    /// <remarks>
    /// Asserted separately from the set comparison and not derived from <see cref="Pairs"/>: an
    /// empty or truncated list satisfies a both-direction set comparison trivially, so a walk that
    /// only compares sets is a gate that passes when it has nothing to say. Changing this number is
    /// the deliberate act of accepting that the schema's shape changed.
    /// 50 → 56 (technical-skills feature, outside the ERD document itself). <c>skill</c> (3 columns)
    /// and <c>employee_skill</c> (3 columns) are listed for the same reason the postdating
    /// <c>legacy_tps_id</c>/<c>timezone</c>/<c>is_delivery_team</c> columns are: omitting them would
    /// fail this walk as "undocumented columns" rather than leave the gate meaningful.
    /// </remarks>
    internal const int ExpectedPairCount = 56;

    /// <summary>
    /// Grouped by table with the lookups first — this file's own arrangement, not the document's.
    /// The assertion compares sets, so the order carries no meaning.
    /// </summary>
    internal static readonly string[] Pairs =
    [
        // compass.employee_type — Super Admin-managed lookup of employee types
        "employee_type.employee_type_id",
        "employee_type.type_name",
        "employee_type.is_active",
        // compass.invoice_frequency_type — Super Admin-managed lookup of invoice frequencies
        "invoice_frequency_type.invoice_frequency_type_id",
        "invoice_frequency_type.type_name",
        "invoice_frequency_type.is_active",
        // compass.employee — Leading EDJE EDJErs (HiBob remains HRIS of record)
        "employee.employee_id",
        "employee.first_name",
        "employee.last_name",
        "employee.hire_date",
        "employee.email",
        "employee.employee_type_id",
        "employee.coach_employee_id",
        "employee.is_active",
        "employee.state_of_residence",
        "employee.timesheet_required",
        "employee.can_submit_under_40",
        "employee.include_in_payroll",
        "employee.legacy_tps_id",
        "employee.timezone",
        "employee.is_delivery_team",
        // compass.client — Leading EDJE clients (incl. internal "beach" clients).
        // No status column: derived from assignments (BR-11, AC-42).
        "client.client_id",
        "client.client_name",
        "client.msa_signed_date",
        "client.nda_signed_date",
        "client.is_internal",
        "client.invoice_frequency_type_id",
        "client.legacy_tps_id",
        // compass.billable_time_category — 0..many per client; consumed by the timesheet system
        "billable_time_category.billable_time_category_id",
        "billable_time_category.client_id",
        "billable_time_category.category_name",
        "billable_time_category.is_active",
        "billable_time_category.legacy_tps_id",
        // compass.client_assignment — an EDJEr's engagement at a client
        "client_assignment.client_assignment_id",
        "client_assignment.employee_id",
        "client_assignment.client_id",
        "client_assignment.start_date",
        "client_assignment.end_date",
        "client_assignment.note",
        "client_assignment.invoice_frequency_type_id",
        "client_assignment.legacy_tps_id",
        // compass.sow — contract periods attached to a client assignment.
        "sow.sow_id",
        "sow.client_assignment_id",
        "sow.sow_type",
        "sow.rate_increase",
        "sow.has_passed_application_validation",
        "sow.sow_start_date",
        "sow.sow_end_date",
        "sow.note",
        "sow.legacy_tps_id",
        // compass.skill — Super Admin-managed lookup of technical skills (technical-skills feature,
        // postdates the ERD document, listed for the same reason as employee.timezone above)
        "skill.skill_id",
        "skill.name",
        "skill.is_active",
        // compass.employee_skill — join of an EDJEr to a tagged skill
        "employee_skill.employee_skill_id",
        "employee_skill.employee_id",
        "employee_skill.skill_id",
    ];
}
