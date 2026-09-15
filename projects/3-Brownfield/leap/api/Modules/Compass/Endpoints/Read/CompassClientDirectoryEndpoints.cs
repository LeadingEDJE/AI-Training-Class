using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Read;

/// <summary>The Client Directory and client view read surfaces — AC-3 to AC-4.</summary>
public static class CompassClientDirectoryEndpoints
{
    /// <summary>Maps <c>/api/compass/client-directory</c>.</summary>
    public static RouteGroupBuilder MapCompassClientDirectoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/compass/client-directory")
            .WithTags("Compass Read")
            .RequireAuthorization();

        group.MapGet("/", GetClientDirectory);
        group.MapGet("/{id:int}", GetClientView);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<ClientDirectoryRowDto>>> GetClientDirectory(
        ICompassDirectoryReadService readService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        string? search = null,
        string? sort = null,
        bool desc = false)
    {
        var logger = loggerFactory.CreateLogger(typeof(CompassClientDirectoryEndpoints));

        logger.LogInformation(
            "Client Directory requested (search={Search}, sort={Sort})",
            LogSanitizer.Clean(search),
            LogSanitizer.Clean(sort));

        var query = new ClientDirectoryQuery(search, sort, desc);

        return TypedResults.Ok(await readService.GetClientDirectoryAsync(query, cancellationToken));
    }

    private static async Task<Results<Ok<ClientViewDto>, NotFound>> GetClientView(
        int id,
        ICompassDirectoryReadService readService,
        CancellationToken cancellationToken)
    {
        ClientViewDto? view = await readService.GetClientViewAsync(id, cancellationToken);

        return view is not null ? TypedResults.Ok(view) : TypedResults.NotFound();
    }
}
