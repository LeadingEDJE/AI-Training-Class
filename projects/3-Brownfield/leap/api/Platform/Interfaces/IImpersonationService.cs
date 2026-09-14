using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using Microsoft.AspNetCore.Http;

namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// Cookie-session impersonation: a SuperAdmin swaps their session for a target identity, with
/// provenance claims, and later restores the original identity from those claims.
/// </summary>
/// <remarks>No token is minted; the swap re-issues the session cookie via <c>HttpContext.SignInAsync</c>.</remarks>
public interface IImpersonationService
{
    /// <summary>
    /// Re-issues the session as <paramref name="targetEdjeId"/> (target claims plus target privileges
    /// with an EDJEr floor), preserving the caller in the impersonation provenance claims.
    /// </summary>
    /// <remarks>Guards against a target that does not exist and against nested impersonation.</remarks>
    Task<ImpersonationOutcome> StartAsync(ClaimsPrincipal currentUser, Guid targetEdjeId, HttpContext httpContext);

    /// <summary>
    /// Restores the original identity from the impersonation cookie's provenance claims and re-issues the
    /// session without a re-login. Answers <see cref="ImpersonationStatus.NotImpersonating"/> when none.
    /// </summary>
    Task<ImpersonationOutcome> StopAsync(ClaimsPrincipal currentUser, HttpContext httpContext);
}

/// <summary>Result status of an impersonation start/stop operation.</summary>
public enum ImpersonationStatus
{
    /// <summary>Impersonation started; the session was re-issued as the target.</summary>
    Started,
    /// <summary>Impersonation stopped; the original identity was restored.</summary>
    Stopped,
    /// <summary>The requested target EdjeId does not resolve to a TPS person.</summary>
    TargetNotFound,
    /// <summary>The session is already impersonating; nested impersonation is not allowed.</summary>
    AlreadyImpersonating,
    /// <summary>Stop was requested on a session that is not impersonating.</summary>
    NotImpersonating,
}

/// <summary>Outcome of an impersonation operation: a status plus the resulting <see cref="MeResponse"/> when successful.</summary>
public sealed record ImpersonationOutcome(ImpersonationStatus Status, MeResponse? Identity);
