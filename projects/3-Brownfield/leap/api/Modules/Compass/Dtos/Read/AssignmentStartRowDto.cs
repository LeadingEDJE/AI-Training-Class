namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// One row of the Assignment Start lookup (AC-40).
/// </summary>
/// <remarks>
/// The EDJEr, their employee type, the client, and the assignment's start date. The lookup answers
/// "what started between these dates", so it carries no end date, no status and no duration — those
/// belong to the Assignment Duration report (AC-39), a different question with a different row shape.
/// <see cref="StartDate"/> is a <see cref="DateOnly"/>, not a <see cref="DateTime"/>: it maps to a
/// native Postgres <c>date</c> with no value converter, and an assignment starts on a day rather
/// than at an instant, so a time component would invite a
/// timezone question the business rule does not have.
/// </remarks>
public sealed class AssignmentStartRowDto
{
    /// <summary>The assigned EDJEr's display name.</summary>
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>
    /// The EDJEr's employee type (e.g. "Full Time", "Part Time", "1099"), its own column following the
    /// name. Nullable on the wire even though the FK backing it is required in practice.
    /// </summary>
    public string? EmployeeType { get; init; }

    /// <summary>The client's name.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>The date the assignment started, inside the requested range at both ends.</summary>
    public DateOnly StartDate { get; init; }
}
