using Microsoft.AspNetCore.Http;

namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// The shared post-authentication sign-in pipeline, used by both the real SAML callback and the
/// Development stub-login.
/// </summary>
/// <remarks>
/// It validates the email domain, resolves the person (EdjeId and active), maps Google groups to roles,
/// syncs <c>user_roles</c>, and swaps the external holding cookie for the real session cookie. A denied
/// identity is signed out of both schemes so it never holds a session.
/// </remarks>
public interface ISignInService
{
    /// <summary>
    /// Runs the full pipeline for a validated external identity: on success issues the session cookie and
    /// returns a safe local redirect, on denial redirects to access-denied carrying the reason.
    /// </summary>
    /// <remarks>Signs out both the external and session schemes on denial, and never throws for one.</remarks>
    Task<SignInOutcome> CompleteSignInAsync(
        string? email,
        string? name,
        IEnumerable<string> groups,
        HttpContext httpContext,
        string? returnUrl);
}

/// <summary>
/// Result of a sign-in attempt. <see cref="Location"/> is always a safe redirect target — a local path
/// on success, the access-denied page on denial — and <see cref="DenyReason"/> is set only on denial.
/// </summary>
public sealed record SignInOutcome(bool Succeeded, string Location, string? DenyReason)
{
    /// <summary>A successful sign-in redirecting to the (already validated) local <paramref name="location"/>.</summary>
    public static SignInOutcome Success(string location) => new(true, location, null);

    /// <summary>A denied sign-in redirecting to the access-denied page carrying <paramref name="reason"/>.</summary>
    public static SignInOutcome Deny(string reason) =>
        new(false, $"/auth/access-denied?msg={Uri.EscapeDataString(reason)}", reason);
}
