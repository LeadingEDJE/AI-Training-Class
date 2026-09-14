using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints;

/// <summary>
/// The Compass module's client read (spec 009 Slice 3, FR-005) — read family 2 of the Compass
/// Directory boundary, on the versioned, role-gated HTTP transport.
/// </summary>
/// <remarks>
/// Its own route group and its own <c>.RequireAuthorization</c> call, per FR-017. The handler reaches
/// data only through <see cref="IDirectory"/>, never the database context. This is a different surface
/// from the same-named files under <c>Endpoints/Write/</c> and <c>Endpoints/Read/</c>, which back the
/// application surface the Compass SPA calls (ADR-008); this one is the published, versioned boundary
/// out-of-process consumers bind to.
/// </remarks>
public static class CompassClientEndpoints
{
    /// <summary>
    /// Maps the Compass client read endpoint under <c>/api/compass/v1/clients</c>, gated by the
    /// Compass admin policy.
    /// </summary>
    public static RouteGroupBuilder MapCompassClientEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/compass/v1/clients")
            .WithTags("Compass")
            .RequireAuthorization(RolePolicy.CompassAdmin);

        group.MapGet("/{id:int}", GetById);
        group.MapGet("/{id:int}/billable-categories", GetBillableCategories);
        group.MapGet("/{id:int}/assignments", GetAssignments);

        return group;
    }

    /// <summary>Returns one Compass client by id, with its status DERIVED at read time.</summary>
    /// <remarks>
    /// Read family 2 of the Compass Directory boundary (spec 009 Slice 3, FR-005). Returns 404 when
    /// <paramref name="id"/> does not match an existing client. <c>Status</c> is always "Active" or
    /// "Inactive", computed from the client's assignments — see <see cref="CompassClientDto"/>'s own
    /// remarks for why there is no stored status column to read instead.
    /// </remarks>
    private static async Task<Results<Ok<CompassClientDto>, NotFound>> GetById(
        int id,
        IDirectory directory,
        CancellationToken cancellationToken)
    {
        CompassClientDto? client = await directory.GetClientAsync(id, cancellationToken);
        return client is not null
            ? TypedResults.Ok(client)
            : TypedResults.NotFound();
    }

    /// <summary>Returns every billable time category configured for the given client.</summary>
    /// <remarks>
    /// Read family 6 (spec 009 Slice 4, FR-009). Returns EVERY category, active and inactive alike —
    /// see <see cref="Contracts.CompassBillableCategoryDto"/>'s remarks for why. No not-found case: an
    /// unknown client id simply has no categories, so this always returns 200 with a (possibly empty)
    /// list, matching the shape of the invoice-frequencies collection read (Slice 2) rather than the
    /// single-resource <see cref="GetById"/>.
    /// </remarks>
    private static async Task<Ok<IReadOnlyList<CompassBillableCategoryDto>>> GetBillableCategories(
        int id,
        IDirectory directory,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CompassBillableCategoryDto> categories =
            await directory.GetBillableCategoriesAsync(id, cancellationToken);
        return TypedResults.Ok(categories);
    }

    /// <summary>Returns every assignment at the given client.</summary>
    /// <remarks>
    /// Read family 3 (spec 009 Slice 5, FR-006). No not-found case: an unknown or assignment-less
    /// client simply has zero assignments, matching the collection-GET shape above.
    /// </remarks>
    private static async Task<Ok<IReadOnlyList<CompassAssignmentDto>>> GetAssignments(
        int id,
        IDirectory directory,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CompassAssignmentDto> assignments =
            await directory.GetAssignmentsByClientAsync(id, cancellationToken);
        return TypedResults.Ok(assignments);
    }
}
