namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>Supplies "today" as a business date for every Compass derivation.</summary>
public interface ICompassBusinessDate
{
    /// <summary>The current date in the business timezone.</summary>
    DateOnly Today();
}
