namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// Options for the Google SAML SSO integration, bound from the <c>Saml2</c> configuration section.
/// </summary>
/// <remarks>
/// All-or-nothing registration: with the SP <see cref="EntityId"/> or the IdP entity id and metadata
/// missing, the SAML handler is not registered and the app still boots SAML-off. This is our own
/// configuration POCO, distinct from <c>Sustainsys.Saml2.AspNetCore2</c>'s type of the same name;
/// where both namespaces are in scope, reference this one with its namespace to disambiguate.
/// </remarks>
public class Saml2Options
{
    /// <summary>
    /// Gets or sets the service-provider entity id for timesheet (e.g. "https://&lt;host&gt;/Saml2").
    /// </summary>
    public string EntityId { get; set; } = "";

    /// <summary>
    /// Gets or sets the name of the SAML assertion attribute that carries the user's Google Group
    /// memberships (configured by the Workspace admin on the SAML app). Defaults to "groups".
    /// </summary>
    public string GroupsAttributeName { get; set; } = "groups";

    /// <summary>
    /// Gets or sets the identity-provider (Google) settings.
    /// </summary>
    public Saml2IdentityProviderOptions IdentityProvider { get; set; } = new();
}

/// <summary>
/// Identity-provider (Google) settings for the SAML integration.
/// </summary>
public class Saml2IdentityProviderOptions
{
    /// <summary>
    /// Gets or sets the Google IdP entity id from the SAML app.
    /// </summary>
    public string EntityId { get; set; } = "";

    /// <summary>
    /// Gets or sets the location (URL or local file path) of the Google IdP metadata, which carries
    /// the IdP signing certificate — so no separate certificate value is needed.
    /// </summary>
    public string MetadataLocation { get; set; } = "";

    /// <summary>
    /// Gets or sets the raw IdP metadata XML, for environments where it arrives as a secret. It wins
    /// over <see cref="MetadataLocation"/>.
    /// </summary>
    /// <remarks>Sustainsys accepts only a URL or path, so this is written to a temp file at startup.</remarks>
    public string MetadataXml { get; set; } = "";
}
