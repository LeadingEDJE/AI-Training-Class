using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// EDJEr configuration — <c>/api/compass/v1/admin/edjers</c>. Rejections are <c>404</c> for an email
/// collision and <c>422</c> for a malformed value.
/// </summary>
public static class CompassAdminEdjerEndpoints
{
    private const string Resource = "edjers";

    /// <summary>Maps the EDJEr configuration routes.</summary>
    public static RouteGroupBuilder MapCompassAdminEdjerEndpoints(this WebApplication app)
    {
        var group = app.MapCompassAdminGroup(Resource);

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);

        return group;
    }

    private static async Task<IResult> GetAll(
        ICompassEmployeeService edjers,
        CancellationToken cancellationToken
    ) => Results.Ok(await edjers.GetEdjersAsync(cancellationToken));

    private static async Task<IResult> GetById(
        int id,
        ICompassEmployeeService edjers,
        CancellationToken cancellationToken
    )
    {
        var edjer = await edjers.GetEdjerAsync(id, cancellationToken);
        return edjer is null ? Results.NotFound() : Results.Ok(edjer);
    }

    private static async Task<IResult> Create(
        CompassEdjerRequest request,
        HttpContext httpContext,
        ICompassEmployeeService edjers,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        if (!CompassLegacyProvenance.MaySet(httpContext.User, request.LegacyTpsId))
        {
            return CompassLegacyProvenance.Refusal();
        }

        var result = await edjers.CreateEdjerAsync(request, cancellationToken);

        if (result.Status is not CompassWriteStatus.Success)
        {
            LogRejection(loggerFactory, "create", request.Email, result.Status);
            return result.Status.ToErrorResult(result.Error, result.BlockingAssignments);
        }

        return Results.Created(
            $"{CompassAdminRouteGroup.BasePath}/{Resource}/{result.Value!.Id}",
            result.Value
        );
    }

    private static async Task<IResult> Update(
        int id,
        CompassEdjerRequest request,
        HttpContext httpContext,
        ICompassEmployeeService edjers,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        // Same guard as Create, applied only when a coach role is present on the update request.
        if (!CompassLegacyProvenance.MaySet(httpContext.User, request.LegacyTpsId))
        {
            return CompassLegacyProvenance.Refusal();
        }

        var result = await edjers.UpdateEdjerAsync(id, request, cancellationToken);

        if (result.Status is not CompassWriteStatus.Success)
        {
            LogRejection(loggerFactory, "update", request.Email, result.Status);
            return result.Status.ToErrorResult(result.Error, result.BlockingAssignments);
        }

        return Results.Ok(result.Value);
    }

    private static void LogRejection(
        ILoggerFactory loggerFactory,
        string operation,
        string email,
        CompassWriteStatus status
    ) =>
        loggerFactory
            .CreateLogger(typeof(CompassAdminEdjerEndpoints).FullName!)
            .LogInformation(
                "Refused EDJEr {Operation} for {Email}: {Status}",
                operation,
                LogSanitizer.Clean(email),
                status
            );
}
