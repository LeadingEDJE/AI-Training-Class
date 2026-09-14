using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Write;

/// <summary>
/// The SOW write surface: <c>contracts/sow-write-surface.md</c> §1.
/// </summary>
/// <remarks>
/// Nested under the assignment (<c>ClientAssignmentId</c> is the overlap scope, FR-019) and mounted
/// on the SAME <see cref="CompassWriteRouteGroup"/> as the assignment surface, under
/// <see cref="RolePolicy.CompassOps"/>. Unlike <see cref="CompassAssignmentEndpoints"/> there is no
/// widened read exception: the whole SOW group stays Ops-or-root, reads included. <c>DELETE</c> is a
/// TRUE permanent delete on its OWN group under <see cref="RolePolicy.CompassSuperAdmin"/>, never
/// <c>CompassOps</c>, removing ONE period without touching its assignment or siblings. Reasoning:
/// <see cref="ICompassSowService.DeleteAsync"/> and the remark on
/// <see cref="CompassAssignmentEndpoints"/>.
/// </remarks>
public static class CompassSowEndpoints
{
    /// <summary>Maps the SOW write surface under <c>/api/compass/assignments/{assignmentId}/sows</c>.</summary>
    public static RouteGroupBuilder MapCompassSowEndpoints(this WebApplication app)
    {
        var group = app.MapCompassWriteGroup("assignments/{assignmentId:int}/sows", RolePolicy.CompassOps);

        group.MapGet("/", GetByAssignmentId);
        group.MapPost("/", Create);
        group.MapPut("/{sowId:int}", Update);

        // Issue #593 — a TRUE delete, scoped to the Compass root alone. A separate MapGroup on the
        // identical base path, matching CompassAssignmentEndpoints' `superAdminOnly` group: the
        // policy here is NARROWER than the Ops write group's, so CompassOps must not reach it.
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

    /// <summary>Issue #593 — a true delete of ONE contract period.</summary>
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
