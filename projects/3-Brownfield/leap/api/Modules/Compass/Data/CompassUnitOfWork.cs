using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data;

/// <summary>
/// The Compass persistence boundary, over the single application context.
/// </summary>
/// <remarks>
/// Lives under <c>Data/</c> because that is where the module may name the context:
/// <c>CompassBoundaryTests</c> fails the build for any file under <c>Endpoints/</c> or
/// <c>Services/</c> containing the token <c>DbContext</c>. See <see cref="ICompassUnitOfWork"/> for
/// why the seam exists at all.
///
/// Still one context, one <c>__EFMigrationsHistory</c> and one save boundary: this adds an interface
/// in front of the existing context, not a second one. It also translates a lost uniqueness race so
/// the module's services can answer 409 without naming a provider type — here rather than in the
/// service because of the fence above.
/// </remarks>
public class CompassUnitOfWork(LeapDbContext context) : ICompassUnitOfWork
{
    /// <summary>Postgres <c>unique_violation</c>.</summary>
    private const string UniqueViolationSqlState = "23505";

    /// <summary>Set while <see cref="ExecuteAtomicallyAsync"/> is running an operation.</summary>
    private bool _atomicScopeActive;

    /// <summary>The transaction this unit of work opened for the active scope, if it opened one.</summary>
    private IDbContextTransaction? _scopeTransaction;

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        // The transaction opens HERE, on the first save inside an atomic scope — not when the scope
        // itself opens. Most refusals (invalid input, not found, and the uniqueness pre-checks) return
        // before staging anything, and those paths must keep costing nothing: opening eagerly billed
        // every validation-only rejection a BEGIN and a ROLLBACK round trip it never needed.
        if (_atomicScopeActive && _scopeTransaction is null && context.Database.CurrentTransaction is null)
        {
            _scopeTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Detach what the database rejected BEFORE rethrowing. The context is request-scoped and
            // shared with whatever else runs in this request, so an insert left in Added state — or a
            // rejected rename left Modified — would be retried by the next SaveChanges and fail it too,
            // turning one caller's 409 into an unrelated caller's 500. UserRoleService documents the
            // same hazard for the same reason.
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            throw new CompassDuplicateKeyException(
                "A concurrent write already committed a value with this name.",
                ex,
                UniqueViolationIn(ex)?.ConstraintName
            );
        }
    }

    /// <inheritdoc />
    public async Task<T> ExecuteAtomicallyAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<T, bool> commitWhen,
        CancellationToken cancellationToken)
    {
        // Joining an ambient transaction rather than nesting: Npgsql rejects a nested BeginTransaction,
        // and a caller that opened one is composing several operations into a larger unit deliberately.
        // The same applies to a nested scope — the outer one owns commit and rollback.
        if (context.Database.CurrentTransaction is not null || _atomicScopeActive)
        {
            return await operation(cancellationToken);
        }

        _atomicScopeActive = true;
        try
        {
            var result = await operation(cancellationToken);

            // Nothing was ever saved, so no transaction was opened: a refusal that returned before
            // staging anything has nothing to commit and nothing to roll back.
            if (_scopeTransaction is null)
            {
                return result;
            }

            if (commitWhen(result))
            {
                await _scopeTransaction.CommitAsync(cancellationToken);
            }
            else
            {
                // This refusal got here through a FAILED statement — a lost uniqueness race — which
                // Postgres treats as aborting the whole transaction. COMMIT would throw rather than
                // quietly persist nothing, so roll back.
                await _scopeTransaction.RollbackAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            // Disposing rolls back anything still open, which is what an exception thrown out of the
            // operation must leave behind.
            if (_scopeTransaction is not null)
            {
                await _scopeTransaction.DisposeAsync();
                _scopeTransaction = null;
            }

            _atomicScopeActive = false;
        }
    }

    /// <summary>
    /// Whether the failure is a unique-index rejection rather than any other write failure.
    /// </summary>
    /// <remarks>
    /// Walks the inner-exception chain for a Postgres <c>unique_violation</c>: Npgsql surfaces it as a
    /// <see cref="PostgresException"/> and EF Core wraps that in a <see cref="DbUpdateException"/>.
    /// Matching the SQLSTATE rather than message text keeps it locale-independent. This logic exists
    /// twice, here and in <c>UserRoleService.IsDuplicateKeyViolation</c>; a third occurrence should be
    /// extracted to <c>api/Platform/</c> rather than copied again, but extracting it now would edit
    /// shared timesheet code from a Compass change.
    /// </remarks>
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        UniqueViolationIn(exception) is not null;

    /// <summary>
    /// The provider exception reporting the unique violation, so its constraint name can be carried
    /// forward. Walks the chain because EF wraps and the depth is not guaranteed.
    /// </summary>
    private static PostgresException? UniqueViolationIn(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == UniqueViolationSqlState)
            {
                return postgres;
            }
        }

        return null;
    }
}
