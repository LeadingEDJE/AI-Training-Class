using System.Text.Json.Serialization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>One row of the Team Directory (AC-5), projected for the viewer's tier.</summary>
/// <remarks>
/// Part of the Compass application read surface, not the published Directory boundary (ADR-008):
/// the shape varies by who is looking, which a stable integration contract must never do.
/// </remarks>
public sealed class TeamDirectoryRowDto
{
    /// <summary>The EDJEr's identifier.</summary>
    public int Id { get; init; }

    /// <summary>Given name.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Family name — the column AC-6's search matches against.</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>Hire date — the directory's default sort.</summary>
    public DateOnly HireDate { get; init; }

    /// <summary>Work email, carrying AC-7's link into the employee detail.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Employee type name, and one of AC-6's two filters.</summary>
    public string EmployeeType { get; init; } = string.Empty;

    /// <summary>
    /// The coach's own identifier — the drill-in into their record. <c>null</c>
    /// exactly when <see cref="Coach"/> is.
    /// </summary>
    public int? CoachId { get; init; }

    /// <summary>The coach's display name, or <c>null</c> where the EDJEr has none (AC-17).</summary>
    public string? Coach { get; init; }

    /// <summary>State of residence, and AC-6's other filter.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Every current assignment (BR-7), not just the first.
    /// </summary>
    /// <remarks>A list because AC-5 requires all of them to display; a scalar truncates silently.</remarks>
    public IReadOnlyList<TeamDirectoryAssignmentDto> CurrentAssignments { get; init; } = [];

    /// <summary>
    /// Whether the EDJEr is active — elevated tiers only, and absent otherwise.
    /// </summary>
    /// <remarks>
    /// A baseline viewer's set is all-active by construction (AC-9), so the field carries no
    /// information for them. <c>WhenWritingNull</c> makes it absent rather than falsy (FR-005).
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsActive { get; init; }
}

/// <summary>A client an EDJEr is currently assigned to, with AC-7's link.</summary>
public sealed class TeamDirectoryAssignmentDto
{
    /// <summary>The client's identifier.</summary>
    public int ClientId { get; init; }

    /// <summary>The client's name, as displayed in the row.</summary>
    public string ClientName { get; init; } = string.Empty;
}
