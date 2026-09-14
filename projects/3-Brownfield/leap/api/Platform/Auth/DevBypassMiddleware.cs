using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.Extensions.Options;

namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// Development-only middleware that injects a fake ClaimsPrincipal from
/// <c>appsettings.Development.json</c> so headless local dev works without a real Google sign-in.
/// </summary>
/// <remarks>
/// Never runs outside Development, and is registered only when <c>Auth:DevBypass:Enabled</c> is
/// true — the E2E stack sets it false so the suite exercises real cookie sessions and
/// unauthenticated requests stay observable rather than masked by the dev profile. Runs after
/// <c>UseAuthentication</c> so a real cookie session is already resolved when the skip checks
/// below evaluate.
/// </remarks>
public class DevBypassMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Middleware entry point: attaches a dev-profile <see cref="ClaimsPrincipal"/> to
    /// <see cref="HttpContext.User"/> unless a real session is already in play.
    /// </summary>
    /// <remarks>
    /// Skips when the request already carries an authenticated identity or a session cookie, so a
    /// stub-login or SAML session always wins over the dev profile and a present-but-invalid session
    /// cookie is never masked by a dev identity. <c>X-Dev-User</c> and <c>X-Dev-Roles</c> headers
    /// override the profile.
    /// </remarks>
    public async Task InvokeAsync(HttpContext context, IOptions<DevBypassOptions> options)
    {
        var alreadyAuthenticated = context.User?.Identity?.IsAuthenticated == true;
        var hasSessionCookie = context.Request.Cookies.ContainsKey(AuthConstants.Settings.SessionCookieName);
        if (alreadyAuthenticated || hasSessionCookie)
        {
            await next(context);
            return;
        }

        var opts = options.Value;

        var profileName = context.Request.Headers["X-Dev-User"].FirstOrDefault()
            ?? opts.DefaultProfile;

        if (!string.IsNullOrEmpty(profileName) && opts.Profiles.TryGetValue(profileName, out var profile))
        {
            // The claim must never be blank, exactly as SignInService.BuildIdentity guarantees for the real
            // sign-in path. Without this the dev identity was the only authenticated identity that could
            // carry an empty display name, and /api/me (which reads the claim with `?? string.Empty`)
            // handed `""` to every consumer — which is how a role="img" avatar shipped with no accessible
            // name.
            var displayName = !string.IsNullOrWhiteSpace(profile.DisplayName)
                ? profile.DisplayName
                : profile.Email;

            var claims = new List<Claim>
            {
                new(AuthConstants.ClaimTypes.EdjeIdClaim, profile.EdjeId.ToString()),
                new(System.Security.Claims.ClaimTypes.Email, profile.Email),
                new(AuthConstants.ClaimTypes.DisplayNameClaim, displayName),
                new(AuthConstants.ClaimTypes.ClientIdClaim, opts.ClientId),
            };

            // X-Dev-Roles header overrides profile privileges for this request
            var rolesHeader = context.Request.Headers["X-Dev-Roles"].FirstOrDefault();
            var privileges = rolesHeader != null
                ? rolesHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : profile.Privileges;

            foreach (var privilege in privileges)
            {
                claims.Add(new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, privilege.Trim()));
            }

            var identity = new ClaimsIdentity(claims, AuthConstants.Settings.LeadingEdjeAuthenticationType);
            context.User = new ClaimsPrincipal(identity);

            // Seed user_roles table from dev profile privileges
            var roleService = context.RequestServices.GetRequiredService<IUserRoleService>();
            await roleService.SyncFromProfileAsync(profile.EdjeId, privileges);
        }

        await next(context);
    }
}
