using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>The Compass reports' business layer (AC-38 to AC-40, feature 007 US2-US4).</summary>
/// <remarks>
/// Each report resolves the business date independently, so the reports and the dashboard tiles may
/// show slightly different populations if run at different times, per the reporting design doc.
/// </remarks>
public interface ICompassReportReadService
{
    /// <summary>Returns the Availability Report's three sections and the date they share (FR-009).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AvailabilityReportDto> GetAvailabilityAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the Client Assignment Duration report: one row per EDJEr–client pair holding at least
    /// one active assignment, longest tenure first (FR-014, FR-015, AC-39).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AssignmentDurationRowDto>> GetAssignmentDurationAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// The Assignment Start lookup (AC-40) — every assignment starting within the inclusive range.
    /// </summary>
    /// <param name="from">First day of the range, inclusive.</param>
    /// <param name="to">Last day of the range, inclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AssignmentStartRowDto>> GetAssignmentStartsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    /// <summary>
    /// The SOW Extension Report — every extension SOW whose start date falls within the
    /// inclusive range.
    /// </summary>
    /// <param name="from">First day of the range, inclusive.</param>
    /// <param name="to">Last day of the range, inclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SowExtensionRowDto>> GetSowExtensionsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}
