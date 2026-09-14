
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
namespace LeadingEDJE.Leap.Api.Platform.Endpoints;

/// <summary>
/// SuperAdmin cookie-session impersonation endpoints. Starting re-issues the session cookie as the
/// target identity with provenance claims; stopping restores the original identity from them.
/// </summary>
/// <remarks>Both return the <c>/api/me</c>-shaped payload; no token is minted or returned.</remarks>
public static class ImpersonateEndpoints
{
    /// <summary>
    /// Maps <c>POST /api/impersonate/{edjeId}</c> (SuperAdmin-gated) and <c>POST /api/impersonate/stop</c>
    /// (any authenticated session — the impersonated identity itself calls it to restore).
    /// </summary>
    public static WebApplication MapImpersonateEndpoints(this WebApplication app)
    {
        app.MapPost("/api/impersonate/{edjeId:guid}", HandleStart)
            .WithTags("Impersonation")
            .RequireAuthorization(RolePolicy.SuperAdmin);

        // Stop is reachable by the impersonated (non-SuperAdmin) session, so it only requires an
        // authenticated cookie; the handler enforces that impersonation is actually in progress.
        app.MapPost("/api/impersonate/stop", HandleStop)
            .WithTags("Impersonation")
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleStart(
        Guid edjeId,
        HttpContext httpContext,
        IImpersonationService impersonation)
    {
        var outcome = await impersonation.StartAsync(httpContext.User, edjeId, httpContext);
        return outcome.Status switch
        {
            ImpersonationStatus.Started => Results.Ok(outcome.Identity),
            ImpersonationStatus.AlreadyImpersonating =>
                Results.Conflict("Already impersonating; stop the current impersonation first."),
            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> HandleStop(
        HttpContext httpContext,
        IImpersonationService impersonation)
    {
        var outcome = await impersonation.StopAsync(httpContext.User, httpContext);
        return outcome.Status == ImpersonationStatus.Stopped
            ? Results.Ok(outcome.Identity)
            : Results.BadRequest("Not currently impersonating.");
    }
}
