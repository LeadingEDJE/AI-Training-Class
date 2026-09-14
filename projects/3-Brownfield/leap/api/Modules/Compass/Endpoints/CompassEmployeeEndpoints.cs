using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints;

/// <summary>
/// The Compass module's HTTP surface — one versioned, role-gated read through the directory boundary.
/// </summary>
/// <remarks>
/// Versioned from the start; OOTO's unversioned routes are a legacy concession, not to be copied here
/// or harmonised with this one. Authorization is a group-level policy requirement, never an in-handler
/// check. The handler reaches data only through <see cref="IDirectory"/>, never the database context.
/// The <c>int</c> identifier is <c>compass.employee.employee_id</c> (D-2); sequential ids are
/// enumerable, so the policy gate does real work — do not weaken it. Changing it from <see cref="Guid"/>
/// broke <c>/api/compass/v1</c> and was amended in place ONE TIME, exempt from Principle V only because
/// ADR-005's OAuth2 client credentials are unbuilt and no external consumer can reach v1. Do not repeat
/// it: the OpenAPI diff gate now fails CI on a breaking edit (<c>docs/ops/openapi-contract-gate.md</c>).
/// </remarks>
public static class CompassEmployeeEndpoints
{
    /// <summary>
    /// Maps the Compass employee read endpoint under <c>/api/compass/v1/employees</c>, gated by the
    /// Compass admin policy.
    /// </summary>
    public static RouteGroupBuilder MapCompassEmployeeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/compass/v1/employees")
            .WithTags("Compass")
            .RequireAuthorization(RolePolicy.CompassAdmin);

        group.MapGet("/{id:int}", GetById);
        group.MapGet("/{id:int}/assignments", GetAssignments);

        return group;
    }

    /// <summary>Returns one Compass employee (EDJEr) by id.</summary>
    /// <remarks>
    /// Read family 1 of the Compass Directory boundary (FR-004). Returns 404 when <paramref name="id"/>
    /// does not match an existing employee — including a caller who cannot see the record at all,
    /// which the route's own authorization policy already refuses with 403 before this handler runs
    /// (see the type-level remarks on identifier enumerability). <c>TimeTracking</c> is present only
    /// for a Compass Super Admin caller; every other authorized tier gets the field omitted, not null.
    /// </remarks>
    private static async Task<Results<Ok<CompassEmployeeDto>, NotFound>> GetById(
        int id,
        IDirectory directory,
        CancellationToken cancellationToken)
    {
        CompassEmployeeDto? employee = await directory.GetEmployeeAsync(id, cancellationToken);
        return employee is not null
            ? TypedResults.Ok(employee)
            : TypedResults.NotFound();
    }

    /// <summary>Returns every assignment held by the given employee.</summary>
    /// <remarks>
    /// Read family 3 (spec 009 Slice 5, FR-006). No not-found case: an unknown or assignment-less
    /// employee simply has zero assignments, matching the collection-GET shape of the
    /// invoice-frequencies (Slice 2) and billable-categories (Slice 4) reads rather than the
    /// single-resource <see cref="GetById"/>.
    /// </remarks>
    private static async Task<Ok<IReadOnlyList<CompassAssignmentDto>>> GetAssignments(
        int id,
        IDirectory directory,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CompassAssignmentDto> assignments =
            await directory.GetAssignmentsByEmployeeAsync(id, cancellationToken);
        return TypedResults.Ok(assignments);
    }
}
