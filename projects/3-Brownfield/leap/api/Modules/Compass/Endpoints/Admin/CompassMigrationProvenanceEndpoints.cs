using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Auth;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// The provenance read that lets a TPS migration hold no database of its own: what it has already
/// created. Paged, and open to every Compass Super Admin.
/// </summary>
public static class CompassMigrationProvenanceEndpoints
{
    /// <summary>Registers the provenance read route.</summary>
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
