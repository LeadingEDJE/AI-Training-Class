namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// Single fake user profile used by <see cref="DevBypassMiddleware"/> to synthesize an
/// authenticated <see cref="System.Security.Claims.ClaimsPrincipal"/> in local development.
/// </summary>
public class DevUserProfile
{
    /// <summary>EdjeId GUID emitted as the <c>EdjeId</c> claim.</summary>
    public Guid EdjeId { get; set; }
    /// <summary>Email address emitted as the <see cref="System.Security.Claims.ClaimTypes.Email"/> claim.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>
    /// Optional friendly name emitted as the <c>DisplayName</c> claim. When blank the middleware falls
    /// back to <see cref="Email"/>, matching <c>SignInService.BuildIdentity</c> on the real sign-in path.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Privilege strings emitted as repeated <c>Privilege</c> claims (overridable via <c>X-Dev-Roles</c>).</summary>
    public string[] Privileges { get; set; } = [];
}
