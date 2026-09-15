namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// One EDJEr as the administration list renders them.
/// </summary>
/// <param name="Id">The EDJEr's identity key.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="Email">The EDJEr's unique address.</param>
/// <param name="EmployeeTypeName">The classification's display name, resolved server-side.</param>
/// <param name="IsActive">Whether the EDJEr is currently with Leading EDJE.</param>
/// <param name="HireDate">When the EDJEr started, matching Team Directory's own column (issue #659).</param>
/// <param name="StateOfResidence">The two-letter state code, matching Team Directory's own column.</param>
/// <param name="CoachName">
/// The coach's resolved display name, or null when the EDJEr has none. Rendered as a link into the
/// coach's own record, same as the Team Directory's coach column.
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
