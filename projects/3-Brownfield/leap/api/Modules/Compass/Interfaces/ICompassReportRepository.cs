using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Queries that belong to the Compass reports alone.
/// </summary>
public interface ICompassReportRepository
{
    /// <summary>
    /// One row per EDJEr–client pair holding at least one active assignment, with total tenure
    /// summed across every started assignment for that pair, longest first (FR-014, FR-015).
    /// </summary>
    /// <param name="today">The business date, from <see cref="ICompassBusinessDate"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AssignmentDurationRowDto>> GetAssignmentDurationAsync(
        DateOnly today, CancellationToken cancellationToken);

    /// <summary>
    /// Every assignment whose start date falls within the inclusive range (AC-40, FR-016).
    /// </summary>
    /// <remarks>
    /// This method also validates that <c>from</c> does not fall after <c>to</c>, returning an empty
    /// result for an inverted range rather than letting the endpoint reject it.
    /// </remarks>
    /// <param name="from">First day of the range, inclusive.</param>
    /// <param name="to">Last day of the range, inclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AssignmentStartRowDto>> GetAssignmentStartsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every SOW extension period whose start date falls within the inclusive range.
    /// </summary>
    /// <param name="from">First day of the range, inclusive.</param>
    /// <param name="to">Last day of the range, inclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SowExtensionRowDto>> GetSowExtensionsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}
