using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// EDJEr configuration — <c>/api/compass/v1/admin/edjers</c>.
/// </summary>
/// <remarks>
/// There is deliberately no <c>DELETE</c>. An EDJEr is deactivated through the active flag on
/// <c>PUT</c>, and that deactivation is guarded (FR-019); email uniqueness spans inactive rows because
/// rows are never removed (Principle VIII, BR-9). Authorization comes from the shared group
/// (<see cref="CompassAdminRouteGroup"/>) and is never checked in-handler; reads are Super-Admin-scoped
/// too, since a Compass Admin reads EDJErs through the directory boundary, whose DTO carries no
/// time-tracking flags (FR-016, BR-1). Rejections are <c>409</c> for an email collision, <c>400</c> for
/// a malformed or unselectable value, and <c>422</c> for a refused deactivation, naming the blockers.
/// </remarks>
public static class CompassAdminEdjerEndpoints
{
    private const string Resource = "edjers";

    /// <summary>Maps the EDJEr configuration routes.</summary>
    /// <param name="app">The application to map onto.</param>
    /// <returns>The route group.</returns>
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
        // Provenance is gated on principal IDENTITY, not on the Compass root role the migration
        // principal shares with every Super Admin. Refused rather than dropped: a silent discard
        // would return 201 to someone who believes they recorded where this record came from.
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
        // The same guard as Create — see CompassAdminClientEndpoints.Update. This handler binds the
        // same request type, so `legacyTpsId` is part of the update contract; without the check an
        // unauthorised caller gets 200 OK with the value silently discarded. MaySet passes an absent
        // value, so the SPA is unaffected and the migration's coach pass, which re-sends this request
        // type carrying its provenance, keeps working.
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

    /// <summary>Records a refused write.</summary>
    /// <remarks>
    /// The email arrives in a request body, so it is untrusted and passes through
    /// <see cref="LogSanitizer.Clean"/> before reaching the template.
    /// The full address is logged deliberately by owner decision: it is what makes a rejected write
    /// diagnosable and these logs are internal, so do not mask it. CodeQL raises "Exposure of private
    /// information" here; that finding is accepted. The sanitizer is a separate concern and stays.
    /// </remarks>
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
