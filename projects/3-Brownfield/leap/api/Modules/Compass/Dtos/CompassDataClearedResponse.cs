namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// What a Compass data clear actually removed, reported per table.
/// </summary>
/// <remarks>
/// The counts are measured, not assumed: <c>TRUNCATE</c> reports nothing, so each table is counted
/// immediately before it is emptied, in one transaction holding the truncate's own
/// <c>ACCESS EXCLUSIVE</c> lock, so a concurrent insert cannot be destroyed uncounted. The caller has
/// no way to check afterwards, because the evidence is what was destroyed. The two Compass lookup
/// tables (<c>employee_type</c>, <c>invoice_frequency_type</c>) are deliberately absent: they are
/// reference data and FK parents of tables this clears, so they are never touched. See
/// <c>ICompassDataResetService</c>.
/// </remarks>
/// <param name="BillableTimeCategories">Rows removed from <c>compass.billable_time_category</c>.</param>
/// <param name="Sows">Rows removed from <c>compass.sow</c>.</param>
/// <param name="ClientAssignments">Rows removed from <c>compass.client_assignment</c>.</param>
/// <param name="Employees">Rows removed from <c>compass.employee</c>.</param>
/// <param name="Clients">Rows removed from <c>compass.client</c>.</param>
/// <param name="TotalRowsCleared">The sum of the five counts above.</param>
/// <param name="ClearedAtUtc">When the clear committed, in UTC.</param>
public sealed record CompassDataClearedResponse(
    int BillableTimeCategories,
    int Sows,
    int ClientAssignments,
    int Employees,
    int Clients,
    int TotalRowsCleared,
    DateTime ClearedAtUtc);
