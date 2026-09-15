using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Reads and empties the five Compass operational tables.
/// </summary>
public interface ICompassDataResetRepository
{
    /// <summary>
    /// Empties all five tables, restarts their identity sequences, and returns what was removed.
    /// Irreversible.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-table counts, measured under the same lock that performs the clear.</returns>
    /// <remarks>
    /// Counts the rows first, then clears the tables in a separate step described in the
    /// data-reset runbook.
    /// </remarks>
    Task<CompassTableRowCounts> ClearAsync(CancellationToken cancellationToken);
}
