using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Read;

/// <summary>The Sales Dashboard read surface — AC-36, AC-37.</summary>
/// <remarks>
/// Part of the Compass application read surface (ADR-008), not the published boundary — no <c>/v1</c>
/// segment (FR-031). The namespace is a build constraint:
/// <c>CompassTransportContractTests.EveryBoundaryHandlerPayload_IsAlsoAnIDirectoryPayload</c> scans the
/// exact namespace <c>…Modules.Compass.Endpoints</c> and requires every payload there to be one
/// <c>IDirectory</c> also returns, which <see cref="SalesDashboardDto"/> is not. Both routes require
/// <see cref="RolePolicy.CompassReporting"/> — Sales, Ops, or the Compass root. Compass Admin does not
/// satisfy it (FR-019), and that exclusion is enforced at the server, not by hiding a nav link.
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

    /// <summary>The drill-down for one tile.</summary>
    /// <remarks>
    /// An unrecognised category is <c>404</c>, never a silent fallback (contract
    /// <c>dashboard-read-surface.md</c>): a wrong fallback would show one category's data under
    /// another's heading.
    /// </remarks>
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
