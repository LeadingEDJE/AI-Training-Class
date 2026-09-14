using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Shared post-authentication pipeline for Google SAML sign-in.
/// </summary>
/// <remarks>
/// Validates the domain, resolves the person record — auto-creating one for a domain-validated user
/// who has none — maps Google groups to timesheet roles (always granting
/// <see cref="RolePolicy.EDJEr"/>, plus <see cref="RolePolicy.SuperAdmin"/> for any email in
/// <c>Bootstrap:SuperAdmins</c>), syncs <c>user_roles</c>, and swaps the external holding cookie for
/// the real session cookie. Every denial path signs out both schemes so a rejected identity never
/// keeps an authenticated session. Auto-create always mints a real EdjeId, so every session carries a
/// real <c>EdjeId</c> claim and <c>ICurrentUserContext.EdjeId</c> never throws.
/// </remarks>
public class SignInService(
    IGoogleAuthService googleAuth,
    IUserRoleService userRoleService,
    ILogger<SignInService> logger,
    IOptions<AuthReturnUrlOptions> returnUrlOptions,
    IPersonProvisioningService personProvisioning,
    IOptions<BootstrapAdminOptions> bootstrapOptions,
    IHostEnvironment hostEnvironment) : ISignInService
{
    private readonly AuthReturnUrlOptions _returnUrlOptions = returnUrlOptions.Value;
    private readonly BootstrapAdminOptions _bootstrapOptions = bootstrapOptions.Value;

    /// <inheritdoc />
    public async Task<SignInOutcome> CompleteSignInAsync(
        string? email,
        string? name,
        IEnumerable<string> groups,
        HttpContext httpContext,
        string? returnUrl)
    {
        // The domain gate MUST stay the first statement in this method: it is the only thing standing
        // between "any Google account on the internet" and a row in `people` (T-sqc-01).
        if (string.IsNullOrWhiteSpace(email) || !googleAuth.ValidateDomain(email))
        {
            return await DenyAsync(httpContext, SignInDenialReasons.UnauthorizedDomain);
        }

        PersonProvisionResult provisioned;
        try
        {
            // Find-or-create in one call: EnsurePersonAsync reads `people` itself and can tell "no
            // row" from "deactivated row" (D-05/D-06), so there is no separate directory lookup ahead
            // of it. D-08: a created row carries IDENTITY ONLY — no coach, no title category, no
            // delivery-team flag — so HR-dependent features are degraded for such users until the
            // deferred directory import runs. Accepted, and recorded in the SUMMARY.
            provisioned = await personProvisioning.EnsurePersonAsync(
                email, name, PersonSource.SamlAutoCreate);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error provisioning a person record for {Email}",
                LogSanitizer.Clean(email));
            return await DenyAsync(httpContext, SignInDenialReasons.SignInFailed);
        }

        // D-06: an existing deactivated row is still denied — deactivation is never undone, and no
        // second row is created (which the LOWER(email) unique index would reject anyway).
        if (provisioned.Outcome == PersonProvisionOutcome.Inactive)
        {
            return await DenyAsync(httpContext, SignInDenialReasons.InactivePersonRecord);
        }

        if (provisioned.Outcome is not (PersonProvisionOutcome.Created or PersonProvisionOutcome.Existing))
        {
            // Preserve the pre-existing wording for any un-provisionable case.
            return await DenyAsync(httpContext, SignInDenialReasons.NoMatchingPersonRecord);
        }

        logger.LogInformation(
            "Provisioned person record ({Outcome}) for domain-validated {Email} during sign-in",
            LogSanitizer.Clean(provisioned.Outcome.ToString()),
            LogSanitizer.Clean(email));

        var roles = BuildRoles(email, groups);

        // Resolve and persist the display name. Provisioning owns the write and refuses to overwrite
        // an HR-owned name, so its return value — not the raw assertion — is authoritative for the
        // claim. Non-fatal: a failure here must never cost the user their session.
        var displayName = provisioned.DisplayName;
        try
        {
            displayName = await personProvisioning.EnsureDisplayNameAsync(
                provisioned.EdjeId, name, provisioned.DisplayName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not persist the display name for {Email}; continuing with the resolved name",
                LogSanitizer.Clean(email));
        }

        var identity = BuildIdentity(provisioned.EdjeId, provisioned.Email, displayName, roles);

        // Reconcile the local additive role model with the group-derived role set at sign-in.
        await userRoleService.SyncFromProfileAsync(provisioned.EdjeId, roles, "saml-login");

        // Clear the throwaway external-scheme cookie now that its assertion has been validated and
        // copied into the real identity, then establish the session cookie.
        await httpContext.SignOutAsync(AuthConstants.Settings.ExternalScheme);
        await httpContext.SignInAsync(
            AuthConstants.Settings.LeadingEdjeAuthenticationType,
            new ClaimsPrincipal(identity));

        logger.LogInformation(
            "SAML sign-in completed for {Email} with roles {Roles}",
            LogSanitizer.Clean(email),
            LogSanitizer.Clean(string.Join(",", roles)));

        return SignInOutcome.Success(SafeReturnUrl(returnUrl));
    }

    // Mapped group roles unioned with the automatic EDJEr baseline (TPS's baseline-access equivalent:
    // any domain-validated user with a matching person record is at least an EDJEr). Case-insensitive
    // dedup; canonical role strings preserved.
    private IReadOnlyList<string> BuildRoles(string email, IEnumerable<string> groups)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RolePolicy.EDJEr };
        foreach (var role in googleAuth.MapGroupsToRoles(groups, hostEnvironment.IsProduction()))
        {
            roles.Add(role);
        }

        // UserRoleService.SyncFromProfileAsync reconciles rather than appends: after inserting the
        // desired set it re-reads user_roles and deletes every row not in it. The SuperAdmin grant
        // written by BootstrapSuperAdminSeeder is therefore destroyed on the bootstrap admin's first
        // sign-in unless it is re-asserted here. Exact-string, case-insensitive equality against the
        // enumerated config list: no wildcard, no prefix or suffix, no domain matching.
        if (IsBootstrapSuperAdmin(email))
        {
            roles.Add(RolePolicy.SuperAdmin);
        }

        return roles.ToList();
    }

    private bool IsBootstrapSuperAdmin(string email) =>
        _bootstrapOptions.SuperAdmins.Any(configured =>
            !string.IsNullOrWhiteSpace(configured)
            && string.Equals(configured.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

    // EdjeId is authoritative from the person record (detether identity seam). The display name is
    // resolved by the caller via IPersonProvisioningService.EnsureDisplayNameAsync — which reconciles the
    // stored name with the SAML assertion and persists the winner — so this method just stamps it.
    // It is NOT re-derived here: the old local fallback chain preferred person.DisplayName, and that is
    // the EMAIL whenever the row has no name, which silently discarded Google's real name (P2).
    private static ClaimsIdentity BuildIdentity(
        Guid edjeId, string email, string? displayName, IReadOnlyList<string> roles)
    {
        // Belt-and-braces: the claim must never be blank, whatever the caller resolved.
        displayName = !string.IsNullOrWhiteSpace(displayName) ? displayName : email;

        var identity = new ClaimsIdentity(AuthConstants.Settings.LeadingEdjeAuthenticationType);
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, edjeId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Email, email));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.DisplayNameClaim, displayName));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, role));
        }

        return identity;
    }

    // Sign out of BOTH schemes on every denial so a rejected identity never walks away holding an
    // authenticated session of either kind (the external holding cookie OR a stale session cookie).
    private static async Task<SignInOutcome> DenyAsync(HttpContext httpContext, string reason)
    {
        await httpContext.SignOutAsync(AuthConstants.Settings.ExternalScheme);
        await httpContext.SignOutAsync(AuthConstants.Settings.LeadingEdjeAuthenticationType);
        return SignInOutcome.Deny(reason);
    }

    // Open-redirect guard (CWE-601): honors local paths or configured sibling origins. The sibling
    // allowance is config-gated — with Auth:AllowedReturnOrigins and Auth:AllowedReturnHostSuffix unset
    // the guard is local-paths-only. Every rejected non-local target is logged at Warning (sanitized)
    // and falls back to "/". Comparison is always on the parsed Uri (Uri.Compare SchemeAndServer for an
    // exact origin, EndsWith on the parsed Host for the dot-prefixed suffix), never a raw-string match.
    private string SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            return "/";
        }

        // Existing local-path rule: "/", "/path", "~/path"; rejects "//host" and "/\host".
        if (IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }

        // Absolute, https-only. Anything unparseable or non-https is rejected before origin/suffix rules.
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var target)
            || target.Scheme != Uri.UriSchemeHttps)
        {
            return RejectReturnUrl(returnUrl);
        }

        // Exact-origin allow-list: parsed scheme + host + port, never a raw prefix (StartsWith would
        // accept app.dev.leadingedje.com.evil.com).
        foreach (var origin in _returnUrlOptions.AllowedReturnOrigins)
        {
            if (Uri.TryCreate(origin, UriKind.Absolute, out var allowed)
                && Uri.Compare(target, allowed, UriComponents.SchemeAndServer,
                    UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return returnUrl;
            }
        }

        // Dot-prefixed host suffix for dynamic preview hosts (…-pr-N.dev.leadingedje.com). Evaluated on
        // the PARSED host; the mandatory leading dot blocks evildev.leadingedje.com.
        var suffix = _returnUrlOptions.AllowedReturnHostSuffix;
        if (!string.IsNullOrWhiteSpace(suffix) && suffix.StartsWith('.')
            && target.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return returnUrl;
        }

        return RejectReturnUrl(returnUrl);
    }

    private string RejectReturnUrl(string returnUrl)
    {
        logger.LogWarning(
            "Rejected non-local returnUrl {ReturnUrl}; redirecting to '/'", LogSanitizer.Clean(returnUrl));
        return "/";
    }

    internal static bool IsLocalUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        // Allow "/" and "/path" but reject "//host" and "/\host".
        if (url[0] == '/')
        {
            return url.Length == 1 || (url[1] != '/' && url[1] != '\\');
        }

        // Allow "~/path" (app-relative).
        return url.Length > 1 && url[0] == '~' && url[1] == '/';
    }
}
