using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Empties the Compass operational tables. Destructive, irreversible, and reachable only from the
/// environment-gated developer-tools surface.
/// </summary>
public interface ICompassDataResetService
{
    /// <summary>
    /// Counts and then empties the five Compass operational tables in one locked transaction, resets
    /// their identity sequences, and records one audit entry naming the caller and the row counts.
    /// </summary>
    /// <remarks>
    /// The audit entry is written inside the same transaction as the clear, so the two always
    /// succeed or fail together.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-table row counts measured immediately before the tables were emptied.</returns>
    Task<CompassDataClearedResponse> ClearAllAsync(CancellationToken cancellationToken);
}
