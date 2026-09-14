using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;

namespace LeadingEDJE.Leap.Api.Platform.Dtos;

/// <summary>
/// Session-probe response for <c>/api/me</c> and the impersonation endpoints. <c>Impersonator</c> is
/// non-null only while impersonating, and names the SuperAdmin behind it so the SPA can offer a stop.
/// </summary>
public sealed record MeResponse(
    Guid EdjeId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Privileges,
    ImpersonatorInfo? Impersonator);

/// <summary>
/// Provenance block describing the SuperAdmin behind an active impersonation session, sourced from the
/// impersonation cookie's <c>Impersonator*</c> claims.
/// </summary>
public sealed record ImpersonatorInfo(Guid EdjeId, string DisplayName, string Email)
{
    /// <summary>
    /// Builds an <see cref="ImpersonatorInfo"/> from a principal's impersonation-provenance claims, or
    /// returns <c>null</c> when the principal is not impersonating (no <c>ImpersonatorEdjeId</c> claim).
    /// </summary>
    public static ImpersonatorInfo? FromClaims(ClaimsPrincipal principal)
    {
        var edjeId = principal.FindFirstValue(AuthConstants.ClaimTypes.ImpersonatorEdjeId);
        if (edjeId is null)
        {
            return null;
        }

        var displayName = principal.FindFirstValue(AuthConstants.ClaimTypes.ImpersonatorName) ?? string.Empty;
        var email = principal.FindFirstValue(AuthConstants.ClaimTypes.ImpersonatorEmail) ?? string.Empty;
        return new ImpersonatorInfo(Guid.Parse(edjeId), displayName, email);
    }
}
