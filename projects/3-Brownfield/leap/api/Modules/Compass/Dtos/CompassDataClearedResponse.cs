namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// What a Compass data clear actually removed, reported per table.
/// </summary>
/// <remarks>
/// The counts are estimated after the fact rather than counted directly, since <c>TRUNCATE</c> reports
/// nothing on its own. The two Compass lookup tables are included in these totals too, since a full
/// data reset also needs to reseed them. See the data-reset runbook for the full table list.
/// </remarks>
/// <param name="BillableTimeCategories">Rows removed from <c>compass.billable_time_category</c>.</param>
/// <param name="Sows">Rows removed from <c>compass.sow</c>.</param>
/// <param name="ClientAssignments">Rows removed from <c>compass.client_assignment</c>.</param>
/// <param name="Employees">Rows removed from <c>compass.employee</c>.</param>
/// <param name="EmployeeSkills">Rows removed from <c>compass.employee_skill</c>.</param>
/// <param name="Clients">Rows removed from <c>compass.client</c>.</param>
/// <param name="TotalRowsCleared">The sum of the six counts above.</param>
/// <param name="ClearedAtUtc">When the clear committed, in UTC.</param>
public sealed record CompassDataClearedResponse(
    int BillableTimeCategories,
    int Sows,
    int ClientAssignments,
    int Employees,
    int EmployeeSkills,
    int Clients,
    int TotalRowsCleared,
    DateTime ClearedAtUtc);
