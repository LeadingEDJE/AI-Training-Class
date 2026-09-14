using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Sustainsys.Saml2.AspNetCore2;
// Disambiguate our config POCO from Sustainsys.Saml2.AspNetCore2.Saml2Options (41-01 name collision).
using Saml2Options = LeadingEDJE.Leap.Api.Platform.Auth.Saml2Options;

namespace LeadingEDJE.Leap.Api.Platform.Endpoints;

/// <summary>
/// Cookie and Google SAML sign-in endpoints. <c>/auth/*</c> is anonymous, reachable while logged out;
/// <c>/api/me</c> is the SPA's session probe.
/// </summary>
/// <remarks>
/// The login callback drives the CompleteSignIn pipeline against the external holding cookie signed
/// by Sustainsys, never against a session cookie.
/// </remarks>
public static class AuthEndpoints
{
    /// <summary>Maps the <c>/auth/*</c> sign-in endpoints and the <c>/api/me</c> session probe.</summary>
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/auth")
            .WithTags("Auth")
            .AllowAnonymous();

        auth.MapGet("/login", HandleLogin);
        auth.MapGet("/login-callback", HandleLoginCallback);
        // Inline lambda (not a method group): a lone-HttpContext handler method group binds to the
        // RequestDelegate overload and discards the IResult (ASP0016); the async lambda returns the
        // IResult cleanly. Must await SignOutAsync — a discarded SignOut never clears the cookie (the
        // original TPS logout bug). Land on /auth/signed-out, not the SPA, which would silently
        // re-authenticate against the still-live Google session.
        auth.MapGet("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(AuthConstants.Settings.LeadingEdjeAuthenticationType);
            return Results.Redirect("/auth/signed-out");
        });
        auth.MapGet("/signed-out", HandleSignedOut);
        auth.MapGet("/access-denied", HandleAccessDenied);
        auth.MapGet("/stub-login", HandleStubLogin);

        // Anonymous by design: the SPA probes this to detect an unauthenticated session without
        // triggering a SAML challenge. Returns 401 (not a redirect) when there is no session.
        app.MapGet("/api/me", HandleMe)
            .WithTags("Auth")
            .AllowAnonymous();

        return app;
    }

    /// <summary>Initiates the SAML challenge.</summary>
    /// <remarks>
    /// When SAML is not registered for the environment — previews never register a per-PR Google app —
    /// and <c>Auth:SharedLoginBaseUrl</c> is configured, this delegates to the stable dev host's
    /// <c>/auth/login</c> with an absolute returnUrl back at this host. With no delegate configured it
    /// keeps the detectable 503 "SAML not configured" problem: a documented degraded state, never a
    /// silent redirect loop.
    /// </remarks>
    private static async Task<IResult> HandleLogin(
        IAuthenticationSchemeProvider schemes,
        HttpContext httpContext,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        string? returnUrl,
        string? switchAccount = null)
    {
        // A string, not a bool: the flag is presence-based and its recovery links use "1", which
        // minimal-API bool binding rejects with a 400 before the handler runs (#671). Treat "true"
        // and "1" as the request to force Google's account chooser; anything else stays silent.
        var forceChooser = string.Equals(switchAccount, "true", StringComparison.OrdinalIgnoreCase)
            || switchAccount == "1";

        var samlScheme = await schemes.GetSchemeAsync(Saml2Defaults.Scheme);
        if (samlScheme is null)
        {
            var sharedLoginBaseUrl = configuration["Auth:SharedLoginBaseUrl"];
            if (string.IsNullOrWhiteSpace(sharedLoginBaseUrl))
            {
                // No delegate configured: retain the detectable degraded state (R-3 / T-45-06).
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "SAML not configured",
                    detail: "Google SAML sign-in is not configured for this environment.");
            }

            // Absolutize returnUrl against this host so the dev leg can send the user back here.
            // Request.Scheme/Host are ForwardedHeaders-corrected (UseForwardedHeaders runs first).
            // Only an http/https absolute URL is forwarded verbatim: on Unix, Uri.TryCreate treats a
            // rooted path like "/ooto/" as an absolute file: URI, so the scheme check is required to
            // keep local paths on the absolutize branch.
            string absoluteReturnUrl;
            if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var parsed)
                && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
            {
                // Already absolute — forward verbatim; the dev host's SafeReturnUrl is the guard here.
                absoluteReturnUrl = returnUrl!;
            }
            else
            {
                var localPath = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/')
                    ? returnUrl
                    : "/";
                absoluteReturnUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{localPath}";
            }

            var delegateTarget =
                $"{sharedLoginBaseUrl.TrimEnd('/')}/auth/login?returnUrl={Uri.EscapeDataString(absoluteReturnUrl)}";

            // Previews delegate to the dev host's real Google SAML, so the switch-account request has
            // to ride along for the chooser to be forced there (#671).
            if (forceChooser)
            {
                delegateTarget += $"&{AuthConstants.Saml.SwitchAccountQueryFlag}=true";
            }

            loggerFactory.CreateLogger(typeof(AuthEndpoints)).LogInformation(
                "SAML unregistered; delegating /auth/login to shared dev host with returnUrl {ReturnUrl}",
                LogSanitizer.Clean(absoluteReturnUrl));

            return Results.Redirect(delegateTarget);
        }

        var callback = string.IsNullOrEmpty(returnUrl)
            ? "/auth/login-callback"
            : $"/auth/login-callback?returnUrl={Uri.EscapeDataString(returnUrl)}";

        var props = new AuthenticationProperties { RedirectUri = callback };
        if (forceChooser)
        {
            // Round-tripped as relayData into AuthenticationRequestCreated, which sets ForceAuthn (#671).
            props.Items[AuthConstants.Saml.SwitchAccountRelayKey] = "true";
        }

        return Results.Challenge(props, [Saml2Defaults.Scheme]);
    }

    /// <summary>
    /// Runs after Sustainsys signs the validated assertion into the external holding cookie. Reads
    /// the identity + group memberships and drives the full CompleteSignIn pipeline.
    /// </summary>
    private static async Task<IResult> HandleLoginCallback(
        HttpContext httpContext,
        ISignInService signInService,
        IOptions<Saml2Options> samlOptions,
        string? returnUrl)
    {
        var result = await httpContext.AuthenticateAsync(AuthConstants.Settings.ExternalScheme);
        if (!result.Succeeded || result.Principal is null)
        {
            // Hitting the callback with no valid external cookie must deny cleanly, never 500.
            await httpContext.SignOutAsync(AuthConstants.Settings.ExternalScheme);
            await httpContext.SignOutAsync(AuthConstants.Settings.LeadingEdjeAuthenticationType);
            return Results.Redirect($"/auth/access-denied?msg={Uri.EscapeDataString(SignInDenialReasons.SignInFailed)}");
        }

        var email = result.Principal.FindFirstValue(ClaimTypes.Email)
                    ?? result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = result.Principal.FindFirstValue(ClaimTypes.Name) ?? email;
        var groups = ExtractGroups(result.Principal, samlOptions.Value.GroupsAttributeName);

        var outcome = await signInService.CompleteSignInAsync(email, name, groups, httpContext, returnUrl);
        return Results.Redirect(outcome.Location);
    }

    /// <summary>
    /// Extracts Google group display names from the assertion principal under the configured
    /// attribute (default <c>"groups"</c>), matched case-insensitively.
    /// </summary>
    internal static IEnumerable<string> ExtractGroups(ClaimsPrincipal principal, string? groupsAttributeName)
    {
        var attribute = string.IsNullOrWhiteSpace(groupsAttributeName) ? "groups" : groupsAttributeName;
        return principal.Claims
            .Where(c => string.Equals(c.Type, attribute, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .ToList();
    }

    /// <summary>Anonymous, non-SPA landing page for a completed sign-out.</summary>
    private static IResult HandleSignedOut() =>
        Results.Text(
            "You are signed out of the LeadingEDJE Timesheet system.\n\n" +
            "You are still signed in to your Google account, so signing in again will not prompt you.\n\n" +
            "Sign in again: /auth/login",
            "text/plain");

    /// <summary>
    /// The LE-branded denial page, served as a 403 for every sign-in denial and failure. It carries the
    /// reason verbatim, which <c>saml-login.spec.ts</c> asserts against the rendered body.
    /// </summary>
    /// <remarks>
    /// <c>msg</c> is caller-controlled, so it is matched against a closed set rather than rendered.
    /// See <see cref="SignInDenialReasons"/> for why encoding alone would not be enough.
    /// </remarks>
    private static IResult HandleAccessDenied(string? msg) =>
        Results.Text(
            AccessDeniedPage.Render(msg),
            "text/html",
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Development-only stub driving the same CompleteSignIn pipeline from a directly supplied
    /// identity — the supported local-dev login. Returns 404 outside Development.
    /// </summary>
    private static async Task<IResult> HandleStubLogin(
        HttpContext httpContext,
        IHostEnvironment env,
        ISignInService signInService,
        string? email,
        string? groups,
        string? name,
        string? returnUrl)
    {
        if (!env.IsDevelopment())
        {
            return Results.NotFound();
        }

        var groupValues = (groups ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var outcome = await signInService.CompleteSignInAsync(
            email, name ?? email, groupValues, httpContext, returnUrl);
        return Results.Redirect(outcome.Location);
    }

    /// <summary>Session probe: identity + privileges for a cookie session, 401 otherwise.</summary>
    private static IResult HandleMe(HttpContext httpContext, ICurrentUserContext currentUser)
    {
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        var displayName = httpContext.User.FindFirstValue(AuthConstants.ClaimTypes.DisplayNameClaim)
                          ?? string.Empty;

        return Results.Ok(new MeResponse(
            currentUser.EdjeId,
            currentUser.Email,
            displayName,
            currentUser.Privileges,
            ImpersonatorInfo.FromClaims(httpContext.User)));
    }
}
