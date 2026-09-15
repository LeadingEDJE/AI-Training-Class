using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Authorization;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// The destructive developer-tools surface for Compass. Mapped only when
/// <see cref="LeadingEDJE.Leap.Api.Platform.Services.DeveloperToolsGate"/> allows it. Mounted under
/// the published <c>/api/compass/v1</c> contract alongside the other admin surfaces.
/// </summary>
public static class CompassDeveloperToolsEndpoints
{
    /// <summary>The versioned base path for this surface.</summary>
    public const string BasePath = "/api/compass/developer-tools";

    /// <summary>Registers the developer-tools probe and the Compass clear action.</summary>
    public static WebApplication MapCompassDeveloperToolsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(BasePath)
            .WithTags("Compass Developer Tools")
            .RequireAuthorization(RolePolicy.CompassSuperAdmin);

        group.MapGet("/availability", GetAvailability)
            .WithName("GetCompassDeveloperToolsAvailability");

        group.MapPost("/clear-compass-data", ClearCompassData)
            .WithName("ClearCompassData");

        return app;
    }

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
