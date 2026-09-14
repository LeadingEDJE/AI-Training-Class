using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Authorization;

namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// The named system identity that bulk migrations write as.
/// </summary>
/// <remarks>
/// No other authentication path serves a script in a deployed environment: SAML needs a browser,
/// and <c>DevBypass</c> and <c>/auth/stub-login</c> are hard-off outside Development. Principle VIII
/// also requires bulk and migration writes to be attributed to a named system principal,
/// distinguishable from operator activity. Authority is Compass-only and is exactly one privilege,
/// the Compass root: Principle IV fails open, so an identifier resolving to one of timesheet's nine
/// bare role strings would silently grant that role in timesheet, and
/// <c>MigrationPrincipalAuthenticationTests</c> asserts none appear. The privilege is a claim, not a
/// <c>user_roles</c> row, so this identity resolves with no query in an empty environment.
/// </remarks>
public static class MigrationPrincipal
{
    /// <summary>The authentication scheme name.</summary>
    public const string Scheme = "MigrationPrincipal";

    /// <summary>
    /// The policy scheme that routes a request to <see cref="Scheme"/> or to the cookie.
    /// </summary>
    /// <remarks>
    /// Registered as the DEFAULT authenticate scheme, and only when a migration token is configured.
    /// It exists so the authorization policies can stay scheme-less: a policy that names schemes
    /// re-authenticates through them and discards the user DevBypass set, which breaks local
    /// development on every Compass route. See the block in <c>Program.cs</c>.
    /// </remarks>
    public const string ForwardingScheme = "MigrationPrincipalForwarding";

    /// <summary>
    /// Display name for the principal. Deliberately not an email address and not a person's name —
    /// an audit reader must be able to tell at a glance that no human made this write.
    /// </summary>
    public const string Name = "tps-migration (system)";

    /// <summary>
    /// The <c>TriggeredBy</c> value stamped on every audit entry this principal produces.
    /// </summary>
    /// <remarks>
    /// Must differ from the operator value <c>"Compass Admin"</c> that the Compass configuration
    /// services stamp; Principle VIII's "distinguishable from operator activity" is exactly this
    /// distinction, and it is asserted by a test rather than left to convention.
    /// </remarks>
    public const string AuditTriggeredBy = "TPS Migration";

    /// <summary>
    /// The stable identifier the audit trail records as the actor.
    /// </summary>
    /// <remarks>
    /// A fixed sentinel rather than a generated value: an attributable write is only useful if the
    /// same actor is recognisable across every run and every environment. It corresponds to no
    /// <c>people</c> row and is not meant to — <c>CurrentUserContext.EdjeId</c> only parses the claim.
    /// </remarks>
    public static readonly Guid EdjeId = new("00000010-0000-0000-0000-000000000010");

    /// <summary>Builds the claims principal for an authenticated migration run.</summary>
    /// <returns>A principal carrying the Compass root privilege and nothing else.</returns>
    public static ClaimsPrincipal Create()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, Name),
                new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, EdjeId.ToString()),
                new Claim(AuthConstants.ClaimTypes.DisplayNameClaim, Name),
                // The only privilege. See the Principle IV note in the type remarks before adding
                // a second one — in particular, never one of the nine timesheet role strings.
                new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, RolePolicy.CompassSuperAdminRole),
            ],
            Scheme,
            ClaimTypes.Name,
            AuthConstants.ClaimTypes.RoleClaim
        );

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Whether a principal IS the migration principal, as opposed to merely holding its authority.
    /// </summary>
    /// <param name="user">The principal to test.</param>
    /// <returns><c>true</c> only for the migration identity itself.</returns>
    /// <remarks>
    /// Identity, not role. The SOW service gates the <c>LegacyMigrated</c> validation bypass on this
    /// check; gating on the Compass root role instead would hand that bypass to every Compass Super
    /// Admin, turning a scoped migration allowance into a permanent hole in two partial database
    /// constraints. Principle VIII requires a validation-bypass path to be reachable only by the
    /// migration principal, never through the application surface.
    /// </remarks>
    public static bool IsMigrationPrincipal(ClaimsPrincipal? user)
    {
        var edjeId = user?.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim);
        return Guid.TryParse(edjeId, out var parsed) && parsed == EdjeId;
    }
}
