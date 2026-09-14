namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// Configuration for the post-sign-in open-redirect guard (<c>SignInService.SafeReturnUrl</c>), bound
/// from the <c>Auth</c> section. With both members unset the guard honors local paths only.
/// </summary>
public class AuthReturnUrlOptions
{
    /// <summary>
    /// Gets or sets the exact absolute origins an absolute <c>returnUrl</c> may target. Compared with
    /// <see cref="Uri.Compare"/> on <see cref="UriComponents.SchemeAndServer"/>, never a prefix match.
    /// </summary>
    public string[] AllowedReturnOrigins { get; set; } = [];

    /// <summary>
    /// Gets or sets an optional dot-prefixed host suffix a returnUrl's parsed host may end with, for
    /// preview hostnames an explicit origin list cannot enumerate.
    /// </summary>
    /// <remarks>
    /// The leading dot is mandatory and a suffix without one is inert: it blocks
    /// <c>evildev.leadingedje.com</c> while allowing <c>app-x.dev.leadingedje.com</c>.
    /// </remarks>
    public string? AllowedReturnHostSuffix { get; set; }
}
