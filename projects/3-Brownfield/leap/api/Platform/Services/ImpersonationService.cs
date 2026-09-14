using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Cookie-session impersonation: re-issues the session cookie as the target identity.
/// </summary>
/// <remarks>
/// <see cref="StartAsync"/> issues the target EdjeId, email and DisplayName plus target privileges
/// (<c>user_roles</c> unioned with an <see cref="RolePolicy.EDJEr"/> floor), stamping
/// <c>Impersonator*</c> and <c>OriginalPrivilege</c> provenance claims so <see cref="StopAsync"/> can
/// rebuild the original identity with no database lookup. Every start and stop is audit-logged with the
/// original actor. The cookie carries the target EdjeId as the primary <c>EdjeId</c> claim, so
/// downstream audit attribution and authorization evaluate the target.
/// </remarks>
public class ImpersonationService(
    IPersonRepository personRepository,
    IUserRoleService roleService,
    IAuditService auditService,
    ILogger<ImpersonationService> logger) : IImpersonationService
{
    /// <inheritdoc />
    public async Task<ImpersonationOutcome> StartAsync(ClaimsPrincipal currentUser, Guid targetEdjeId, HttpContext httpContext)
    {
        // Nested impersonation is not allowed: an already-impersonating session carries provenance claims.
        if (currentUser.FindFirst(AuthConstants.ClaimTypes.ImpersonatorEdjeId) is not null)
        {
            return new ImpersonationOutcome(ImpersonationStatus.AlreadyImpersonating, null);
        }

        var target = await personRepository.GetByEdjeIdAsync(targetEdjeId);
        if (target is null)
        {
            return new ImpersonationOutcome(ImpersonationStatus.TargetNotFound, null);
        }

        var original = CaptureOriginal(currentUser);
        var targetPrivileges = await ResolveTargetPrivilegesAsync(targetEdjeId);
        var targetDisplayName = ResolveDisplayName(target, targetEdjeId);
        var targetEmail = target.Email ?? string.Empty;

        await auditService.LogAsync(new AuditEntry(
            EntityType: "Impersonate",
            EntityId: targetEdjeId.ToString(),
            Action: "create",
            Actor: original.EdjeId.ToString(),
            TriggeredBy: "SuperAdmin",
            Reason: $"Impersonated {targetDisplayName} ({target.Id})",
            Changes: [new FieldChange("Roles", null, string.Join(",", targetPrivileges))]));

        var identity = BuildImpersonatedIdentity(targetEdjeId, targetEmail, targetDisplayName, targetPrivileges, original);
        await httpContext.SignInAsync(
            AuthConstants.Settings.LeadingEdjeAuthenticationType, new ClaimsPrincipal(identity));

        logger.LogInformation(
            "Impersonation started by {Actor} as {Target}",
            LogSanitizer.Clean(original.EdjeId.ToString()),
            LogSanitizer.Clean(targetEdjeId.ToString()));

        var impersonator = new ImpersonatorInfo(original.EdjeId, original.DisplayName, original.Email);
        return new ImpersonationOutcome(
            ImpersonationStatus.Started,
            new MeResponse(targetEdjeId, targetEmail, targetDisplayName, targetPrivileges, impersonator));
    }

    /// <inheritdoc />
    public async Task<ImpersonationOutcome> StopAsync(ClaimsPrincipal currentUser, HttpContext httpContext)
    {
        var originalEdjeId = currentUser.FindFirstValue(AuthConstants.ClaimTypes.ImpersonatorEdjeId);
        if (originalEdjeId is null)
        {
            return new ImpersonationOutcome(ImpersonationStatus.NotImpersonating, null);
        }

        var originalName = currentUser.FindFirstValue(AuthConstants.ClaimTypes.ImpersonatorName) ?? string.Empty;
        var originalEmail = currentUser.FindFirstValue(AuthConstants.ClaimTypes.ImpersonatorEmail) ?? string.Empty;
        var originalPrivileges = currentUser.FindAll(AuthConstants.ClaimTypes.OriginalPrivilege)
            .Select(c => c.Value).ToList();
        var impersonatedEdjeId = currentUser.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim) ?? string.Empty;

        await auditService.LogAsync(new AuditEntry(
            EntityType: "Impersonate",
            EntityId: impersonatedEdjeId,
            Action: "delete",
            Actor: originalEdjeId,
            TriggeredBy: "SuperAdmin",
            Reason: "Stopped impersonation",
            Changes: []));

        var originalGuid = Guid.Parse(originalEdjeId);
        var identity = BuildBaseIdentity(originalGuid, originalEmail, originalName, originalPrivileges);
        await httpContext.SignInAsync(
            AuthConstants.Settings.LeadingEdjeAuthenticationType, new ClaimsPrincipal(identity));

        logger.LogInformation(
            "Impersonation stopped, restored {Actor}",
            LogSanitizer.Clean(originalEdjeId));

        return new ImpersonationOutcome(
            ImpersonationStatus.Stopped,
            new MeResponse(originalGuid, originalEmail, originalName, originalPrivileges, Impersonator: null));
    }

    // Target privileges = local user_roles unioned with the automatic EDJEr floor (a target with no
    // user_roles rows still impersonates as at least an EDJEr, mirroring the sign-in baseline).
    private async Task<IReadOnlyList<string>> ResolveTargetPrivilegesAsync(Guid targetEdjeId)
    {
        var roles = new HashSet<string>(await roleService.GetRolesAsync(targetEdjeId), StringComparer.Ordinal)
        {
            RolePolicy.EDJEr,
        };
        return roles.ToList();
    }

    private static (Guid EdjeId, string DisplayName, string Email, IReadOnlyList<string> Privileges) CaptureOriginal(
        ClaimsPrincipal user)
    {
        var edjeId = user.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim)
            ?? throw new InvalidOperationException("Missing EdjeId claim on the impersonating principal");
        var displayName = user.FindFirstValue(AuthConstants.ClaimTypes.DisplayNameClaim) ?? string.Empty;
        var email = user.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var privileges = user.FindAll(AuthConstants.ClaimTypes.PrivilegeClaim).Select(c => c.Value).ToList();
        return (Guid.Parse(edjeId), displayName, email, privileges);
    }

    private static string ResolveDisplayName(Person target, Guid targetEdjeId)
    {
        var name = $"{target.FirstName} {target.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return !string.IsNullOrWhiteSpace(target.Email) ? target.Email! : targetEdjeId.ToString();
    }

    private static ClaimsIdentity BuildImpersonatedIdentity(
        Guid targetEdjeId,
        string email,
        string displayName,
        IReadOnlyList<string> privileges,
        (Guid EdjeId, string DisplayName, string Email, IReadOnlyList<string> Privileges) original)
    {
        var identity = BuildBaseIdentity(targetEdjeId, email, displayName, privileges);

        // Provenance: preserves the original identity so StopAsync can restore it without a DB lookup.
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.ImpersonatorEdjeId, original.EdjeId.ToString()));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.ImpersonatorName, original.DisplayName));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.ImpersonatorEmail, original.Email));
        foreach (var privilege in original.Privileges)
        {
            identity.AddClaim(new Claim(AuthConstants.ClaimTypes.OriginalPrivilege, privilege));
        }

        return identity;
    }

    private static ClaimsIdentity BuildBaseIdentity(
        Guid edjeId, string email, string displayName, IReadOnlyList<string> privileges)
    {
        var identity = new ClaimsIdentity(AuthConstants.Settings.LeadingEdjeAuthenticationType);
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, edjeId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Email, email));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.DisplayNameClaim, displayName));
        foreach (var privilege in privileges)
        {
            identity.AddClaim(new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, privilege));
        }

        return identity;
    }
}
