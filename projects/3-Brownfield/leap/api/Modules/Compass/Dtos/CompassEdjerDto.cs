namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// One EDJEr as the configuration API returns it — exactly AC-17's field set, plus the identifier.
/// </summary>
/// <remarks>
/// Nothing here is speculative (Principle II): every member traces to AC-17, the two fields AC-NFR-6
/// omits (a termination date, a billing rate) are absent, and <c>CompassEdjerDtoTests</c> pins the set.
/// This is not a widened <see cref="Contracts.CompassEmployeeDto"/>: FR-016 requires the three
/// time-tracking flags be absent from any payload served below Compass Super Admin, so the two stay
/// separate types rather than one type with conditional members. The flags are safe here because every
/// route returning this DTO requires <c>RolePolicy.CompassSuperAdmin</c>. Addressed by
/// <c>compass.employee</c>'s <c>int</c> key, matching the directory boundary rather than inventing a
/// second scheme (Constitution 1.2.0). Employee type and coach appear as ids, not names.
/// </remarks>
/// <param name="Id">The EDJEr's identity key.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="HireDate">Date of hire.</param>
/// <param name="Email">The identity that must never collide, active or inactive (BR-9).</param>
/// <param name="EmployeeTypeId">The employment classification. Only active types may be assigned.</param>
/// <param name="CoachEmployeeId">Another EDJEr who coaches this one. Optional (AC-17).</param>
/// <param name="StateOfResidence">A USPS code from the 50 states plus DC.</param>
/// <param name="IsActive">Whether the EDJEr is currently with Leading EDJE.</param>
/// <param name="TimesheetRequired">Time-tracking setting, Super-Admin-only (BR-1).</param>
/// <param name="CanSubmitUnder40">Time-tracking setting, Super-Admin-only (BR-1).</param>
/// <param name="IncludeInPayroll">Time-tracking setting, Super-Admin-only (BR-1).</param>
/// <param name="Timezone">
/// The EDJEr's IANA timezone, always one of <see cref="UsTimeZones.All"/> (FR-8.1). Last rather than
/// beside <paramref name="StateOfResidence"/>, which is where it belongs by meaning: a positional
/// record's parameter ORDER is its deconstruction order, and inserting mid-list silently rebinds
/// every positional read of it. Appending cannot.
/// </param>
/// <param name="IsDeliveryTeam">
/// Whether the EDJEr is on the delivery team (issue #502). A stored attribute, never derived, and
/// visible to every tier — the form needs it to render the control it edits. Appended for the same
/// reason <paramref name="Timezone"/> was: a positional record's parameter order is its
/// deconstruction order.
/// </param>
public sealed record CompassEdjerDto(
    int Id,
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
    string Timezone,
    bool IsDeliveryTeam
);
