using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// Skill administration — <c>/api/compass/v1/admin/skills</c>. Reads are open to any authenticated
/// EDJEr; only the writes require the admin role.
/// </summary>
public static class CompassAdminSkillEndpoints
{
    private const string Resource = "skills";

    /// <summary>Maps the skill administration routes.</summary>
    public static RouteGroupBuilder MapCompassAdminSkillEndpoints(this WebApplication app)
    {
        var group = app.MapCompassAdminGroup(Resource);

        group.MapGet("/", GetAll);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);

        return group;
    }

    private static async Task<IResult> GetAll(
        ICompassLookupService lookups,
        CancellationToken cancellationToken,
        bool activeOnly = false
    ) => Results.Ok(await lookups.GetSkillsAsync(activeOnly, cancellationToken));

    private static async Task<IResult> Create(
        CreateSkillRequest request,
        ICompassLookupService lookups,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        var result = await lookups.CreateSkillAsync(request.Name, cancellationToken);

        if (result.Status is not AdminMutationStatus.Success)
        {
            LogRejection(loggerFactory, "create", request.Name, result.Status);
            return result.Status.ToErrorResult(result.Error);
        }

        return Results.Created(
            $"{CompassAdminRouteGroup.BasePath}/{Resource}/{result.Value!.Id}",
            result.Value
        );
    }

    private static async Task<IResult> Update(
        int id,
        UpdateSkillRequest request,
        ICompassLookupService lookups,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        var result = await lookups.UpdateSkillAsync(
            id,
            request.Name,
            request.IsActive,
            cancellationToken
        );

        if (result.Status is not AdminMutationStatus.Success)
        {
            LogRejection(loggerFactory, "update", request.Name, result.Status);
            return result.Status.ToErrorResult(result.Error);
        }

        return Results.Ok(result.Value);
    }

    private static void LogRejection(
        ILoggerFactory loggerFactory,
        string operation,
        string name,
        AdminMutationStatus status
    ) =>
        loggerFactory
            .CreateLogger(typeof(CompassAdminSkillEndpoints).FullName!)
            .LogInformation(
                "Refused skill {Operation} of {Name}: {Status}",
                operation,
                LogSanitizer.Clean(name),
                status
            );
}
