namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Provenance constants recorded on <c>people</c> rows the application mints itself (see
/// <see cref="PersonProvisioningService"/>). A <c>null</c> <c>Source</c> means an HR-authoritative row
/// the application did not create.
/// </summary>
public static class PersonSource
{
    /// <summary>A row created by <c>BootstrapSuperAdminSeeder</c> for a configured admin email.</summary>
    public const string BootstrapSeed = "bootstrap-seed";

    /// <summary>A row auto-created on first Google SAML sign-in for a domain-validated user.</summary>
    public const string SamlAutoCreate = "saml-auto-create";
}
