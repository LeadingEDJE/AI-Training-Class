using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints;

/// <summary>
/// The Compass module's invoice-frequencies read (spec 009 Slice 2, FR-010) — read family 7 of the
/// Compass Directory boundary, on the versioned, role-gated HTTP transport.
/// </summary>
/// <remarks>
/// Its own route group and its own <c>.RequireAuthorization</c> call: FR-017 requires every published
/// read family to carry its own authorization requirement rather than one boundary-wide gate, so a
/// future per-data-kind scope has a group to attach to. The handler reaches data only through
/// <see cref="IDirectory"/>, never the database context — the same rule
/// <see cref="CompassEmployeeEndpoints"/> documents.
/// </remarks>
public static class CompassInvoiceFrequencyEndpoints
{
    /// <summary>
    /// Maps the Compass invoice-frequencies read endpoint under
    /// <c>/api/compass/v1/invoice-frequencies</c>, gated by the Compass admin policy.
    /// </summary>
    public static RouteGroupBuilder MapCompassInvoiceFrequencyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/compass/v1/invoice-frequencies")
            .WithTags("Compass")
            .RequireAuthorization(RolePolicy.CompassAdmin);

        group.MapGet("/", GetAll);

        return group;
    }

    /// <summary>Returns every active invoice frequency type.</summary>
    /// <remarks>
    /// Read family 7 (spec 009 Slice 2, FR-010). No id parameter and no not-found case — this is a
    /// flat lookup, always 200. Filters to active types server-side, so every row in the response is
    /// active by construction; see <see cref="Contracts.CompassInvoiceFrequencyDto"/>'s remarks for why
    /// there is no <c>IsActive</c> field to check instead.
    /// </remarks>
    private static async Task<Ok<IReadOnlyList<CompassInvoiceFrequencyDto>>> GetAll(
        IDirectory directory,
        CancellationToken cancellationToken)
    {
        var invoiceFrequencies = await directory.GetInvoiceFrequenciesAsync(cancellationToken);
        return TypedResults.Ok(invoiceFrequencies);
    }
}
