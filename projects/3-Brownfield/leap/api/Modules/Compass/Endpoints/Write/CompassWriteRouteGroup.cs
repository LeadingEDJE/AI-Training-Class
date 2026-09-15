using LeadingEDJE.Leap.Api.Platform.Authorization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Write;

/// <summary>
/// The shared mount point for every Compass assignment/SOW write surface, mounted under the versioned
/// <c>/api/compass/v1</c> boundary alongside the admin routes.
/// </summary>
public static class CompassWriteRouteGroup
{
    /// <summary>The Compass application surface's base path — not the versioned boundary.</summary>
    public const string BasePath = "/api/compass";

    /// <summary>
    /// Maps a Compass write resource under the application surface, gated by the supplied policy.
    /// </summary>
    public static RouteGroupBuilder MapCompassWriteGroup(
        this WebApplication app,
        string resource,
        string policy) =>
        app.MapGroup($"{BasePath}/{resource}")
            .WithTags("Compass")
            .RequireAuthorization(policy);
}
