namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>
/// A Super Admin-managed Compass lookup: a named, retirable reference value other records classify
/// themselves by.
/// </summary>
public interface ICompassLookup
{
    /// <summary>Primary key.</summary>
    int Id { get; set; }

    /// <summary>The unique display name, e.g. "Full Time" or "Monthly".</summary>
    string TypeName { get; set; }

    /// <summary>Whether the value may be selected on new or edited records.</summary>
    bool IsActive { get; set; }
}
