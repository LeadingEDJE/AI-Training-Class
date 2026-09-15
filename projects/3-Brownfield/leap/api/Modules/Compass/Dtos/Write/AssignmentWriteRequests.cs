namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;

/// <summary>
/// <c>POST /api/compass/assignments</c>. <c>contracts/assignment-write-surface.md</c> §2.
/// </summary>
public sealed record CreateAssignmentRequest(
    int EmployeeId,
    int ClientId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Note,
    int? InvoiceFrequencyTypeId = null,
    string? LegacyTpsId = null);

/// <summary><c>PUT /api/compass/assignments/{id}</c>.</summary>
public sealed record UpdateAssignmentRequest(
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Note,
    int? InvoiceFrequencyTypeId = null);
