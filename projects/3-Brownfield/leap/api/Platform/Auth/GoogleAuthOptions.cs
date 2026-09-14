namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// Business configuration for Google SAML authorization, bound from the <c>GoogleAuth</c> section:
/// the allowed email domain and the Google-group-to-role mappings.
/// </summary>
/// <remarks>All-or-nothing — an empty <see cref="AllowedDomain"/> rejects every sign-in.</remarks>
public class GoogleAuthOptions
{
    /// <summary>
    /// Gets or sets the email domain allowed to sign in (e.g. "leadingedje.com").
    /// </summary>
    public string AllowedDomain { get; set; } = "";

    /// <summary>
    /// Gets or sets the Google-group-to-role mappings.
    /// </summary>
    public List<GroupRoleMapping> Groups { get; set; } = [];
}

/// <summary>
/// A single mapping from a Google Group display name to a timesheet role string.
/// </summary>
public class GroupRoleMapping
{
    /// <summary>
    /// Gets or sets the Google Group display name as sent in the SAML "Group membership" attribute.
    /// Google sends the display name, not the group email, so it must match exactly, case-insensitively.
    /// </summary>
    public string GroupName { get; set; } = "";

    /// <summary>
    /// Gets or sets the timesheet role granted by membership in the group. Must be one of the
    /// <see cref="Platform.Authorization.RolePolicy"/> role constants (e.g. "Admin", "SuperAdmin").
    /// </summary>
    public string Role { get; set; } = "";
}
