using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Resolves authorization by checking JWT claims first (IdP-issued), then falling back to local DB roles (additive).
/// </summary>
public class CompositeAuthorizationResolver(IUserRoleService roleService) : IAuthorizationResolver
{
    /// <summary>
    /// Returns true if the user holds any of the specified roles in JWT claims or local DB.
    /// </summary>
    public async Task<bool> HasAnyRoleAsync(ClaimsPrincipal user, params string[] roles)
    {
        // Check JWT claims first (short-circuit -- IdP always wins)
        if (roles.Any(r => user.HasClaim(AuthConstants.ClaimTypes.PrivilegeClaim, r)))
        {
            return true;
        }

        // Then check local DB (additive/more permissive)
        var edjeId = user.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim);
        if (edjeId is null)
        {
            return false;
        }

        return await roleService.HasAnyRoleAsync(Guid.Parse(edjeId), roles);
    }
}
