namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;

/// <summary>
/// <c>POST /api/compass/assignments</c>. <c>contracts/assignment-write-surface.md</c> §2.
/// </summary>
/// <remarks>
/// <c>InvoiceFrequencyTypeId</c> is this assignment's optional override of its client's default
/// invoicing cadence (FR-036). It defaults to <c>null</c> — meaning "no override, bill the way this
/// client does" — and the default keeps existing call sites compiling, which is what makes adding it
/// additive rather than a contract break.
/// </remarks>
public sealed record CreateAssignmentRequest(
    int EmployeeId,
    int ClientId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Note,
    int? InvoiceFrequencyTypeId = null,
    string? LegacyTpsId = null);

/// <summary>
/// <c>PUT /api/compass/assignments/{id}</c>. §2. <c>EmployeeId</c>/<c>ClientId</c> are deliberately
/// absent: moving an assignment would rewrite the history the AC-24 panel and the AC-39 report read.
/// </summary>
public sealed record UpdateAssignmentRequest(
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Note,
    int? InvoiceFrequencyTypeId = null);
