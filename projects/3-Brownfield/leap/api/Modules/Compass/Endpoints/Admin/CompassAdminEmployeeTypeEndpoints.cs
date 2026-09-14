using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// Employee-type administration — <c>/api/compass/v1/admin/employee-types</c>.
/// </summary>
/// <remarks>
/// There is deliberately no <c>DELETE</c>. A lookup is retired by clearing its active flag through
/// <c>PUT</c>, because other records reference it by id and deactivation must leave them untouched
/// (FR-007, Principle VIII). Authorization comes from the shared group
/// (<see cref="CompassAdminRouteGroup"/>) and is never checked in-handler; reads are Super-Admin-scoped
/// too, per FR-009.
/// </remarks>
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

    /// <summary>
    /// Every employee type, or only the selectable ones when <paramref name="activeOnly"/> is set — the
    /// shape the EDJEr configuration form consumes (AC-25).
    /// </summary>
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

    /// <summary>Records a refused write.</summary>
    /// <remarks>
    /// The name arrives in a request body, so it is untrusted and passes through
    /// <see cref="LogSanitizer.Clean"/> before reaching the template.
    /// </remarks>
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
