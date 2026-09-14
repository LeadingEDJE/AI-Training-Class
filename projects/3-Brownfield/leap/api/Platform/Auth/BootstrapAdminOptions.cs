namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// Per-environment bootstrap SuperAdmin list, bound from the <c>Bootstrap</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="SuperAdmins"/> holds the exact email addresses granted <c>RolePolicy.SuperAdmin</c>
/// (plus a person row) by <c>BootstrapSuperAdminSeeder</c> at startup. Matching is exact-string,
/// case-insensitive equality — no wildcard, prefix, suffix or domain matching. Unset or empty is a
/// complete no-op, which is why no <c>Bootstrap</c> section ships in any <c>appsettings</c> file.
/// The list is supplied per environment through in-repo Helm runtime values
/// (<c>Bootstrap__SuperAdmins__0</c>, …) and is deliberately not a secret: a privilege-grant surface
/// must stay PR-reviewable and auditable in git. The section is named <c>Bootstrap</c> because
/// <see cref="AuthReturnUrlOptions"/> already binds <c>Auth</c>.
/// </remarks>
public class BootstrapAdminOptions
{
    /// <summary>The configuration section this options class binds from.</summary>
    public const string SectionName = "Bootstrap";

    /// <summary>
    /// Gets or sets the email addresses granted <c>SuperAdmin</c> at startup. Empty/unset = no-op.
    /// </summary>
    public string[] SuperAdmins { get; set; } = [];
}
