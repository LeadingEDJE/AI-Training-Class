namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// A Super Admin-managed Compass lookup: a named, retirable reference value other records classify
/// themselves by.
/// </summary>
/// <remarks>
/// It lives in <c>Interfaces/</c>, not at the module root beside the entities it abstracts: that is
/// where a module's own interfaces belong, and
/// <c>scripts/check-coverage.sh</c> excludes <c>api/*/Interfaces/*</c> because an interface has no
/// executable code to instrument. Implemented by <see cref="EmployeeType"/> and
/// <see cref="InvoiceFrequencyType"/>, which are structurally identical, so the repository is written
/// once rather than twice; it adds no property either entity did not already have. Deactivation, never
/// deletion: retiring a value must leave records that already reference it untouched (FR-007), so
/// every lookup carries <see cref="IsActive"/> and none has a delete path (Principle VIII).
/// </remarks>
public interface ICompassLookup
{
    /// <summary>Primary key.</summary>
    int Id { get; set; }

    /// <summary>The unique display name, e.g. "Full Time" or "Monthly".</summary>
    string TypeName { get; set; }

    /// <summary>Whether the value may be selected on new or edited records.</summary>
    bool IsActive { get; set; }
}
