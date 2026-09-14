using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// Empties the Compass operational tables. Destructive, irreversible, and reachable only from the
/// environment-gated developer-tools surface.
/// </summary>
/// <remarks>
/// Five tables, and the two that are missing are missing on purpose: this clears
/// <c>billable_time_category</c>, <c>sow</c>, <c>client_assignment</c>, <c>employee</c> and
/// <c>client</c>, and never <c>employee_type</c> or <c>invoice_frequency_type</c>, which are reference
/// data and FK parents of tables this does clear — emptying them would leave Compass unable to accept
/// a new EDJEr or client. No reseed: repopulating is a separate, deliberate act
/// (<c>StartupTasks:SeedCompassDirectory</c>, or a TPS migration run). This is not a repository and
/// deliberately owns its own transaction, because the atomic unit is all five tables together; a
/// partial clear leaves orphan-shaped data that satisfies no foreign key.
/// </remarks>
public interface ICompassDataResetService
{
    /// <summary>
    /// Counts and then empties the five Compass operational tables in one locked transaction, resets
    /// their identity sequences, and records one audit entry naming the caller and the row counts.
    /// </summary>
    /// <remarks>
    /// The audit entry is written after that transaction commits and is deliberately not part of it —
    /// see the implementation. The rows are gone either way by then, so a failed audit write must not
    /// tell the caller the clear did not happen.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-table row counts measured immediately before the tables were emptied.</returns>
    Task<CompassDataClearedResponse> ClearAllAsync(CancellationToken cancellationToken);
}
