using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Reads and empties the five Compass operational tables.
/// </summary>
/// <remarks>
/// The data access lives behind this interface because Compass's boundary requires it: a Compass
/// endpoint or service may not name a <c>DbContext</c> at all, and
/// <c>CompassBoundaryTests.RuleTwo_CompassEndpointsAndServices_ReferenceNoDataContext</c> fails the
/// build if one does. The service above owns the audit record and the reporting; this owns the SQL.
/// Relational only, and that is not an implementation detail: <see cref="ClearAsync"/> issues a real
/// <c>TRUNCATE</c>, so there is no in-memory equivalent — the unit suite exercises the service against
/// a hand-written double of this interface, and the implementation is proven against PostgreSQL in
/// <c>tests/integration</c>.
/// </remarks>
public interface ICompassDataResetRepository
{
    /// <summary>
    /// Empties all five tables, restarts their identity sequences, and returns what was removed.
    /// Irreversible.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-table counts, measured under the same lock that performs the clear.</returns>
    /// <remarks>
    /// Counting and clearing are one operation here rather than two calls, on purpose. The counts are
    /// the only surviving record of what the clear destroyed, so they have to be taken while nothing
    /// else can write — which means the caller must not be able to take them separately. Do not split
    /// this into a <c>CountAsync</c> plus a clear: the ordering can be right while the atomicity is
    /// wrong, and the difference is invisible at the call site. The implementation's remarks carry the
    /// mechanism.
    /// </remarks>
    Task<CompassTableRowCounts> ClearAsync(CancellationToken cancellationToken);
}
