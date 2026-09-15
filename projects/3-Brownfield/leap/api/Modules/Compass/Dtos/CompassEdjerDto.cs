namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// One EDJEr as the configuration API returns it — exactly AC-17's field set, plus the identifier.
/// </summary>
/// <param name="Id">The EDJEr's identity key.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="HireDate">Date of hire.</param>
/// <param name="Email">The EDJEr's email address.</param>
/// <param name="EmployeeTypeId">The employment classification. Only active types may be assigned.</param>
/// <param name="CoachEmployeeId">Another EDJEr who coaches this one. Optional (AC-17).</param>
/// <param name="StateOfResidence">A USPS code from the 50 states plus DC.</param>
/// <param name="IsActive">Whether the EDJEr is currently with Leading EDJE.</param>
/// <param name="TimesheetRequired">Time-tracking setting, Super-Admin-only (BR-1).</param>
/// <param name="CanSubmitUnder40">Time-tracking setting, Super-Admin-only (BR-1).</param>
/// <param name="IncludeInPayroll">Time-tracking setting, Super-Admin-only (BR-1).</param>
/// <param name="Timezone">
/// The EDJEr's IANA timezone, always one of <see cref="UsTimeZones.All"/> (FR-8.1). Field order here is
/// purely cosmetic and can be freely rearranged without affecting callers.
/// </param>
/// <param name="IsDeliveryTeam">Whether the EDJEr is on the delivery team.</param>
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
