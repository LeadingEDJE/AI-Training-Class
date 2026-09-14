using LeadingEDJE.Leap.Api.Platform.Authorization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Write;

/// <summary>
/// The shared mount point for every Compass assignment/SOW write surface.
/// </summary>
/// <remarks>
/// Policy-parameterised, unlike
/// <see cref="LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin.CompassAdminRouteGroup"/>, which
/// hardcodes <see cref="RolePolicy.CompassSuperAdmin"/>: these writes need
/// <see cref="RolePolicy.CompassOps"/> and a later write surface may need another. Authorization stays
/// declared once on the group, never in a handler. Mounted on the Compass application surface, never
/// <c>/api/compass/v1</c>: ADR-008 rule 1 forbids serving a viewer-dependent payload from the published
/// boundary, and SOW payloads are viewer-dependent (FR-018, the rate-increase indicator and note being
/// elevated-only). These routes sit alongside the Compass read routes instead.
/// </remarks>
public static class CompassWriteRouteGroup
{
    /// <summary>The Compass application surface's base path — not the versioned boundary.</summary>
    public const string BasePath = "/api/compass";

    /// <summary>
    /// Maps a Compass write resource under the application surface, gated by the supplied policy.
    /// </summary>
    /// <param name="app">The application to map onto.</param>
    /// <param name="resource">The kebab-case resource segment, e.g. <c>assignments</c>.</param>
    /// <param name="policy">
    /// The <see cref="RolePolicy"/> constant this resource's writes require. Never
    /// <see cref="RolePolicy.CompassAdmin"/> for a write route — that policy is also satisfied by the
    /// READ-ONLY "Compass Admin" role (AC-44), so attaching it here would be privilege escalation.
    /// </param>
    /// <returns>The route group, for the caller to hang its routes on.</returns>
    public static RouteGroupBuilder MapCompassWriteGroup(
        this WebApplication app,
        string resource,
        string policy) =>
        app.MapGroup($"{BasePath}/{resource}")
            .WithTags("Compass")
            .RequireAuthorization(policy);
}
