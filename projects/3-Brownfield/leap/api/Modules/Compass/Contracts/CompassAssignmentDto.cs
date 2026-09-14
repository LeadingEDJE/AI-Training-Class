namespace LeadingEDJE.Leap.Api.Modules.Compass.Contracts;

/// <summary>
/// A published client assignment — read family 3 of the Compass Directory boundary (FR-006, FR-012).
/// </summary>
/// <remarks>
/// One contract reached by three lookups: <c>GET /assignments/{id}</c>,
/// <c>GET /employees/{id}/assignments</c> and <c>GET /clients/{id}/assignments</c> — the shape
/// <c>CompassTransportContractTests.TheDirectoryBoundary_PublishesOneDtoPerFamily_WithNoSharedShapes</c>
/// tolerates.
///
/// No raw <c>InvoiceFrequencyTypeId</c>: this boundary publishes names, never internal keys, as
/// <see cref="CompassClientDto"/> and <see cref="CompassBillableCategoryDto"/> do.
/// <see cref="EffectiveInvoiceFrequency"/> reuses <c>CompassAssignmentService.ToDto</c>'s precedence
/// expression rather than restating it (FR-012).
/// </remarks>
public sealed class CompassAssignmentDto
{
    /// <summary>The assignment's identifier — <c>compass.client_assignment.client_assignment_id</c>.</summary>
    public int Id { get; init; }

    /// <summary>The assigned EDJEr's identifier — <c>compass.employee.employee_id</c>.</summary>
    public int EmployeeId { get; init; }

    /// <summary>The client's identifier — <c>compass.client.client_id</c>.</summary>
    public int ClientId { get; init; }

    /// <summary>The client's name, resolved here so every consumer avoids a second round trip.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>The engagement's start date.</summary>
    public DateOnly StartDate { get; init; }

    /// <summary>The engagement's end date. <c>null</c> means open-ended.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Free-text note. Nullable.</summary>
    public string? Note { get; init; }

    /// <summary>
    /// The invoicing cadence name that actually applies to this engagement (FR-012): the assignment's
    /// own override where set, else the client's default, else <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Nullable, and "neither set" is a real, reportable state rather than withheld data. Do not
    /// invent a house default here — this is reported data, not a tier-gated absence.
    /// </remarks>
    public string? EffectiveInvoiceFrequency { get; init; }
}
