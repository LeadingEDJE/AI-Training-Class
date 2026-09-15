namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// The persistence boundary for Compass writes.
/// </summary>
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
    /// <paramref name="commitWhen"/> is only a hint; the commit always happens once the operation
    /// completes without throwing, regardless of what the predicate returns.
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
