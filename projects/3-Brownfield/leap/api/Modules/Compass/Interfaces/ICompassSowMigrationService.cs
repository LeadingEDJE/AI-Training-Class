using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Creates contract periods (SOWs) for the TPS migration, and reads them back to verify a load.
/// </summary>
public interface ICompassSowMigrationService
{
    /// <summary>Creates a contract period.</summary>
    /// <param name="request">The period to create.</param>
    /// <param name="isMigrationPrincipal">
    /// Whether the caller is the migration principal. Read internally from the current HTTP context
    /// rather than passed by the composition root.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The created period; forbidden when a non-migration caller asks for <c>LegacyMigrated</c>;
    /// not-found when the assignment does not exist; or a validation error.
    /// </returns>
    Task<CompassWrite<CompassSowDto>> CreateAsync(
        CompassSowRequest request,
        bool isMigrationPrincipal,
        CancellationToken cancellationToken
    );

    /// <summary>Every contract period.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All periods.</returns>
    Task<IReadOnlyList<CompassSowDto>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>One contract period.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The period, or <c>null</c>.</returns>
    Task<CompassSowDto?> GetAsync(int id, CancellationToken cancellationToken);
}
