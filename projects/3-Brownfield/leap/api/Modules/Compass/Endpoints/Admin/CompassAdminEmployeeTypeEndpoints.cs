using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// Employee-type administration — <c>/api/compass/v1/admin/employee-types</c>. Reads are open to any
/// authenticated EDJEr; only the writes require the admin role.
/// </summary>
public static class CompassAdminEmployeeTypeEndpoints
{
    private const string Resource = "employee-types";

    /// <summary>Maps the employee-type administration routes.</summary>
    public static RouteGroupBuilder MapCompassAdminEmployeeTypeEndpoints(this WebApplication app)
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
    ) => Results.Ok(await lookups.GetEmployeeTypesAsync(activeOnly, cancellationToken));

    private static async Task<IResult> Create(
        CreateCompassLookupRequest request,
        ICompassLookupService lookups,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        var result = await lookups.CreateEmployeeTypeAsync(request.TypeName, cancellationToken);

        if (result.Status is not AdminMutationStatus.Success)
        {
            LogRejection(loggerFactory, "create", request.TypeName, result.Status);
            return result.Status.ToErrorResult(result.Error);
        }

        return Results.Created(
            $"{CompassAdminRouteGroup.BasePath}/{Resource}/{result.Value!.Id}",
            result.Value
        );
    }

    private static async Task<IResult> Update(
        int id,
        UpdateCompassLookupRequest request,
        ICompassLookupService lookups,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        var result = await lookups.UpdateEmployeeTypeAsync(
            id,
            request.TypeName,
            request.IsActive,
            cancellationToken
        );

        if (result.Status is not AdminMutationStatus.Success)
        {
            LogRejection(loggerFactory, "update", request.TypeName, result.Status);
            return result.Status.ToErrorResult(result.Error);
        }

        return Results.Ok(result.Value);
    }

    private static void LogRejection(
        ILoggerFactory loggerFactory,
        string operation,
        string typeName,
        AdminMutationStatus status
    ) =>
        loggerFactory
            .CreateLogger(typeof(CompassAdminEmployeeTypeEndpoints).FullName!)
            .LogInformation(
                "Refused employee type {Operation} of {TypeName}: {Status}",
                operation,
                LogSanitizer.Clean(typeName),
                status
            );
}
