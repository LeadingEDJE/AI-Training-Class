namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// The persistence boundary for Compass writes.
/// </summary>
/// <remarks>
/// This seam is forced by two rules. Principle III puts <c>SaveChangesAsync</c> in the service layer,
/// not repositories; rule two of <c>CompassBoundaryTests</c> fails the build if any file under the
/// module's <c>Endpoints/</c> or <c>Services/</c> folders contains the token <c>DbContext</c>. A
/// Compass service therefore cannot hold the context it is required to save, and this is the narrowest
/// thing that satisfies both — every Compass service takes it rather than inventing a second
/// mechanism. It is a transaction boundary as well, because a Compass audit entry must co-commit with
/// the change it describes (<see cref="ExecuteAtomicallyAsync"/>, fenced by
/// <c>CompassAuditAtomicityTests</c>): a create cannot name its own row until the insert assigns an id.
/// </remarks>
public interface ICompassUnitOfWork
{
    /// <summary>Persists everything staged on the current unit of work.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs one logical write — its change and its audit entry — so the two commit together or not at
    /// all (FR-043, SC-007).
    /// </summary>
    /// <remarks>
    /// <paramref name="commitWhen"/> exists because of Postgres, not for flexibility. A refused write
    /// reaches its result by way of a failed statement: the uniqueness pre-checks are check-then-act, so
    /// a lost race surfaces as a constraint violation that <see cref="SaveChangesAsync"/> translates
    /// into a duplicate result. That failed statement aborts the enclosing transaction, after which
    /// Postgres rejects everything but <c>ROLLBACK</c>, so committing after a non-success result throws
    /// rather than merely persisting nothing. The predicate keeps the decision with the caller, the only
    /// place that knows what its own result type calls success. An operation already inside a
    /// transaction runs as-is: Npgsql rejects a nested <c>BeginTransaction</c>.
    /// </remarks>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="operation">The write, including its audit entry.</param>
    /// <param name="commitWhen">Whether the result represents a success worth committing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whatever <paramref name="operation"/> returned.</returns>
    Task<T> ExecuteAtomicallyAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<T, bool> commitWhen,
        CancellationToken cancellationToken);
}
