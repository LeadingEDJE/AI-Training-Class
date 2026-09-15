using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Translates the exclusion-constraint and CHECK violations an assignment or SOW write can lose a race
/// to into the same rejection value the service's own pre-check returns.
/// </summary>
public static class CompassWriteFailure
{
    /// <summary>Postgres <c>exclusion_violation</c> — the SOW non-overlap constraint.</summary>
    public const string ExclusionViolationSqlState = "23P01";

    /// <summary>
    /// Postgres <c>check_violation</c> — date order, rate-increase-requires-extension, or the SOW type
    /// allowlist.
    /// </summary>
    public const string CheckViolationSqlState = "23514";

    /// <summary>Whether this exception carries a SQLSTATE this translator can automatically retry.</summary>
    /// <param name="exception">The exception a save just threw.</param>
    public static bool IsTranslatable(DbUpdateException exception) =>
        SqlStateOf(exception) is ExclusionViolationSqlState or CheckViolationSqlState;

    /// <summary>
    /// The rejection message for a translatable exception. Only meaningful when
    /// <see cref="IsTranslatable"/> returned <c>true</c> for the same exception.
    /// </summary>
    /// <param name="exception">The exception a save just threw.</param>
    /// <exception cref="ArgumentException">
    /// The exception does not carry a translatable SQLSTATE — call <see cref="IsTranslatable"/> first.
    /// </exception>
    public static string MessageFor(DbUpdateException exception) => SqlStateOf(exception) switch
    {
        ExclusionViolationSqlState =>
            "This period overlaps an existing one for this assignment.",
        CheckViolationSqlState =>
            "The end date must be on or after the start date.",
        _ => throw new ArgumentException(
            "This exception does not carry a translatable SQLSTATE. Call IsTranslatable first.",
            nameof(exception)),
    };

    /// <summary>Walks the outer-exception chain for the Postgres SQLSTATE, stopping at the first match.</summary>
    private static string? SqlStateOf(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres.SqlState;
            }
        }

        return null;
    }
}
