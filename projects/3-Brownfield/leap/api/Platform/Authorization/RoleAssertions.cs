using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace LeadingEDJE.Leap.Api.Platform.Authorization;

/// <summary>
/// Shared policy assertion that resolves roles through the platform's authorization resolver
/// (SAML/claims union plus the local roles table).
/// </summary>
/// <remarks>
/// A module's registration extension builds its policies through this, not through its own copy of
/// the assertion. A copy drifts from the platform's role resolution — missing the database fallback,
/// say — and an authorization difference that silent is the worst kind.
/// </remarks>
public static class RoleAssertions
{
    /// <summary>
    /// Builds an assertion that passes when the current principal holds ANY of the given role strings.
    /// </summary>
    public static Func<AuthorizationHandlerContext, Task<bool>> RequireLocalRole(params string[] roles)
        => async ctx =>
        {
            var httpContext = (ctx.Resource as HttpContext)
                ?? throw new InvalidOperationException("No HttpContext");
            var resolver = httpContext.RequestServices.GetRequiredService<IAuthorizationResolver>();
            return await resolver.HasAnyRoleAsync(ctx.User, roles);
        };
}
