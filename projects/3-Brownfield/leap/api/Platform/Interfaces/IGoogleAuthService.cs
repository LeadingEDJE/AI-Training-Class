namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// Pure business logic for Google SAML sign-in: validates a user's email domain and translates the
/// Google Group memberships carried in the SAML assertion into timesheet role strings.
/// </summary>
/// <remarks>No external I/O: the group values arrive in the assertion, so Google is never called.</remarks>
public interface IGoogleAuthService
{
    /// <summary>
    /// Returns <c>true</c> only when the domain after the last '@' is an exact, case-insensitive match
    /// for the configured allowed domain. Rejects null, empty, malformed, foreign and suffix-spoof input.
    /// </summary>
    /// <param name="email">The email address from the SAML assertion.</param>
    bool ValidateDomain(string email);

    /// <summary>
    /// Translates Google Group display names to the distinct set of timesheet role strings from the
    /// configured mapping table.
    /// </summary>
    /// <remarks>
    /// A configured mapping is honoured only when its group name's environment scope matches
    /// <paramref name="isProduction"/>: a bare group name (<c>Compass-Admin</c>) is Production-only, a
    /// <c>-dev</c>-suffixed one (<c>Compass-Admin-dev</c>) is non-Production-only, and out-of-scope
    /// mappings are discarded before matching against the asserted groups (filter before union).
    /// Matching is full-string equality — never substring or prefix — case-insensitive and
    /// whitespace-trimmed on both sides. Unmatched, blank and null group values are ignored, and the
    /// result is deduplicated across every group that survives the environment filter.
    /// </remarks>
    /// <param name="groups">The Google Group display names carried in the SAML assertion.</param>
    /// <param name="isProduction">
    /// Whether the current host environment is Production. Required, with no default, so a caller
    /// can never silently resolve roles in the wrong environment scope.
    /// </param>
    IReadOnlyList<string> MapGroupsToRoles(IEnumerable<string> groups, bool isProduction);
}
