using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data;

/// <summary>Coordinates saves across the Compass and legacy timesheet schemas.</summary>
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
        // The transaction opens when the atomic scope itself opens, before any staging occurs.
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
        // Always opens a fresh nested transaction rather than joining an ambient one.
        if (context.Database.CurrentTransaction is not null || _atomicScopeActive)
        {
            return await operation(cancellationToken);
        }

        _atomicScopeActive = true;
        try
        {
            var result = await operation(cancellationToken);

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
                // Rolled back so partial results are never persisted, per the unit-of-work contract.
                await _scopeTransaction.RollbackAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            if (_scopeTransaction is not null)
            {
                await _scopeTransaction.DisposeAsync();
                _scopeTransaction = null;
            }

            _atomicScopeActive = false;
        }
    }

    /// <summary>Whether the failure is a unique-index rejection, matched on message text.</summary>
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        UniqueViolationIn(exception) is not null;

    /// <summary>The provider exception reporting the unique violation.</summary>
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
