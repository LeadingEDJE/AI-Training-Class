namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// One EDJEr as the administration list renders them.
/// </summary>
/// <remarks>
/// Narrower than <see cref="CompassEdjerDto"/> on purpose: it carries no time-tracking flags, which
/// belong to the record's own screen (BR-1). <paramref name="EmployeeTypeName"/> and
/// <paramref name="CoachName"/> are both denormalised, resolved server-side in the same read; without
/// them every row would need a second client-side lookup, the N+1 that AC-NFR-4's p95 target is
/// measured against. This DTO also feeds the form's coach picker, which is why the names are separate
/// members rather than one pre-joined string: the picker sorts by family name.
/// </remarks>
/// <param name="Id">The EDJEr's identity key.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="Email">The EDJEr's unique address.</param>
/// <param name="EmployeeTypeName">The classification's display name, resolved server-side.</param>
/// <param name="IsActive">Whether the EDJEr is currently with Leading EDJE.</param>
/// <param name="HireDate">When the EDJEr started, matching Team Directory's own column (issue #659).</param>
/// <param name="StateOfResidence">The two-letter state code, matching Team Directory's own column.</param>
/// <param name="CoachName">
/// The coach's resolved display name, or null when the EDJEr has none. Plain text on this screen by
/// owner request — unlike Team Directory's coach column, it is not a link to the coach's own record.
/// </param>
public sealed record CompassEdjerSummaryDto(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    string EmployeeTypeName,
    bool IsActive,
    DateOnly HireDate,
    string StateOfResidence,
    string? CoachName
);
