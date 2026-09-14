namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>Supplies "today" as a business date for every Compass derivation.</summary>
/// <remarks>
/// Exists so no other file in Compass reads the clock: consumers receive a
/// <see cref="DateOnly"/> rather than fetching one, which is what makes it controllable in tests.
/// </remarks>
public interface ICompassBusinessDate
{
    /// <summary>The current date in the business timezone.</summary>
    DateOnly Today();
}
