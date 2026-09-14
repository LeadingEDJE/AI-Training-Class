using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>
/// Authenticates a bulk migration run presenting the migration principal's bearer token.
/// </summary>
/// <remarks>
/// Registered only when a token is configured — see the conditional block in <c>Program.cs</c> — so
/// it never runs in an environment that does not perform migrations. A missing or non-bearer
/// <c>Authorization</c> header returns <see cref="AuthenticateResult.NoResult"/> rather than a
/// failure, so the cookie scheme still gets its turn and ordinary browser traffic is unaffected. A
/// header that is present but wrong is a genuine failure and produces 401: an operator with a stale
/// token must be told, not silently downgraded to anonymous.
/// </remarks>
/// <param name="options">Scheme options plumbing supplied by the authentication framework.</param>
/// <param name="logger">Logger factory supplied by the authentication framework.</param>
/// <param name="encoder">URL encoder supplied by the authentication framework.</param>
/// <param name="migrationOptions">The configured migration-principal token.</param>
public sealed class MigrationPrincipalAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<MigrationPrincipalOptions> migrationOptions
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var settings = migrationOptions.Value;

        // Defence in depth: Program.cs does not register the scheme when unconfigured, so this
        // should be unreachable. It is here because "unreachable" is a property of today's
        // composition root, and a future refactor that registers unconditionally must not
        // accidentally authenticate anyone.
        if (!settings.IsConfigured)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string? header = Request.Headers.Authorization;

        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var presented = header[BearerPrefix.Length..].Trim();

        if (!settings.Matches(presented))
        {
            // The token itself is never logged, not even truncated. A prefix is enough to make a
            // brute-force meaningfully cheaper, and this line would be written on every attempt.
            Logger.LogWarning("Migration principal authentication failed: token mismatch.");
            return Task.FromResult(AuthenticateResult.Fail("Invalid migration token."));
        }

        var ticket = new AuthenticationTicket(MigrationPrincipal.Create(), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
