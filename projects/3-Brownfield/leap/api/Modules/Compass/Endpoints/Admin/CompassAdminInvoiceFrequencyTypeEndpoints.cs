using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// Invoice-frequency-type administration — <c>/api/compass/v1/admin/invoice-frequency-types</c>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="CompassAdminEmployeeTypeEndpoints"/>. The two are separate route groups over one
/// service because they are separate resources with separate name spaces and separate published DTOs —
/// see <c>CompassAdminInvoiceFrequencyTypeEndpointsTests.TheTwoLookups_AreIndependentNameSpaces</c>,
/// which fails if they are ever wired to the same table. No <c>DELETE</c>, for the same reason as there.
/// </remarks>
public static class CompassAdminInvoiceFrequencyTypeEndpoints
{
    private const string Resource = "invoice-frequency-types";

    /// <summary>Maps the invoice-frequency-type administration routes.</summary>
    public static RouteGroupBuilder MapCompassAdminInvoiceFrequencyTypeEndpoints(
        this WebApplication app
    )
    {
        var group = app.MapCompassAdminGroup(Resource);

        group.MapGet("/", GetAll);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);

        return group;
    }

    /// <summary>
    /// Every invoice frequency type, or only the selectable ones — the shape the client configuration
    /// form's invoicing default consumes (AC-26).
    /// </summary>
    private static async Task<IResult> GetAll(
        ICompassLookupService lookups,
        CancellationToken cancellationToken,
        bool activeOnly = false
    ) => Results.Ok(await lookups.GetInvoiceFrequencyTypesAsync(activeOnly, cancellationToken));

    private static async Task<IResult> Create(
        CreateCompassLookupRequest request,
        ICompassLookupService lookups,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        var result = await lookups.CreateInvoiceFrequencyTypeAsync(
            request.TypeName,
            cancellationToken
        );

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
        var result = await lookups.UpdateInvoiceFrequencyTypeAsync(
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

    /// <summary>
    /// Records a refused write, sanitising the request-sourced name before it reaches the log template
    /// (CodeQL <c>cs/log-forging</c>).
    /// </summary>
    private static void LogRejection(
        ILoggerFactory loggerFactory,
        string operation,
        string typeName,
        AdminMutationStatus status
    ) =>
        loggerFactory
            .CreateLogger(typeof(CompassAdminInvoiceFrequencyTypeEndpoints).FullName!)
            .LogInformation(
                "Refused invoice frequency type {Operation} of {TypeName}: {Status}",
                operation,
                LogSanitizer.Clean(typeName),
                status
            );
}
