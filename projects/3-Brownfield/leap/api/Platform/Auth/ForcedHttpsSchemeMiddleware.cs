namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// Rewrites <c>Request.Scheme</c> to <c>https</c> for every request. Registered only when
/// <c>Auth:ForceHttpsScheme</c> is true — deployed environments where TLS terminates upstream.
/// </summary>
/// <remarks>
/// The EKS gateway routes <c>/auth</c> and <c>/Saml2</c> straight to the API and overwrites
/// <c>X-Forwarded-Proto</c> with its own http entrypoint scheme, so the ForwardedHeaders middleware
/// cannot recover the real client scheme. An http scheme makes Sustainsys emit an http ACS URL in
/// the AuthnRequest and append its correlation cookie with <c>SameSite=None</c> but no
/// <c>Secure</c> flag, which browsers refuse to store; the ACS post then fails as an unsolicited
/// response. Never enable it where the app is genuinely reachable over plain http — local dev and
/// the mock-SAML E2E stack — because Secure cookies and https URLs break those flows.
/// </remarks>
public class ForcedHttpsSchemeMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Middleware entry point: forces the request scheme to <c>https</c> so everything downstream
    /// (auth handlers, Sustainsys, URL generation) treats the request as TLS-fronted.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        context.Request.Scheme = "https";
        await next(context);
    }
}
