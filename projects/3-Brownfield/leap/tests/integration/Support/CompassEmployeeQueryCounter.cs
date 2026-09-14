using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Support;

/// <summary>
/// Counts the SQL commands issued against <c>compass.employee</c>, so a constant-read requirement can
/// be ASSERTED rather than assumed.
/// </summary>
/// <remarks>
/// <para>
/// Why a count and never a duration. Feature 018's FR-007 requires OOTO's employee-list read to
/// perform a number of directory reads independent of the result size — one, not one per employee. A
/// timing assertion cannot distinguish one query from ninety-nine on a fast machine against a local
/// container, so it would pass while the N+1 shipped. That is the failure mode this type exists to
/// remove.
/// </para>
/// <para>
/// Why an interceptor rather than reading the statement log. The quickstart's
/// <c>log_statement = 'all'</c> plus <c>grep -c</c> works at a terminal, but it is not reproducible
/// inside a test run and not available in CI. An interceptor sees exactly the commands EF issued for
/// the request under test.
/// </para>
/// <para>
/// Matching on the schema-qualified table name. Compass entities are mapped explicitly through
/// <c>CompassEntityConfiguration</c>, which is the only <c>ToTable</c> call site in the repository, so
/// every generated statement against them names <c>compass.employee</c> — schema-qualified, because
/// the application connection's <c>search_path</c> is pinned to <c>public</c>. It is a text match on
/// generated SQL, which is coarse; that is acceptable because the assertion is a magnitude (one versus
/// many), not an exact statement shape.
/// </para>
/// </remarks>
public sealed class CompassEmployeeQueryCounter : DbCommandInterceptor
{
    private int _count;

    /// <summary>Commands issued against <c>compass.employee</c> since the last <see cref="Reset"/>.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Zeroes the counter. Call immediately before the Act, never after.</summary>
    public void Reset() => Volatile.Write(ref _count, 0);

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Record(DbCommand command)
    {
        if (command.CommandText.Contains("compass.employee", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _count);
        }
    }
}
