using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Creates contract periods (SOWs) for the TPS migration, and reads them back to verify a load.
/// </summary>
/// <remarks>
/// Create-and-read only: the migration only creates, and it needs <c>GetAll</c>/<c>Get</c> so
/// <c>--reconcile</c> and <c>--spot-check</c> can verify what it wrote. Deliberately distinct from
/// <see cref="ICompassSowService"/>, the Ops-or-root application write surface that has a <c>PUT</c>,
/// runs the full FR-019/FR-020 validation, and refuses <see cref="SowType.LegacyMigrated"/> on input
/// regardless of caller (FR-016). This one is the migration's create path and the only route to the
/// <c>LegacyMigrated</c> validation bypass, gated on principal identity (Principle VIII). Keep them
/// separate: folding that bypass into the Ops-gated service would put a permanent constraint bypass
/// inside the surface every Compass Super Admin reaches from a browser.
/// </remarks>
public interface ICompassSowMigrationService
{
    /// <summary>Creates a contract period.</summary>
    /// <param name="request">The period to create.</param>
    /// <param name="isMigrationPrincipal">
    /// Whether the caller is the migration principal. Gates the <c>LegacyMigrated</c> validation
    /// bypass (Principle VIII).
    /// <para>
    /// Passed in rather than read from the user context, deliberately. The decision belongs to
    /// the composition root, which knows the authenticated principal; threading it as a parameter
    /// keeps the service unit-testable without an HTTP context and makes the gate visible at every
    /// call site instead of hidden inside an ambient lookup.
    /// </para>
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
