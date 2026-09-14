using System.Security.Cryptography;
using System.Text;

namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// Configuration for the migration principal's bearer token. Bound from <c>Auth:MigrationPrincipal</c>.
/// </summary>
/// <remarks>
/// Absent configuration means the scheme is never registered rather than registered-but-rejecting,
/// so a deployment that runs no migrations gains no new authentication surface. The token is
/// supplied by reference and the repository never holds the value: the stable dev environment reads
/// it from the <c>TPS_MIGRATION_TOKEN</c> repository secret, injected into the chart Secret and
/// reaching the container through <c>envFrom</c>; an unset or misspelled secret renders an empty
/// value, the unconfigured case. Production is not wired yet, and when it is the token belongs in
/// Secrets Manager under the mandatory <c>/chat-edje/&lt;env&gt;/</c> prefix — a bare name is
/// AccessDenied and blocks the rollout. Do not widen the IAM policy around a naming mistake.
/// </remarks>
public sealed class MigrationPrincipalOptions
{
    /// <summary>The shared secret a migration run presents as a bearer token.</summary>
    /// <remarks>Never logged. Never defaulted — an unset value disables the scheme.</remarks>
    public string? Token { get; set; }

    /// <summary>Whether a usable token is configured.</summary>
    /// <remarks>
    /// Whitespace counts as unset: an empty string in configuration is a missing value, and treating
    /// it as configured would register a scheme that authenticates nobody while looking enabled.
    /// </remarks>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token);

    /// <summary>Whether a presented token matches the configured one.</summary>
    /// <param name="presented">The token from the request's <c>Authorization</c> header.</param>
    /// <returns><c>true</c> only when the scheme is configured and the tokens match exactly.</returns>
    /// <remarks>
    /// Compared in fixed time via <see cref="CryptographicOperations.FixedTimeEquals"/>. Ordinary
    /// string equality returns at the first differing byte, so its timing leaks how many leading
    /// bytes were correct — enough to recover a secret one byte at a time across many requests. The
    /// unconfigured case short-circuits to <c>false</c> before any comparison, so an unset token can
    /// never be matched by an empty presented one.
    /// </remarks>
    public bool Matches(string? presented)
    {
        if (!IsConfigured || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(Token!);
        var presentedBytes = Encoding.UTF8.GetBytes(presented);

        return CryptographicOperations.FixedTimeEquals(expectedBytes, presentedBytes);
    }
}
