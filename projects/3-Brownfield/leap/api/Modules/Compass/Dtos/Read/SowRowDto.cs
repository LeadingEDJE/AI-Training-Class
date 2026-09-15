using System.Text.Json.Serialization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// One contract period on the SOW write surface, per <c>contracts/sow-write-surface.md</c> §3.
/// </summary>
/// <remarks>
/// <see cref="RateIncrease"/> and <see cref="Note"/> are visible to every caller who can reach this
/// endpoint, baseline viewers included; the withholding described in the assignment surface's docs
/// does not apply here.
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
