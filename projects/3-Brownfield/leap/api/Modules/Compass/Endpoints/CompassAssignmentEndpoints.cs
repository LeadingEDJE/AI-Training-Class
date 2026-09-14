using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints;

/// <summary>
/// The Compass module's bare assignment-by-id read (spec 009 Slice 5, FR-006) — read family 3 of the
/// Compass Directory boundary, on the versioned, role-gated HTTP transport.
/// </summary>
/// <remarks>
/// Its own route group and its own <c>.RequireAuthorization</c> call, per FR-017. The other two
/// assignment lookups (<c>/employees/{id}/assignments</c>, <c>/clients/{id}/assignments</c>) are scoped
/// by an owning resource and live in <see cref="CompassEmployeeEndpoints"/> and
/// <see cref="CompassClientEndpoints"/>, inheriting those groups' authorization. This file shares a
/// class name with <c>Endpoints/Write/CompassAssignmentEndpoints</c>, a different surface, so
/// <c>app.MapCompassAssignmentEndpoints()</c> is ambiguous (CS0121) and <c>Program.cs</c> registers
/// both by fully qualified type name. The handler reads only through <see cref="IDirectory"/>.
/// </remarks>
public static class CompassAssignmentEndpoints
{
    /// <summary>
    /// Maps the Compass assignment read endpoint under <c>/api/compass/v1/assignments</c>, gated by
    /// the Compass admin policy.
    /// </summary>
    public static RouteGroupBuilder MapCompassAssignmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/compass/v1/assignments")
            .WithTags("Compass")
            .RequireAuthorization(RolePolicy.CompassAdmin);

        group.MapGet("/{id:int}", GetById);
        group.MapGet("/{id:int}/sows", GetSows);

        return group;
    }

    /// <summary>Returns one client assignment by id.</summary>
    /// <remarks>
    /// Read family 3 of the Compass Directory boundary (spec 009 Slice 5, FR-006). Returns 404 when
    /// <paramref name="id"/> does not match an existing assignment. The SAME contract
    /// (<see cref="Contracts.CompassAssignmentDto"/>) also backs the two owner-scoped collection reads —
    /// <c>employees/{id}/assignments</c> and <c>clients/{id}/assignments</c> — this route is the
    /// bare, single-resource lookup.
    /// </remarks>
    private static async Task<Results<Ok<CompassAssignmentDto>, NotFound>> GetById(
        int id,
        IDirectory directory,
        CancellationToken cancellationToken)
    {
        CompassAssignmentDto? assignment = await directory.GetAssignmentAsync(id, cancellationToken);
        return assignment is not null
            ? TypedResults.Ok(assignment)
            : TypedResults.NotFound();
    }

    /// <summary>Returns every SOW (statement of work / contract period) under the given assignment.</summary>
    /// <remarks>
    /// Read family 4 (spec 009 Slice 6, FR-007). No not-found case: an unknown or SOW-less assignment
    /// simply has zero SOWs, matching the collection reads elsewhere on this boundary.
    /// <c>RateIncrease</c>/<c>Note</c> are present only for an Elevated-tier-or-above caller; every
    /// other authorized tier gets them omitted, not null.
    /// </remarks>
    private static async Task<Ok<IReadOnlyList<CompassDirectorySowDto>>> GetSows(
        int id,
        IDirectory directory,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CompassDirectorySowDto> sows =
            await directory.GetSowsByAssignmentAsync(id, cancellationToken);
        return TypedResults.Ok(sows);
    }
}
