using System.Security.Claims;

namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>Resolves role-based authorization checks against the user's claims and local role store.</summary>
public interface IAuthorizationResolver
{
    /// <summary>
    /// Returns true if <paramref name="user"/> holds any of <paramref name="roles"/>, either via IdP
    /// privileges on the <see cref="ClaimsPrincipal"/> or locally-assigned roles in the <c>UserRole</c> store.
    /// </summary>
    Task<bool> HasAnyRoleAsync(ClaimsPrincipal user, params string[] roles);
}
