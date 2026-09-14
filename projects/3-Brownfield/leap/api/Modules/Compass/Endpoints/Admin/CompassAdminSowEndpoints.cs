using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;

/// <summary>
/// The contract-period (SOW) configuration surface.
/// </summary>
/// <remarks>
/// Mounted through <see cref="CompassAdminRouteGroup.MapCompassAdminGroup"/>, inheriting
/// <c>RolePolicy.CompassSuperAdmin</c>. Substituting <c>CompassAdmin</c> would admit the read-only
/// role and is a privilege escalation, not a widening (AC-44). The handler decides whether the caller
/// is the migration principal and passes that boolean to the service, where it gates the
/// <c>LegacyMigrated</c> validation bypass. It is decided here, the only layer holding the
/// authenticated principal, so the service stays testable without an HTTP context and the gate is
/// visible at the call site. See <c>CompassSowMigrationService</c> and Principle VIII.
/// </remarks>
public static class CompassAdminSowEndpoints
{
    private const string Resource = "sows";

    /// <summary>Maps the contract-period configuration routes.</summary>
    /// <param name="app">The application to map onto.</param>
    /// <returns>The route group.</returns>
    public static RouteGroupBuilder MapCompassAdminSowEndpoints(this WebApplication app)
    {
        var group = app.MapCompassAdminGroup(Resource);

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);

        return group;
    }

    /// <summary>
    /// Lists every record.
    /// </summary>
    /// <remarks>
    /// Added so the TPS migration can VERIFY what it wrote. SC-002 requires every migrated
    /// relationship checked at 100% rather than sampled, and a create-only surface makes that
    /// impossible in principle — the reconciliation report could only ever say "unverified" for this
    /// entity. Read-only: still no update or delete.
    /// </remarks>
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
        // Identity, not role. The migration principal holds the Compass root, so a role check here
        // would hand the LegacyMigrated bypass to every Compass Super Admin.
        var isMigrationPrincipal = MigrationPrincipal.IsMigrationPrincipal(httpContext.User);

        // Provenance is gated on principal IDENTITY, not on the Compass root role the migration
        // principal shares with every Super Admin. Refused rather than dropped: a silent discard
        // would return 201 to someone who believes they recorded where this record came from.
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

    /// <summary>
    /// Records a refused write.
    /// </summary>
    /// <remarks>
    /// Values arrive in a request body, so they are untrusted and pass through
    /// <see cref="LogSanitizer.Clean"/> — newlines in input otherwise forge log lines (CodeQL
    /// <c>cs/log-forging</c>). Applied at the boundary rather than on a per-type judgement a later
    /// refactor would silently invalidate.
    /// </remarks>
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
