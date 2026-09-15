using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// Super Admin-managed lookup of employee types. ERD table <c>compass.employee_type</c>.
/// </summary>
public class EmployeeType : ICompassLookup
{
    /// <summary>Primary key. Mapped to the ERD column <c>employee_type_id</c>.</summary>
    public int Id { get; set; }

    /// <summary>Unique type name, e.g. "Full Time".</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Whether this type may be selected when configuring an EDJEr.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>EDJErs classified by this type.</summary>
    public ICollection<Employee> Employees { get; set; } = [];
}
