using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Write;

/// <summary>
/// The SOW write surface, sharing its authorization policy with <see cref="CompassAssignmentEndpoints"/>.
/// </summary>
public static class CompassSowEndpoints
{
    /// <summary>Maps the SOW write surface under <c>/api/compass/assignments/{assignmentId}/sows</c>.</summary>
    public static RouteGroupBuilder MapCompassSowEndpoints(this WebApplication app)
    {
        var group = app.MapCompassWriteGroup("assignments/{assignmentId:int}/sows", RolePolicy.CompassOps);

        group.MapGet("/", GetByAssignmentId);
        group.MapPost("/", Create);
        group.MapPut("/{sowId:int}", Update);

        // A separate MapGroup so that CompassOps, which is broader, can also reach this route.
        var superAdminOnly = app.MapGroup($"{CompassWriteRouteGroup.BasePath}/assignments/{{assignmentId:int}}/sows")
            .WithTags("Compass")
            .RequireAuthorization(RolePolicy.CompassSuperAdmin);

        superAdminOnly.MapDelete("/{sowId:int}", Delete);

        return group;
    }

    private static async Task<IResult> GetByAssignmentId(
        int assignmentId, ICompassSowService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.GetByAssignmentIdAsync(assignmentId, cancellationToken));

    private static async Task<IResult> Create(
        int assignmentId,
        CreateSowRequest request,
        ICompassSowService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(assignmentId, request, cancellationToken);
        return result.Status == AdminMutationStatus.Success
            ? Results.Created(
                $"/api/compass/assignments/{assignmentId}/sows/{result.Value!.Id}", result.Value)
            : result.Status.ToErrorResult(result.Error);
    }

    private static async Task<IResult> Update(
        int assignmentId,
        int sowId,
        UpdateSowRequest request,
        ICompassSowService service,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(assignmentId, sowId, request, cancellationToken);
        return result.Status == AdminMutationStatus.Success
            ? Results.Ok(result.Value)
            : result.Status.ToErrorResult(result.Error);
    }

    private static async Task<IResult> Delete(
        int assignmentId,
        int sowId,
        ICompassSowService service,
        CancellationToken cancellationToken)
    {
        var status = await service.DeleteAsync(assignmentId, sowId, cancellationToken);
        return status == AdminMutationStatus.Success
            ? Results.NoContent()
            : status.ToErrorResult(null);
    }
}
