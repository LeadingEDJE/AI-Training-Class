namespace LeadingEDJE.Leap.Api.Platform.Services.Logging;

/// <summary>
/// Sanitizes untrusted input before it lands in an ILogger placeholder: strips CR/LF (the CodeQL
/// cs/log-forging primitive), all C0 controls and DEL (0x7F), and bounds length to prevent flooding.
/// </summary>
/// <remarks>
/// Apply at the call-site boundary whenever a log placeholder value is
/// attacker-controllable (HTTP headers, request bodies, Slack payloads,
/// email fields, TPS-provided data).
/// </remarks>
public static class LogSanitizer
{
    private const int DefaultMaxLength = 256;

    /// <summary>Sanitize a potentially-untrusted value for safe logging.</summary>
    public static string Clean(string? input, int maxLen = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // CodeQL-recognized primitive: explicit newline strips satisfy the taint query.
        var stripped = input.Replace("\n", string.Empty).Replace("\r", string.Empty);

        // Defense-in-depth: strip all C0 controls (< 0x20) and DEL (0x7F).
        var filtered = new string(stripped.Where(c => c >= 0x20 && c != 0x7F).ToArray());

        // Length bound with ellipsis marker.
        if (filtered.Length > maxLen)
        {
            return filtered[..maxLen] + "…";
        }

        return filtered;
    }

    /// <summary>
    /// Redacts an email address for logging: the local part is replaced, the domain is kept, and a
    /// short stable pseudonym distinguishes one recipient from another.
    /// </summary>
    /// <remarks>
    /// The domain stays because an operator needs to know whether a notice reached a colleague or
    /// somewhere it should not have, and it identifies nobody on its own; the local part is the
    /// identifying half that CodeQL <c>cs/exposure-of-sensitive-information</c> objects to. The
    /// pseudonym exists because nearly every recipient is <c>@leadingedje.com</c>; see
    /// <see cref="PseudonymKey"/>. A privacy improvement, not a CodeQL fix — the taint tracker follows
    /// the address through this method anyway, so never add redaction to a sink and mark the alert
    /// resolved; only keeping address-derived values out of logs entirely closes those. Includes
    /// <see cref="Clean"/>; a value with no <c>@</c> is masked entirely.
    /// </remarks>
    public static string RedactEmail(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var pseudonym = Pseudonym(input);

        // Last '@', not first: a quoted local part is allowed to contain one, and splitting on the
        // first would leak part of it into what we then print as the "domain".
        var at = input.LastIndexOf('@');
        if (at < 0)
        {
            return $"***#{pseudonym}";
        }

        // Lower-cased: DNS is case-insensitive, so the same domain must render one way or two lines
        // about one recipient read as two domains. The pseudonym already normalises for the same
        // reason; without this the hashes would match while the printed strings differed.
        var domain = Clean(input[(at + 1)..], maxLen: 128).ToLowerInvariant();

        return $"***@{domain}#{pseudonym}";
    }

    /// <summary>
    /// Per-process random key for <see cref="Pseudonym"/>. Generated in memory at first use and never
    /// written anywhere.
    /// </summary>
    /// <remarks>
    /// A plain digest is not enough: an unsalted eight-hex truncation is a dictionary lookup against a
    /// small guessable corpus, and 805 <c>first.last@leadingedje.com</c>-shaped candidates recovered a
    /// real address in under a millisecond. Keying with a secret the log reader does not have leaves
    /// nothing to enumerate against. Random per process, deliberately: two lines about one recipient
    /// correlate within a process's logs but not across a restart or between pods. A configured key
    /// would buy that, at the cost of a secret to distribute, rotate and leak, so cross-process
    /// correlation is a deliberate change with a key-management story attached, not a default.
    /// </remarks>
    private static readonly byte[] PseudonymKey =
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    /// <summary>Eight hex characters of keyed HMAC-SHA256 over the lower-cased address.</summary>
    private static string Pseudonym(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();
        var digest = System.Security.Cryptography.HMACSHA256.HashData(
            PseudonymKey, System.Text.Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(digest)[..8].ToLowerInvariant();
    }
}
