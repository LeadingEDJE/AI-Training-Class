using System.Text.Json.Serialization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// One contract period on the SOW write surface, per <c>contracts/sow-write-surface.md</c> §3.
/// </summary>
/// <remarks>
/// <see cref="RateIncrease"/> and <see cref="Note"/> are elevated-visibility only (FR-018, BR-1),
/// withheld as absent rather than <c>null</c> (<c>[JsonIgnore(WhenWritingNull)]</c>) per ADR-008 rule
/// 2 — a nullable DTO cannot distinguish "withheld" from "never given". Per contract §1 the whole SOW
/// route group requires Compass Ops or the Compass root, with no widened read exception (unlike the
/// assignment surface's <c>GetById</c>), so every caller who can reach this endpoint is already
/// elevated. <c>HasPassedApplicationValidation</c> is not exposed at all: it is an internal
/// grandfathering marker (FR-046), not a criterion's field, and publishing it would invite a client to
/// reason about it (Principle II).
/// </remarks>
public sealed class SowRowDto
{
    /// <summary>The period's identifier.</summary>
    public int Id { get; init; }

    /// <summary>What this period represents, as the enum's name — <c>InitialContract</c> or <c>SowExtension</c>.</summary>
    public string SowType { get; init; } = string.Empty;

    /// <summary>Required start date.</summary>
    public DateOnly SowStartDate { get; init; }

    /// <summary>Required end date.</summary>
    public DateOnly SowEndDate { get; init; }

    /// <summary>Whether this period carries a rate increase. Elevated-visibility only; absent otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RateIncrease { get; init; }

    /// <summary>Free-text note, absent (not null) when none was recorded, or when withheld.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }
}
