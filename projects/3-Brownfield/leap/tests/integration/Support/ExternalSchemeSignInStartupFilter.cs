using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Support;

/// <summary>
/// Test-only middleware that signs a fabricated principal into the SAML external holding-cookie
/// scheme ("saml-external") — exactly what Sustainsys produces after validating a real Google
/// assertion. Registered ONLY in <see cref="SamlSignInTestFactory"/>, never in the app. The test
/// then GETs the real <c>/auth/login-callback</c> carrying that cookie, so the full CompleteSignIn
/// pipeline (domain validation, group→role mapping, person lookup, user_roles sync, cookie swap)
/// runs for real without a live Google IdP. Ported from TPS <c>MySqlIntegrationTestBase</c>.
/// </summary>
public sealed class ExternalSchemeSignInStartupFilter : IStartupFilter
{
    /// <summary>The external holding-cookie scheme name (matches Program.cs AddCookie registration).</summary>
    public const string ExternalScheme = "saml-external";

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Map("/test-only/sign-in-external", branch => branch.Run(async ctx =>
            {
                var email = ctx.Request.Query["email"].ToString();
                // Optional: lets a test assert a specific assertion-supplied display name flows
                // through PersonProvisioningService.EnsureDisplayNameAsync on auto-create. Falls back
                // to the email, matching a real assertion that carries no name attribute.
                var name = ctx.Request.Query["name"].ToString() is { Length: > 0 } n ? n : email;
                var groupsAttribute = ctx.Request.Query["groupsAttribute"].ToString() is { Length: > 0 } ga
                    ? ga
                    : "groups";
                var groups = ctx.Request.Query["groups"].ToString()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var identity = new ClaimsIdentity(ExternalScheme);
                identity.AddClaim(new Claim(ClaimTypes.Email, email));
                identity.AddClaim(new Claim(ClaimTypes.Name, name));
                foreach (var group in groups)
                {
                    identity.AddClaim(new Claim(groupsAttribute, group));
                }

                await ctx.SignInAsync(ExternalScheme, new ClaimsPrincipal(identity));
                ctx.Response.StatusCode = 200;
            }));
            next(app);
        };
    }
}
