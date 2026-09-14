using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Authorization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// The destructive developer-tools surface for Compass. Mapped only when
/// <see cref="LeadingEDJE.Leap.Api.Platform.Services.DeveloperToolsGate"/> allows it.
/// </summary>
/// <remarks>
/// Three things gate a delete, and none of them is the UI: the environment opts in and is not
/// Production (<c>DeveloperToolsGate</c>, checked in <c>api/Program.cs</c> — on refusal these routes
/// are never mapped, so the surface answers 404, not 403); the caller satisfies
/// <see cref="RolePolicy.CompassSuperAdmin"/>; and the clear is a POST. Substituting
/// <c>CompassAdmin</c> is a privilege escalation, since that policy admits the read-only Compass Admin
/// role (AC-44), and no timesheet role is admitted either. The Konami code in the launcher is not a
/// security control. Mounted on the Compass application surface, never on the published
/// <c>/api/compass/v1</c> contract, because these routes exist only conditionally.
/// </remarks>
public static class CompassDeveloperToolsEndpoints
{
    /// <summary>The base path for this surface. Deliberately NOT under <c>/api/compass/v1</c>.</summary>
    public const string BasePath = "/api/compass/developer-tools";

    /// <summary>Registers the developer-tools probe and the Compass clear action.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The application, for chaining.</returns>
    public static WebApplication MapCompassDeveloperToolsEndpoints(this WebApplication app)
    {
        // Declared here rather than through CompassAdminRouteGroup: that helper exists to mount things
        // under the versioned boundary, which is exactly where these must not go. The POLICY is
        // identical to the one it applies — deliberately, since a configuration surface and a truncate
        // button warrant the same role.
        var group = app.MapGroup(BasePath)
            .WithTags("Compass Developer Tools")
            .RequireAuthorization(RolePolicy.CompassSuperAdmin);

        group.MapGet("/availability", GetAvailability)
            .WithName("GetCompassDeveloperToolsAvailability");

        group.MapPost("/clear-compass-data", ClearCompassData)
            .WithName("ClearCompassData");

        return app;
    }

    /// <summary>The capability probe.</summary>
    /// <remarks>
    /// Lets a client tell "this environment has no developer tools" (404, the group was never mapped)
    /// from "you are not allowed to use them" (403, from the group policy) without attempting a
    /// destructive call to find out.
    /// </remarks>
    private static IResult GetAvailability() =>
        Results.Ok(new DeveloperToolsAvailabilityResponse(true, ["clear-compass-data"]));

    private static async Task<IResult> ClearCompassData(
        ICompassDataResetService resetService,
        CancellationToken cancellationToken)
    {
        var cleared = await resetService.ClearAllAsync(cancellationToken);
        return Results.Ok(cleared);
    }
}
