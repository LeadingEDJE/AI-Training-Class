using System.Text.Json.Serialization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// One assignment row on the write surface, per <c>contracts/assignment-write-surface.md</c> §3.
/// </summary>
/// <remarks>
/// <see cref="IsCurrent"/> comes from <see cref="Interfaces.IClientStatusDerivation"/> and is never
/// computed here (FR-005). A <c>Sows</c> collection is deliberately absent from this shape.
/// <see cref="Note"/> is withheld as absent, not null (<c>[JsonIgnore(WhenWritingNull)]</c>),
/// matching ADR-008 rule 2 — a nullable DTO cannot distinguish "withheld" from "empty". Writes
/// require Compass Ops or the Compass root, and <c>GetById</c> additionally admits Compass Admin and
/// Compass Sales (the Elevated tier, AC-16/FR-025), so every caller who can reach this endpoint is at
/// least Elevated and there is no reachable caller to withhold the note from.
/// </remarks>
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
    /// Not a property of the assignment — it is <c>Client.IsInternal</c>, carried here so the
    /// assignment screen can hide the contract and invoicing surfaces without a second fetch of the
    /// client. Internal work is EDJE on EDJE: there is no counterparty to sign a statement of work
    /// with and nothing to invoice, so both surfaces are meaningless rather than merely empty.
    /// <c>ClientView.IsInternal</c> carries the same flag to the client screen for the same reason.
    /// Always present and never conditional on the caller: it decides whether a section renders, which
    /// is a layout question rather than an entitlement, so unlike <see cref="Note"/> it carries no
    /// <c>[JsonIgnore]</c> and has no withheld state.
    /// </remarks>
    public bool IsInternal { get; init; }

    /// <summary>Free-text note, absent (not null) when none was recorded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }

    /// <summary>
    /// This assignment's own invoice-frequency override, or <c>null</c> when it sets none (FR-036).
    /// </summary>
    public int? InvoiceFrequencyTypeId { get; init; }

    /// <summary>
    /// The frequency that actually applies: the override where set, otherwise the client default,
    /// otherwise <c>null</c> meaning none set (FR-037, FR-039).
    /// </summary>
    /// <remarks>
    /// <c>null</c> is neither an error nor a substituted value — a client may genuinely have no cadence
    /// agreed yet, and inventing a house default here would misreport the contract.
    /// </remarks>
    public string? EffectiveInvoiceFrequency { get; init; }
}
