using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>Reads the legacy-to-Compass identifier map back out of the migrated tables.</summary>
public interface ICompassMigrationProvenanceRepository
{
    /// <summary>Every migrated record's source identifier and the id Compass assigned it.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per record carrying provenance, across all five migrated tables.</returns>
    Task<IReadOnlyList<CompassMigrationProvenanceEntry>> GetAllAsync(
        CancellationToken cancellationToken
    );
}
