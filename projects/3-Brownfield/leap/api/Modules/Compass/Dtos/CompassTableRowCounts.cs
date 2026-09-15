namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// How many rows each of the five Compass operational tables holds.
/// </summary>
/// <param name="BillableTimeCategories">Rows in <c>compass.billable_time_category</c>.</param>
/// <param name="Sows">Rows in <c>compass.sow</c>.</param>
/// <param name="ClientAssignments">Rows in <c>compass.client_assignment</c>.</param>
/// <param name="Employees">Rows in <c>compass.employee</c>.</param>
/// <param name="Clients">Rows in <c>compass.client</c>.</param>
public sealed record CompassTableRowCounts(
    int BillableTimeCategories,
    int Sows,
    int ClientAssignments,
    int Employees,
    int Clients)
{
    /// <summary>The five counts added together.</summary>
    public int Total => BillableTimeCategories + Sows + ClientAssignments + Employees + Clients;
}
