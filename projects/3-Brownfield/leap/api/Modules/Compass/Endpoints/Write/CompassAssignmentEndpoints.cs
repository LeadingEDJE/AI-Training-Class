using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Write;

/// <summary>
/// The assignment write surface. <c>DELETE</c> is a soft archive, folded onto the same
/// <see cref="RolePolicy.CompassOps"/> group as the other writes.
/// </summary>
public static class CompassAssignmentEndpoints
{
    /// <summary>Maps the assignment write surface under <c>/api/compass/assignments</c>.</summary>
    public static RouteGroupBuilder MapCompassAssignmentEndpoints(this WebApplication app)
    {
        var group = app.MapCompassWriteGroup("assignments", RolePolicy.CompassOps);

        group.MapGet("/", GetAll);
        group.MapGet("/pickers/clients", GetClientPickers);
        group.MapGet("/pickers/edjers", GetEdjerPickers);

        // Mounted on the elevated group below, since Compass Admin is the role that reviews
        // deactivation blockers before Ops acts on them.
        group.MapGet("/blockers/{employeeId:int}", GetDeactivationBlockers);

        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);

        var elevated = app.MapGroup($"{CompassWriteRouteGroup.BasePath}/assignments")
            .WithTags("Compass")
            .RequireAuthorization(RolePolicy.CompassElevated);

        elevated.MapGet("/{id:int}", GetById);

        elevated.MapGet("/pickers/invoice-frequency-types", GetInvoiceFrequencyTypes);

        var superAdminOnly = app.MapGroup($"{CompassWriteRouteGroup.BasePath}/assignments")
            .WithTags("Compass")
            .RequireAuthorization(RolePolicy.CompassSuperAdmin);

        superAdminOnly.MapDelete("/{id:int}", Delete);

        return group;
    }

    private static async Task<IResult> GetAll(ICompassAssignmentService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.GetAllAsync(cancellationToken));

    private static async Task<IResult> GetClientPickers(
        ICompassAssignmentService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.GetClientPickersAsync(cancellationToken));

    private static async Task<IResult> GetEdjerPickers(
        ICompassAssignmentService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.GetEdjerPickersAsync(cancellationToken));

    private static async Task<IResult> GetDeactivationBlockers(
        int employeeId,
        IEdjerDeactivationGuard guard,
        CancellationToken cancellationToken)
    {
        var verdict = await guard.EvaluateAsync(employeeId, cancellationToken);
        return verdict.Status == EdjerDeactivationStatus.NotFound
            ? Results.NotFound()
            : Results.Ok(verdict);
    }

    private static async Task<IResult> GetInvoiceFrequencyTypes(
        ICompassLookupService lookups,
        CancellationToken cancellationToken,
        bool activeOnly = false
    ) => Results.Ok(await lookups.GetInvoiceFrequencyTypesAsync(activeOnly, cancellationToken));

    private static async Task<IResult> GetById(
        int id, ICompassAssignmentService service, CancellationToken cancellationToken)
    {
        var dto = await service.GetByIdAsync(id, cancellationToken);
        return dto is not null ? Results.Ok(dto) : Results.NotFound();
    }

    private static async Task<IResult> Create(
        CreateAssignmentRequest request,
        HttpContext httpContext,
        ICompassAssignmentService service,
        CancellationToken cancellationToken)
    {
        if (!CompassLegacyProvenance.MaySet(httpContext.User, request.LegacyTpsId))
        {
            return CompassLegacyProvenance.Refusal();
        }

        var result = await service.CreateAsync(request, cancellationToken);
        return result.Status == AdminMutationStatus.Success
            ? Results.Created($"/api/compass/assignments/{result.Value!.Id}", result.Value)
            : result.Status.ToErrorResult(result.Error);
    }

    private static async Task<IResult> Update(
        int id,
        UpdateAssignmentRequest request,
        ICompassAssignmentService service,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, request, cancellationToken);
        return result.Status == AdminMutationStatus.Success
            ? Results.Ok(result.Value)
            : result.Status.ToErrorResult(result.Error);
    }

    private static async Task<IResult> Delete(
        int id,
        ICompassAssignmentService service,
        CancellationToken cancellationToken)
    {
        var status = await service.DeleteAsync(id, cancellationToken);
        return status == AdminMutationStatus.Success
            ? Results.NoContent()
            : status.ToErrorResult(null);
    }
}
