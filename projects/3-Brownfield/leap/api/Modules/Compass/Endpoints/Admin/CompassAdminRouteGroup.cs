using LeadingEDJE.Leap.Api.Platform.Authorization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// The shared mount point and policy for every Compass configuration surface. Uses
/// <see cref="RolePolicy.CompassAdmin"/> so that both the read-only Compass Admin role and the
/// Compass root can reach these routes.
/// </summary>
public static class CompassAdminRouteGroup
{
    /// <summary>The versioned path every Compass configuration surface hangs off.</summary>
    public const string BasePath = "/api/compass/v1/admin";

    /// <summary>Maps a Compass configuration resource under the versioned admin path.</summary>
    public static RouteGroupBuilder MapCompassAdminGroup(this WebApplication app, string resource) =>
        app.MapGroup($"{BasePath}/{resource}")
            .WithTags("Compass Admin")
            .RequireAuthorization(RolePolicy.CompassSuperAdmin);
}
