using System.Security.Claims;
using System.Text.Encodings.Web;
using LeadingEDJE.Leap.Api.Platform.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadingEDJE.Leap.Api.Tests.Auth;

/// <summary>
/// Test-only authentication handler that injects a SuperAdmin-level ClaimsPrincipal by default.
/// Tests that need to verify privilege-gated behavior can override via the
/// <c>X-Test-Privileges</c> request header (comma-separated privilege names).
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";
    public const string PrivilegeOverrideHeader = "X-Test-Privileges";

    /// <summary>
    /// When present, its value is treated as the impersonating SuperAdmin's EdjeId and the handler
    /// stamps impersonation-provenance claims (<c>ImpersonatorEdjeId</c>/<c>ImpersonatorName</c>/
    /// <c>ImpersonatorEmail</c> + one <c>OriginalPrivilege</c>) so endpoint tests can drive the
    /// "already impersonating" / "stop restores original" branches without a real cookie round-trip.
    /// </summary>
    public const string ImpersonatorOverrideHeader = "X-Test-Impersonator";

    /// <summary>
    /// When present, the handler returns <see cref="AuthenticateResult.NoResult"/> so the request runs
    /// unauthenticated. Lets tests exercise anonymous-endpoint branches (e.g. the /api/me 401 path)
    /// that the default always-SuperAdmin principal would otherwise mask.
    /// </summary>
    public const string AnonymousHeader = "X-Test-Anonymous";

    /// <summary>
    /// When present, the principal is AUTHENTICATED but carries zero <c>Privilege</c> claims, so it earns
    /// 403 rather than 401 (contrast <see cref="AnonymousHeader"/>).
    /// </summary>
    /// <remarks>
    /// This exists because <see cref="PrivilegeOverrideHeader"/> cannot express "no privileges":
    /// <c>HttpClient</c> does not transmit an empty header value, so the override arrives ABSENT and the
    /// handler falls back to <see cref="DefaultPrivileges"/> — all nine timesheet roles, including root.
    /// A test aiming for a role-less principal therefore got the most privileged one available, and for a
    /// Compass assertion the two are indistinguishable (a timesheet root satisfies no Compass policy
    /// either). Added by T005 after that exact false pass was caught by pairing the Compass probe with a
    /// timesheet write.
    /// </remarks>
    public const string NoPrivilegesHeader = "X-Test-No-Privileges";

    /// <summary>
    /// Overrides the identity's email claim.
    /// </summary>
    /// <remarks>
    /// Added for Compass AC-11, whose own-record rule matches the session email against
    /// <c>compass.employee.email</c>. Testing that rule needs both directions — an identity that
    /// matches a seeded EDJEr and one that matches none — and the fixed <c>test@leadingedje.com</c>
    /// claim can only express the first. The fail-closed branch is the one that matters most and the
    /// one hardest to observe, because it returns exactly what a non-owner gets.
    /// </remarks>
    public const string EmailOverrideHeader = "X-Test-Email";

    private static readonly string[] DefaultPrivileges =
    [
        "EDJEr", "Manager", "TimesheetProcessor", "Accounting",
        "HR", "Ops", "PayrollProcessor", "Admin", "SuperAdmin"
    ];

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey(AnonymousHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string[] privileges;
        if (Request.Headers.ContainsKey(NoPrivilegesHeader))
        {
            privileges = [];
        }
        else if (Request.Headers.TryGetValue(PrivilegeOverrideHeader, out var header))
        {
            privileges = header.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        else
        {
            privileges = DefaultPrivileges;
        }

        // Model the exact claim set CompleteSignIn stamps on a real cookie session: EdjeId + email +
        // DisplayName + one Privilege claim per role. (ClientId is retained as a harmless dev-parity
        // claim; the cookie era reads none of the old JWT settings.)
        var claims = new List<Claim>
        {
            new(AuthConstants.ClaimTypes.EdjeIdClaim, "00000000-0000-0000-0000-000000000001"),
            new(ClaimTypes.Email,
                Request.Headers.TryGetValue(EmailOverrideHeader, out var emailOverride)
                && !string.IsNullOrWhiteSpace(emailOverride)
                    ? emailOverride.ToString()
                    : "test@leadingedje.com"),
            new(AuthConstants.ClaimTypes.DisplayNameClaim, "Test User"),
            new(AuthConstants.ClaimTypes.ClientIdClaim, "TESTCLIENT00000000000000000000001"),
        };
        claims.AddRange(privileges.Select(p => new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, p)));

        if (Request.Headers.TryGetValue(ImpersonatorOverrideHeader, out var impersonator)
            && !string.IsNullOrWhiteSpace(impersonator))
        {
            claims.Add(new Claim(AuthConstants.ClaimTypes.ImpersonatorEdjeId, impersonator.ToString()));
            claims.Add(new Claim(AuthConstants.ClaimTypes.ImpersonatorName, "Original Admin"));
            claims.Add(new Claim(AuthConstants.ClaimTypes.ImpersonatorEmail, "original.admin@leadingedje.com"));
            claims.Add(new Claim(AuthConstants.ClaimTypes.OriginalPrivilege, "SuperAdmin"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
