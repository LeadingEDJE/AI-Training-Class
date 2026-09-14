using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Queries that belong to the Compass reports alone.
/// </summary>
/// <remarks>
/// Not a second definition of the dashboard's populations. The Availability Report deliberately
/// reads <see cref="ICompassDashboardRepository"/> — re-deriving those three populations here is the
/// divergence BR-11 forbids, and <c>CompassReportReadService</c>'s remarks say so. This interface holds
/// only queries the dashboard does not have: the duration aggregation, which FR-021 requires be
/// set-based, and the start-date range.
/// </remarks>
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
    /// The availability report deliberately reads <see cref="ICompassDashboardRepository"/> instead: its
    /// three sections are the dashboard's three breakdowns, and re-deriving them is the divergence BR-11
    /// forbids. That is the line between the two repositories — this one holds queries the dashboard has
    /// no counterpart for, not every query a report happens to make. Validation of the range belongs to
    /// the endpoint, not here: <c>from &gt; to</c> is a user error that must be answered with a 400 and a
    /// message rather than an empty result (contract <c>reports-read-surface.md</c>), and a repository
    /// cannot make that distinction visible.
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
    /// <remarks>
    /// A <see cref="Sow"/> query, not a <see cref="ClientAssignment"/> one — unlike the assignment-start
    /// lookup above, which asks when an assignment began, this asks when an extension period began, and
    /// an assignment can carry any number of SOWs. Filters on <c>SowType == SowType.SowExtension</c>
    /// (<see cref="Sow"/>'s remarks: the type replaced the ERD's <c>is_extension</c> boolean).
    /// </remarks>
    /// <param name="from">First day of the range, inclusive.</param>
    /// <param name="to">Last day of the range, inclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SowExtensionRowDto>> GetSowExtensionsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}
