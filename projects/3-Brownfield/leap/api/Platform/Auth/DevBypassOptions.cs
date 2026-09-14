namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// Bound to the <c>DevBypass</c> configuration section in <c>appsettings.Development.json</c>;
/// defines the set of fake user profiles the <see cref="DevBypassMiddleware"/> can impersonate.
/// </summary>
public class DevBypassOptions
{
    /// <summary>Profile name used when the request lacks an <c>X-Dev-User</c> header.</summary>
    public string DefaultProfile { get; set; } = string.Empty;
    /// <summary>Synthetic OAuth client id attached to every bypass-authenticated request.</summary>
    public string ClientId { get; set; } = string.Empty;
    /// <summary>Named fake user profiles keyed by profile name (matched against <c>X-Dev-User</c>).</summary>
    public Dictionary<string, DevUserProfile> Profiles { get; set; } = new();
}
