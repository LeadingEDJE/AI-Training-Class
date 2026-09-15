namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>The developer-tools capability probe answer.</summary>
/// <param name="Available">Always <c>true</c> when this payload is returned at all.</param>
/// <param name="Tools">Stable identifiers for the tools this build offers, e.g. <c>clear-compass-data</c>.</param>
public sealed record DeveloperToolsAvailabilityResponse(bool Available, IReadOnlyList<string> Tools);
