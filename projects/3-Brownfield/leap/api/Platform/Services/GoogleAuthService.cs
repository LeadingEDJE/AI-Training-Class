using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.Extensions.Options;

namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Validates a user's email domain and maps the Google Group memberships carried in the SAML assertion
/// to timesheet role strings. Pure business logic: the groups arrive in the assertion, so there is no I/O.
/// </summary>
public class GoogleAuthService(IOptions<GoogleAuthOptions> options) : IGoogleAuthService
{
    private readonly GoogleAuthOptions _options = options.Value;

    /// <inheritdoc />
    public bool ValidateDomain(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(_options.AllowedDomain))
        {
            return false;
        }

        var atIndex = email.LastIndexOf('@');
        if (atIndex < 0 || atIndex == email.Length - 1)
        {
            return false;
        }

        var domain = email[(atIndex + 1)..];
        return string.Equals(domain, _options.AllowedDomain, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> MapGroupsToRoles(IEnumerable<string> groups, bool isProduction)
    {
        if (groups is null)
        {
            return [];
        }

        var assertionGroups = new HashSet<string>(
            groups.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()),
            StringComparer.OrdinalIgnoreCase);

        return _options.Groups
            .Where(m => !string.IsNullOrWhiteSpace(m.GroupName))
            .Where(m => IsHonoredInEnvironment(m.GroupName, isProduction))
            .Where(m => assertionGroups.Contains(m.GroupName.Trim()))
            .Select(m => m.Role)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Environment scope is inferred from the group name itself, never from a separate config flag:
    // every group in this platform follows the "-dev"/"-Dev" suffix convention, so a bare name is
    // Production-only and a suffixed name is non-Production-only. This predicate is what withholds an
    // out-of-scope mapping, and GoogleAuthServiceTests pins it. Its position above the
    // assertion-membership check is readability only; both are commutative Where clauses.
    private static bool IsHonoredInEnvironment(string groupName, bool isProduction) =>
        isProduction != groupName.EndsWith("-dev", StringComparison.OrdinalIgnoreCase);
}
