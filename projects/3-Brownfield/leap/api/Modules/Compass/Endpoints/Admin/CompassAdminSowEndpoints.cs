using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// The contract-period (SOW) configuration surface, gated the same way as the client and EDJEr
/// configuration surfaces.
/// </summary>
public static class CompassAdminSowEndpoints
{
    private const string Resource = "sows";

    /// <summary>Maps the contract-period configuration routes.</summary>
    public static RouteGroupBuilder MapCompassAdminSowEndpoints(this WebApplication app)
    {
        var group = app.MapCompassAdminGroup(Resource);

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);

        return group;
    }

    private static async Task<IResult> GetAll(
        ICompassSowMigrationService service,
        CancellationToken cancellationToken
    ) => Results.Ok(await service.GetAllAsync(cancellationToken));

    private static async Task<IResult> GetById(
        int id,
        ICompassSowMigrationService service,
        CancellationToken cancellationToken
    )
    {
        var record = await service.GetAsync(id, cancellationToken);
        return record is null ? Results.NotFound() : Results.Ok(record);
    }

    private static async Task<IResult> Create(
        CompassSowRequest request,
        HttpContext httpContext,
        ICompassSowMigrationService sows,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        var isMigrationPrincipal = MigrationPrincipal.IsMigrationPrincipal(httpContext.User);

        if (!CompassLegacyProvenance.MaySet(httpContext.User, request.LegacyTpsId))
        {
            return CompassLegacyProvenance.Refusal();
        }

        var result = await sows.CreateAsync(request, isMigrationPrincipal, cancellationToken);

        if (result.Status is not CompassWriteStatus.Success)
        {
            LogRejection(loggerFactory, request, result.Status);
            return result.Status.ToErrorResult(result.Error);
        }

        return Results.Created(
            $"{CompassAdminRouteGroup.BasePath}/{Resource}/{result.Value!.Id}",
            result.Value
        );
    }

    private static void LogRejection(
        ILoggerFactory loggerFactory,
        CompassSowRequest request,
        CompassWriteStatus status
    ) =>
        loggerFactory
            .CreateLogger(typeof(CompassAdminSowEndpoints))
            .LogWarning(
                "Compass SOW create refused for assignment {AssignmentId} (type {SowType}): {Status}",
                LogSanitizer.Clean(request.ClientAssignmentId.ToString()),
                LogSanitizer.Clean(request.SowType.ToString()),
                status
            );
}
