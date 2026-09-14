namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Derives a best-effort display name from an email local part, for app-minted <c>people</c> rows that
/// have no better source of a name yet.
/// </summary>
/// <remarks>
/// Used by <c>BootstrapSuperAdminSeeder</c> so a configured admin is not listed with a blank name in
/// Admin -> People before they have ever signed in. What it returns is a placeholder: the seeded row
/// carries a non-null provenance <c>Source</c>, so <c>IPersonProvisioningService</c> replaces it with
/// the authoritative Google assertion name on first sign-in.
/// Deliberately conservative. Derivation fires only when the local part clearly encodes name tokens
/// separated by <c>.</c>, <c>_</c> or <c>-</c>; anything ambiguous — no separator, any digit, a
/// single-character token, or a role-account prefix — returns <c>null</c> and the row stays
/// identity-only. A wrong name in a people list is worse than a blank one.
/// </remarks>
public static class EmailDerivedName
{
    private static readonly char[] TokenSeparators = ['.', '_', '-'];

    /// <summary>
    /// First tokens that mark a role or service mailbox rather than a person. <c>no</c> covers
    /// <c>no-reply</c> and <c>do</c> covers <c>do-not-reply</c>, which would otherwise pass every check.
    /// </summary>
    private static readonly HashSet<string> NonPersonPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "no", "do", "noreply", "donotreply", "admin", "administrator", "svc", "service", "support",
        "info", "help", "sales", "postmaster", "webmaster", "mailer", "bounce", "notifications",
    };

    /// <summary>
    /// Returns a title-cased display name derived from the local part of <paramref name="email"/>, or
    /// <c>null</c> when the local part does not unambiguously encode a person's name.
    /// </summary>
    public static string? TryDerive(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var atIndex = email.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0)
        {
            // No '@' at all, or an empty local part: not something to guess a name from.
            return null;
        }

        var localPart = email[..atIndex].Trim();
        var tokens = localPart.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);

        // A single token cannot be split into first/last without guessing where the surname starts.
        if (tokens.Length < 2)
        {
            return null;
        }

        if (NonPersonPrefixes.Contains(tokens[0]))
        {
            return null;
        }

        foreach (var token in tokens)
        {
            // Every token must read as a name fragment: letters only, at least two of them.
            if (token.Length < 2 || !token.All(char.IsLetter))
            {
                return null;
            }
        }

        return string.Join(' ', tokens.Select(TitleCase));
    }

    private static string TitleCase(string token) =>
        char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
}
