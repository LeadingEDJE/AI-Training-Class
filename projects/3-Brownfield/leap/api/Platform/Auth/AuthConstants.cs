namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// Claim type and authentication scheme constants. Defined locally because the LeadingEdje.Core
/// package ships only TimeService; the strings must match the IdP claim format exactly.
/// </summary>
public static class AuthConstants
{
    /// <summary>Claim type strings emitted by the LeadingEDJE IdP on authenticated requests.</summary>
    public static class ClaimTypes
    {
        /// <summary>Employee's EdjeId GUID claim (primary user identifier across LeadingEDJE systems).</summary>
        public const string EdjeIdClaim = "EdjeId";
        /// <summary>Client (OAuth application) identifier that issued the token.</summary>
        public const string ClientIdClaim = "ClientId";
        /// <summary>Fine-grained privilege string (repeated claim — one entry per privilege).</summary>
        public const string PrivilegeClaim = "Privilege";
        /// <summary>Role name claim (repeated claim — one entry per role).</summary>
        public const string RoleClaim = "Role";
        /// <summary>Human-readable display name of the authenticated user.</summary>
        public const string DisplayNameClaim = "DisplayName";
        /// <summary>EdjeId of the SuperAdmin who initiated the current impersonation session (provenance).</summary>
        public const string ImpersonatorEdjeId = "ImpersonatorEdjeId";
        /// <summary>Display name of the impersonating SuperAdmin (provenance, restored on stop).</summary>
        public const string ImpersonatorName = "ImpersonatorName";
        /// <summary>Email of the impersonating SuperAdmin (provenance, restored on stop).</summary>
        public const string ImpersonatorEmail = "ImpersonatorEmail";
        /// <summary>A privilege held by the original identity before impersonation (repeated claim; restored on stop).</summary>
        public const string OriginalPrivilege = "OriginalPrivilege";
    }

    /// <summary>SAML per-request customization keys for the switch-account (ForceAuthn) path.</summary>
    public static class Saml
    {
        /// <summary>Relay-data/AuthenticationProperties key that makes the AuthnRequest set ForceAuthn so Google re-prompts.</summary>
        public const string SwitchAccountRelayKey = "forceAuthn";
        /// <summary>Query flag on <c>/auth/login</c> requesting Google's account chooser.</summary>
        public const string SwitchAccountQueryFlag = "switchAccount";
    }

    /// <summary>Authentication scheme settings for the cookie-session / Google SAML pipeline.</summary>
    public static class Settings
    {
        /// <summary>ASP.NET Core authentication scheme name for the primary cookie session (default scheme).</summary>
        public const string LeadingEdjeAuthenticationType = "LeadingEdjeAuthenticationType";
        /// <summary>Primary session cookie name issued after a successful CompleteSignIn.</summary>
        public const string SessionCookieName = "Timesheet";
        /// <summary>
        /// Short-lived holding-cookie scheme the raw SAML assertion signs into rather than the session,
        /// so a denied identity holds none. The sign-in pipeline mints the session after validation.
        /// </summary>
        public const string ExternalScheme = "saml-external";
        /// <summary>External holding cookie name for <see cref="ExternalScheme"/>.</summary>
        public const string ExternalCookieName = "Timesheet-External";
    }
}
