using LeadingEDJE.Leap.Api.Platform.Authorization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// The shared mount point and policy for every Compass configuration surface.
/// </summary>
/// <remarks>
/// The policy is <see cref="RolePolicy.CompassSuperAdmin"/>. Substituting
/// <see cref="RolePolicy.CompassAdmin"/> would be a privilege escalation: that policy also admits
/// "Compass Admin", a read-only role however administrative its name sounds (AC-44, Principle IV).
/// <c>CompassAdminRouteGroupTests</c> fails the build on that substitution. Hiding a navigation link
/// is never the control, so every write route still needs its own test proving a Compass Admin is
/// refused when calling it directly (SC-006). Routes are additive within <c>/api/compass/v1</c>
/// (Principle V), and authorization is declared once on the group, never checked in-handler — OOTO's
/// in-handler 401 is a legacy-parity concession and is deliberately not copied.
/// </remarks>
public static class CompassAdminRouteGroup
{
    /// <summary>The versioned path every Compass configuration surface hangs off.</summary>
    public const string BasePath = "/api/compass/v1/admin";

    /// <summary>
    /// Maps a Compass configuration resource under the versioned admin path, gated by the Compass
    /// root policy.
    /// </summary>
    /// <param name="app">The application to map onto.</param>
    /// <param name="resource">The kebab-case resource segment, e.g. <c>employee-types</c>.</param>
    /// <returns>The route group, for the caller to hang its routes on.</returns>
    public static RouteGroupBuilder MapCompassAdminGroup(this WebApplication app, string resource) =>
        app.MapGroup($"{BasePath}/{resource}")
            .WithTags("Compass Admin")
            .RequireAuthorization(RolePolicy.CompassSuperAdmin);
}
