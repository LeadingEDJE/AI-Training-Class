using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// Client configuration — <c>/api/compass/v1/admin/clients</c>.
/// </summary>
/// <remarks>
/// There is deliberately no <c>DELETE</c>. A category is retired through <c>PUT</c> with its active
/// flag off (AC-23, Principle VIII); a client is never retired at all, because its Active/Inactive is
/// derived from its assignments (FR-021). Category routes are nested under their client so ownership
/// is part of the address, and because they hang off <see cref="CompassAdminRouteGroup"/> they inherit
/// the Super-Admin policy; a nested route mapped outside that group would be authorised by nothing,
/// which <c>CompassAdminClientAuthorizationTests</c> exists to catch. Authorization comes from the
/// shared group and is never checked in-handler.
/// </remarks>
public static class CompassAdminClientEndpoints
{
    private const string Resource = "clients";
    private const string Categories = "billable-time-categories";

    /// <summary>Maps the client configuration routes.</summary>
    /// <param name="app">The application to map onto.</param>
    /// <returns>The route group.</returns>
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
        // Provenance is gated on principal IDENTITY, not on the Compass root role the migration
        // principal shares with every Super Admin. Refused rather than dropped: a silent discard
        // would return 201 to someone who believes they recorded where this record came from.
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
        // The same guard as Create: this handler binds the same request type, so `legacyTpsId` is
        // part of the update contract whether or not the service reads it — and it does not. Without
        // the check an unauthorised caller gets 200 OK with the field silently discarded. MaySet
        // answers true for an absent value, so the Compass SPA, which never sends the field, is
        // unaffected; provenance is immutable once set, so nothing is re-applied.
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
        // Provenance is gated on principal IDENTITY, not on the Compass root role the migration
        // principal shares with every Super Admin. Refused rather than dropped: a silent discard
        // would return 201 to someone who believes they recorded where this record came from.
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

    /// <summary>Records a refused write.</summary>
    /// <remarks>
    /// The name arrives in a request body, so it is untrusted and passes through
    /// <see cref="LogSanitizer.Clean"/> before reaching the template.
    /// </remarks>
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
