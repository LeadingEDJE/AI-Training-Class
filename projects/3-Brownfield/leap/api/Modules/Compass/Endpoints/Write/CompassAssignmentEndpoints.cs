using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Dtos;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Write;

/// <summary>
/// The assignment write surface: <c>contracts/assignment-write-surface.md</c> §1.
/// </summary>
/// <remarks>
/// Writes, <c>GetAll</c> and the pickers sit on <see cref="CompassWriteRouteGroup"/> under
/// <see cref="RolePolicy.CompassOps"/>, declared once on the group and never in a handler.
/// <c>DELETE</c> is a TRUE permanent delete — the module's ONE exception to ADR-007/Principle VIII,
/// for data entered in error; own group, <see cref="RolePolicy.CompassSuperAdmin"/>, never
/// <c>CompassOps</c>, and do not fold it onto the Ops group. It cascades to every SOW in one commit
/// (<see cref="ICompassAssignmentService.DeleteAsync"/>). <c>GetById</c> also has its own group under
/// <see cref="RolePolicy.CompassElevated"/>, since AC-16/FR-025 grant Admin and Sales that affordance.
/// Two <c>MapGroup</c> calls on one base path is intentional: routes resolve by method and template.
/// </remarks>
public static class CompassAssignmentEndpoints
{
    /// <summary>Maps the assignment write surface under <c>/api/compass/assignments</c>.</summary>
    public static RouteGroupBuilder MapCompassAssignmentEndpoints(this WebApplication app)
    {
        var group = app.MapCompassWriteGroup("assignments", RolePolicy.CompassOps);

        group.MapGet("/", GetAll);
        // Literal paths registered ahead of the parameterised sibling below, matching the
        // adminEdjerNewRoute/adminEdjerRoute convention elsewhere in Compass — ASP.NET Core's router
        // would resolve these correctly either way, but this keeps the ordering self-documenting.
        group.MapGet("/pickers/clients", GetClientPickers);
        group.MapGet("/pickers/edjers", GetEdjerPickers);

        // The AC-19 deactivation precondition, asked before the toggle rather than discovered as
        // a 422 after it. Mounted on the Ops write group and not the more permissive `elevated` group
        // below: Ops is the role that end-dates assignments, so Ops is who can act on the answer, and
        // Compass Admin is read-only (AC-44) and can never make the decision this serves.
        group.MapGet("/blockers/{employeeId:int}", GetDeactivationBlockers);

        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);

        // A separate, more permissive read: AC-16/FR-025 grant Admin and Sales this affordance too,
        // which `CompassOps` above does not admit.
        var elevated = app.MapGroup($"{CompassWriteRouteGroup.BasePath}/assignments")
            .WithTags("Compass")
            .RequireAuthorization(RolePolicy.CompassElevated);

        elevated.MapGet("/{id:int}", GetById);

        // The cadences the override selector offers. Elevated rather than Ops, and deliberately
        // not the admin lookup route, which is gated at the Compass root because it creates and updates
        // cadences: a 403 there renders as an empty list rather than an error, so the selector silently
        // offers "use the client default" alone. Elevated also matches GetById — the read-only view
        // names the stored override from this list, or shows an active cadence as "Retired cadence".
        elevated.MapGet("/pickers/invoice-frequency-types", GetInvoiceFrequencyTypes);

        // Issue #593 — a TRUE delete, scoped to the Compass root alone. A separate MapGroup, exactly
        // like `elevated` above, because the policy here is NARROWER than the Ops write group's, not
        // wider — CompassOps must not be able to reach this route.
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

    /// <summary>
    /// Whether this EDJEr may be deactivated, and if not, which assignments block it (FR-040, FR-041).
    /// </summary>
    /// <remarks>
    /// A read, and only a read. FR-042 forbids auto-ending an assignment under any circumstance — an
    /// engagement's true end date frequently differs from the date somebody attempted a deactivation —
    /// so this route reports and the operator acts. There is no companion "end them all" action, and
    /// adding one would be the requirement's exact inverse. The intended second caller is the EDJEr
    /// edit form, which is unbuilt: it would call this before offering the active toggle, so the
    /// operator sees what to end up front. Until it exists the guard is reachable only here and
    /// through the EDJEr write path.
    /// </remarks>
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
        // Provenance is gated on principal IDENTITY, not on the Compass root role the migration
        // principal shares with every Super Admin. Refused rather than dropped: a silent discard
        // would return 201 to someone who believes they recorded where this record came from.
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

    /// <summary>Issue #593 — a true delete, cascading to every SOW under this assignment.</summary>
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
