using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Auth;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// The provenance read that lets a TPS migration hold no database of its own: what it has already
/// created.
/// </summary>
/// <remarks>
/// Migration principal only, gated by IDENTITY rather than role: the migration principal holds the
/// Compass root, so a role check would expose this read to every Super Admin. Provenance is
/// deliberately absent from every application-facing DTO and this route is the single exception,
/// fenced by <c>CompassEmployeeDtoTests</c> and <c>CompassTransportContractTests</c>. Unpaged on
/// purpose: a partial answer would let the tool re-create records it already made.
/// </remarks>
public static class CompassMigrationProvenanceEndpoints
{
    /// <summary>Registers the provenance read route.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The application, for chaining.</returns>
    public static WebApplication MapCompassMigrationProvenanceEndpoints(this WebApplication app)
    {
        var group = app.MapCompassAdminGroup("migration");

        group.MapGet("/provenance", GetProvenance)
            .WithName("GetCompassMigrationProvenance");

        return app;
    }

    private static async Task<IResult> GetProvenance(
        HttpContext httpContext,
        ICompassMigrationProvenanceRepository provenance,
        CancellationToken cancellationToken
    )
    {
        // Identity, not role. The route group already requires the Compass root, which the migration
        // principal holds -- and so does every Super Admin, which is exactly who this must exclude.
        if (!MigrationPrincipal.IsMigrationPrincipal(httpContext.User))
        {
            return Results.Problem(
                title: "Migration provenance is readable only by the migration principal",
                detail: "This route exposes the mapping from legacy TPS identifiers to the Compass "
                    + "records they became. It exists for the migration tool alone; provenance is "
                    + "deliberately absent from every other Compass surface.",
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        return Results.Ok(
            new CompassMigrationProvenanceResponse(
                await provenance.GetAllAsync(cancellationToken)
            )
        );
    }
}
