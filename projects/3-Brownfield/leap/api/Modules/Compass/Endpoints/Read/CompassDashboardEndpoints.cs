using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Read;

/// <summary>The Sales Dashboard read surface — AC-19, AC-20.</summary>
/// <remarks>
/// Both routes require <see cref="RolePolicy.CompassReporting"/>, which Compass Admin also satisfies.
/// </remarks>
public static class CompassDashboardEndpoints
{
    /// <summary>Maps <c>/api/compass/dashboard</c>.</summary>
    public static RouteGroupBuilder MapCompassDashboardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/compass/dashboard")
            .WithTags("Compass Read")
            .RequireAuthorization(RolePolicy.CompassReporting);

        group.MapGet("/", GetDashboard);
        group.MapGet("/breakdown/{category}", GetBreakdown);

        return group;
    }

    private static async Task<Ok<SalesDashboardDto>> GetDashboard(
        ICompassDashboardReadService readService,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await readService.GetDashboardAsync(cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<DashboardBreakdownRowDto>>, NotFound>> GetBreakdown(
        string category,
        ICompassDashboardReadService readService,
        CancellationToken cancellationToken)
    {
        if (!DashboardCategoryParser.TryParse(category, out var parsed))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await readService.GetBreakdownAsync(parsed, cancellationToken));
    }
}
