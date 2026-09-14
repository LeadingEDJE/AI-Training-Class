namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// Adds or updates an EDJEr. Captures exactly AC-17's field set.
/// </summary>
/// <remarks>
/// One request type serves POST and PUT, because AC-17 and AC-19 describe the same field set on both
/// paths — including <paramref name="IsActive"/>, since AC-19's deactivation is an edit to the active
/// flag rather than a separate operation. There is no reason field: every EDJEr write is audited, but
/// the reason is system-generated (<c>CompassAuditReason</c>) because no acceptance criterion asks the
/// administrator for one. No termination date and no billing rate either (AC-NFR-6), and EDJErs are
/// never deleted, which is why email uniqueness spans inactive rows. Construct this with named
/// arguments: <paramref name="Timezone"/> sits before <paramref name="LegacyTpsId"/>, so a positional
/// list rebinds silently and compiles. <c>LegacyProvenanceTests</c> is the only fence.
/// </remarks>
/// <param name="FirstName">Given name. Required.</param>
/// <param name="LastName">Family name. Required.</param>
/// <param name="HireDate">Date of hire. Required.</param>
/// <param name="Email">
/// Required, and unique across every EDJEr, active or inactive, compared case-insensitively and trimmed
/// (BR-9, spec A-2).
/// </param>
/// <param name="EmployeeTypeId">
/// The employment classification. Must name an active employee type — the server refuses an
/// inactive one even though the selection list omits it (FR-005).
/// </param>
/// <param name="CoachEmployeeId">
/// Another EDJEr who coaches this one. Optional (AC-17). The mockup marks it required; the
/// acceptance criterion wins.
/// </param>
/// <param name="StateOfResidence">A USPS code from the 50 states plus DC (FR-014).</param>
/// <param name="IsActive">
/// Whether the EDJEr is with Leading EDJE. Turning this off is refused while they hold an assignment
/// with no end date (FR-019, BR-10).
/// </param>
/// <param name="TimesheetRequired">Time-tracking setting, Super-Admin-only (BR-1).</param>
/// <param name="CanSubmitUnder40">Time-tracking setting, Super-Admin-only (BR-1).</param>
/// <param name="IncludeInPayroll">Time-tracking setting, Super-Admin-only (BR-1).</param>
/// <param name="Timezone">
/// The EDJEr's IANA timezone — one of <see cref="UsTimeZones.All"/> (FR-8.1). Required of the
/// screen; optional on the wire, and that is not an oversight. <c>/api/compass/v1</c> is
/// additive-only (FR-082, ADR-004), and <c>oasdiff</c> classifies a NEW REQUIRED request property as
/// a breaking change — so a required parameter here would have to be <c>v2</c>. Absent is therefore
/// accepted and means "say nothing about the timezone": a create falls back to
/// <see cref="UsTimeZones.Default"/> (the same value the column defaults to) and an update leaves the
/// stored zone alone. Present-but-blank is a rejection, not a fallback — that is what a form
/// sends when the administrator picked nothing, and silently filing them under Eastern is exactly
/// what FR-8.1 asks the screen to stop doing.
/// </param>
/// <param name="LegacyTpsId">
/// The identifier this record carried in the legacy TPS directory. Only the migration principal
/// may supply it (<see cref="CompassLegacyProvenance"/>); any other caller sending a value is
/// refused rather than having it quietly dropped, on create and on update alike. Absent for
/// everything a person creates. Immutable once set: an authorised caller may re-send it on an
/// update, but it is not re-applied.
/// </param>
/// <param name="IsDeliveryTeam">
/// Whether the EDJEr is on the delivery team (issue #502). Nullable and trailing, both deliberately.
/// A non-nullable <c>bool</c> binds <c>false</c> for a client that says nothing, which is the one
/// value that must never be inferred; and <c>/api/compass/v1</c> is additive-only (FR-082, ADR-004),
/// so a new required request property would be breaking and force a <c>v2</c>. Absent therefore
/// means "say nothing about it": a create falls back to <c>true</c>, the value the column itself
/// defaults to, and an update leaves the stored flag alone, so a hand-made correction survives a
/// save by a client older than this field. It goes after <paramref name="LegacyTpsId"/> because the
/// ETL passes the leading arguments positionally.
/// </param>
public record CompassEdjerRequest(
    string FirstName,
    string LastName,
    DateOnly HireDate,
    string Email,
    int EmployeeTypeId,
    int? CoachEmployeeId,
    string StateOfResidence,
    bool IsActive,
    bool TimesheetRequired,
    bool CanSubmitUnder40,
    bool IncludeInPayroll,
    string? Timezone = null,
    string? LegacyTpsId = null,
    bool? IsDeliveryTeam = null
);
