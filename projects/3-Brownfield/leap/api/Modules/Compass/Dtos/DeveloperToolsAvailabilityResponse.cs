namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos;

/// <summary>
/// The developer-tools capability probe answer. A 200 response IS the answer — reaching this payload
/// means the environment allows developer tools and the caller is authorised to use them.
/// </summary>
/// <remarks>
/// The shell distinguishes three outcomes by status code alone: <c>404</c> (no developer tools in this
/// environment), <c>403</c> (present, caller not authorised — the button renders disabled) and
/// <c>200</c> (enabled). The body exists so the surface can grow a second tool without the client
/// having to guess what is available.
/// </remarks>
/// <param name="Available">Always <c>true</c> when this payload is returned at all.</param>
/// <param name="Tools">Stable identifiers for the tools this build offers, e.g. <c>clear-compass-data</c>.</param>
public sealed record DeveloperToolsAvailabilityResponse(bool Available, IReadOnlyList<string> Tools);
