using System.Text.Json.Serialization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Contracts;

/// <summary>
/// A published SOW (contract period) under a client assignment — read family 4 of the Compass
/// Directory boundary (FR-007).
/// </summary>
/// <remarks>
/// Named <c>CompassDirectorySowDto</c> because a <c>CompassSowDto</c> already exists in this namespace
/// — the admin/migration surface's shape, which publishes <c>Sow.HasPassedApplicationValidation</c>,
/// forbidden here by FR-011. Two distinct contracts: do not reuse, merge or rename them.
///
/// <see cref="RateIncrease"/> and <see cref="Note"/> are elevated-visibility only (BR-1), withheld as
/// absent rather than <c>null</c> (<c>JsonIgnoreCondition.WhenWritingNull</c>), since a nullable DTO
/// cannot otherwise distinguish "withheld" from "never given"; both are gated by
/// <see cref="Authorization.CompassTierVisibility.SeesOthersSowsAndNotes"/> in the directory service —
/// the withholding shape, not the type, of <c>Dtos/Read/SowRowDto.cs</c>'s payload (ADR-008).
/// </remarks>
public sealed class CompassDirectorySowDto
{
    /// <summary>The SOW's identifier — <c>compass.sow.sow_id</c>.</summary>
    public int Id { get; init; }

    /// <summary>
    /// The owning assignment's identifier — <c>compass.client_assignment.client_assignment_id</c>.
    /// </summary>
    public int ClientAssignmentId { get; init; }

    /// <summary>
    /// What this period represents, as the enum's name. An explicit <c>string</c>, so the published
    /// contract's intent is unambiguous in the C# type itself.
    /// </summary>
    public string SowType { get; init; } = string.Empty;

    /// <summary>The period's start date.</summary>
    public DateOnly SowStartDate { get; init; }

    /// <summary>The period's end date.</summary>
    public DateOnly SowEndDate { get; init; }

    /// <summary>Whether this period carries a rate increase. Elevated-visibility only; absent otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RateIncrease { get; init; }

    /// <summary>
    /// Free-text note. Absent when withheld by tier, OR when none was ever recorded — the DTO cannot
    /// distinguish the two, matching <c>SowRowDto</c>'s own documented shape.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }
}
