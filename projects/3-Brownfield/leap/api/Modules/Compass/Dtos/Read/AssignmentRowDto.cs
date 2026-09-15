using System.Text.Json.Serialization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// One assignment row on the write surface, per <c>contracts/assignment-write-surface.md</c> §3.
/// </summary>
public sealed class AssignmentRowDto
{
    /// <summary>The assignment's identifier.</summary>
    public int Id { get; init; }

    /// <summary>The assigned EDJEr's identifier.</summary>
    public int EmployeeId { get; init; }

    /// <summary>The assigned EDJEr's display name.</summary>
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>The client's identifier.</summary>
    public int ClientId { get; init; }

    /// <summary>The client's name.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>Required start date.</summary>
    public DateOnly StartDate { get; init; }

    /// <summary>Optional end date; <c>null</c> means open-ended.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Whether this assignment is current, per <see cref="Interfaces.IClientStatusDerivation"/>.</summary>
    public bool IsCurrent { get; init; }

    /// <summary>
    /// The client's internal-EDJE ("beach") indicator, denormalised onto the assignment row.
    /// </summary>
    /// <remarks>
    /// Derived from a second client lookup performed by the frontend, not carried on the assignment
    /// itself. It is conditional on the caller's tier and can be withheld like <see cref="Note"/>, so a
    /// missing value here does not necessarily mean the client is external.
    /// </remarks>
    public bool IsInternal { get; init; }

    /// <summary>Free-text note, absent (not null) when none was recorded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }

    /// <summary>
    /// This assignment's own invoice-frequency override, or <c>null</c> when it sets none (FR-036).
    /// </summary>
    public int? InvoiceFrequencyTypeId { get; init; }

    /// <summary>The frequency that actually applies.</summary>
    public string? EffectiveInvoiceFrequency { get; init; }
}
