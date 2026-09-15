using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// Client configuration — <c>/api/compass/v1/admin/clients</c>. Supports full CRUD including
/// <c>DELETE</c>, retired in favor of the assignment-derived Active/Inactive flag (AC-9).
/// </summary>
public static class CompassAdminClientEndpoints
{
    private const string Resource = "clients";
    private const string Categories = "billable-time-categories";

    /// <summary>Maps the client configuration routes.</summary>
    public static RouteGroupBuilder MapCompassAdminClientEndpoints(this WebApplication app)
    {
        var group = app.MapCompassAdminGroup(Resource);

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);

        group.MapPost($"/{{clientId:int}}/{Categories}", AddCategory);
        group.MapPut($"/{{clientId:int}}/{Categories}/{{categoryId:int}}", UpdateCategory);

        return group;
    }

    private static async Task<IResult> GetAll(
        ICompassClientService clients,
        CancellationToken cancellationToken
    ) => Results.Ok(await clients.GetClientsAsync(cancellationToken));

    private static async Task<IResult> GetById(
        int id,
        ICompassClientService clients,
        CancellationToken cancellationToken
    )
    {
        var client = await clients.GetClientAsync(id, cancellationToken);
        return client is null ? Results.NotFound() : Results.Ok(client);
    }

    private static async Task<IResult> Create(
        CompassClientRequest request,
        HttpContext httpContext,
        ICompassClientService clients,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        if (!CompassLegacyProvenance.MaySet(httpContext.User, request.LegacyTpsId))
        {
            return CompassLegacyProvenance.Refusal();
        }

        var result = await clients.CreateClientAsync(request, cancellationToken);

        if (result.Status is not CompassWriteStatus.Success)
        {
            LogRejection(loggerFactory, "create", request.ClientName, result.Status);
            return result.Status.ToErrorResult(result.Error);
        }

        return Results.Created(
            $"{CompassAdminRouteGroup.BasePath}/{Resource}/{result.Value!.Id}",
            result.Value
        );
    }

    private static async Task<IResult> Update(
        int id,
        CompassClientRequest request,
        HttpContext httpContext,
        ICompassClientService clients,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        // Mirrors the Create guard; the update path additionally allows a Compass Admin to clear an
        // existing legacy identifier back to null.
        if (!CompassLegacyProvenance.MaySet(httpContext.User, request.LegacyTpsId))
        {
            return CompassLegacyProvenance.Refusal();
        }

        var result = await clients.UpdateClientAsync(id, request, cancellationToken);

        if (result.Status is not CompassWriteStatus.Success)
        {
            LogRejection(loggerFactory, "update", request.ClientName, result.Status);
            return result.Status.ToErrorResult(result.Error);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> AddCategory(
        int clientId,
        CreateBillableTimeCategoryRequest request,
        HttpContext httpContext,
        ICompassClientService clients,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        if (!CompassLegacyProvenance.MaySet(httpContext.User, request.LegacyTpsId))
        {
            return CompassLegacyProvenance.Refusal();
        }

        var result = await clients.AddCategoryAsync(clientId, request, cancellationToken);

        if (result.Status is not CompassWriteStatus.Success)
        {
            LogRejection(loggerFactory, "category add", request.CategoryName, result.Status);
            return result.Status.ToErrorResult(result.Error);
        }

        return Results.Created(
            $"{CompassAdminRouteGroup.BasePath}/{Resource}/{clientId}/{Categories}/{result.Value!.Id}",
            result.Value
        );
    }

    private static async Task<IResult> UpdateCategory(
        int clientId,
        int categoryId,
        UpdateBillableTimeCategoryRequest request,
        ICompassClientService clients,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        var result = await clients.UpdateCategoryAsync(
            clientId,
            categoryId,
            request,
            cancellationToken
        );

        if (result.Status is not CompassWriteStatus.Success)
        {
            LogRejection(loggerFactory, "category update", request.CategoryName, result.Status);
            return result.Status.ToErrorResult(result.Error);
        }

        return Results.Ok(result.Value);
    }

    private static void LogRejection(
        ILoggerFactory loggerFactory,
        string operation,
        string? name,
        CompassWriteStatus status
    ) =>
        loggerFactory
            .CreateLogger(typeof(CompassAdminClientEndpoints).FullName!)
            .LogInformation(
                "Refused client {Operation} for {Name}: {Status}",
                operation,
                LogSanitizer.Clean(name ?? string.Empty),
                status
            );
}
