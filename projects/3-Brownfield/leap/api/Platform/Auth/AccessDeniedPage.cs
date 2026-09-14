namespace LeadingEDJE.Leap.Api.Platform.Auth;

/// <summary>Renders the LE-branded access-denied page served for every sign-in denial and failure.</summary>
/// <remarks>
/// Self-contained: it is served before any session exists, so it cannot reach the shell's
/// stylesheet, its fonts, or any authenticated asset. The CSS is inlined and the palette is copied
/// from <c>web/shell/styles.css</c> as literal hex, kept in step by hand. The link uses
/// <c>--le-link</c> (#3b63b8) rather than <c>--le-blue</c>, which fails AA as text: brand hues stay
/// fills and borders, never normal-size text on a light surface, and no new colour is invented here.
/// No web font is loaded and none may be added: this page must render with no third-party request.
/// The reason is echoed verbatim and only from the closed set in
/// <see cref="SignInDenialReasons"/>; anything else renders the generic denial.
/// </remarks>
public static class AccessDeniedPage
{
    /// <summary>The support address, plain text rather than a mailto link — matching the shell's banner.</summary>
    private const string Helpdesk = "help@leadingedje.com";

    /// <summary>Headline and guidance for a recognised denial reason.</summary>
    private static (string Headline, string Guidance) CopyFor(string reason) => reason switch
    {
        SignInDenialReasons.UnauthorizedDomain => (
            "That account isn't a Leading EDJE account",
            "LEAP is only open to Leading EDJE accounts. Sign out of the account you just used and "
                + "try again with your Leading EDJE address."),

        SignInDenialReasons.InactivePersonRecord => (
            "Your Leading EDJE record is inactive",
            $"Your account signed in, but the employee record attached to it is marked inactive, so "
                + $"LEAP cannot let you through. If that looks wrong, send a ticket to {Helpdesk}."),

        SignInDenialReasons.NoMatchingPersonRecord => (
            "We couldn't find your Leading EDJE record",
            $"Your account signed in, but there is no employee record attached to it yet. If you are "
                + $"a new starter this usually resolves itself shortly. Send a ticket to {Helpdesk} "
                + "if it does not."),

        SignInDenialReasons.SignInFailed => (
            "Something went wrong signing you in",
            $"We couldn't complete the sign-in. Try again — if it keeps happening, send a ticket to "
                + $"{Helpdesk}."),

        _ => (
            "Access denied",
            $"You don't have access to this page. If you think you should, send a ticket to {Helpdesk}."),
    };

    /// <summary>
    /// Builds the page for <paramref name="requestedReason"/>. Anything outside
    /// <see cref="SignInDenialReasons.All"/>, a crafted <c>msg</c> included, renders the generic denial.
    /// </summary>
    public static string Render(string? requestedReason)
    {
        // Ordinal, case-sensitive, by design: `All.Contains` uses the default string comparer, so a
        // near-miss like "unauthorized domain" is not recognised and falls through to the generic
        // page. Loosening this to a case-insensitive match would widen what an attacker can steer.
        var reason = requestedReason is { } supplied && SignInDenialReasons.All.Contains(supplied)
            ? supplied
            : SignInDenialReasons.Generic;
        var (headline, guidance) = CopyFor(reason);

        // Every interpolated value on this page originates in the constants above, never in the
        // request — see the class remarks. No encoder is applied because nothing here is caller data.
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
              <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <meta name="robots" content="noindex" />
                <title>{{headline}} — Leading EDJE</title>
                <style>
                  :root {
                    --le-green: #98c93d;
                    --le-charcoal: #4c4d4f;
                    --le-bg: #f7f8f9;
                    --le-surface: #ffffff;
                    --le-border: #dde1e5;
                    --le-black: #0e0f10;
                    --le-link: #3b63b8;
                  }
                  * { box-sizing: border-box; }
                  body {
                    margin: 0;
                    background: var(--le-bg);
                    color: var(--le-black);
                    font-family: "Manrope", system-ui, sans-serif;
                    line-height: 1.55;
                  }
                  .topbar {
                    display: flex;
                    align-items: center;
                    padding: 0.75rem 1.5rem;
                    background: var(--le-charcoal);
                    color: #fff;
                  }
                  .logo { font-size: 1.1875rem; letter-spacing: 0.01em; white-space: nowrap; }
                  .logo-edje { font-weight: 800; color: var(--le-green); }
                  .logo-divider {
                    margin-left: 0.625rem;
                    padding-left: 0.875rem;
                    border-left: 1px solid rgba(255, 255, 255, 0.3);
                    font-weight: 600;
                  }
                  main { max-width: 34rem; margin: 0 auto; padding: 3rem 1.5rem; }
                  .card {
                    background: var(--le-surface);
                    border: 1px solid var(--le-border);
                    border-radius: 10px;
                    padding: 2rem;
                  }
                  h1 { margin: 0 0 0.75rem; font-size: 1.5rem; line-height: 1.25; }
                  p { margin: 0 0 1rem; color: var(--le-charcoal); }
                  .reason { margin: 0; font-size: 0.8125rem; color: var(--le-charcoal); }
                  .reason-label { font-weight: 700; }
                  a { color: var(--le-link); }
                  .actions { margin: 1.5rem 0 1rem; }
                  hr { border: 0; border-top: 1px solid var(--le-border); margin: 1.5rem 0; }
                  @media (max-width: 640px) { main { padding: 1.5rem 1rem; } .card { padding: 1.25rem; } }
                </style>
              </head>
              <body>
                <header class="topbar">
                  <span class="logo" aria-label="Leading EDJE LEAP">
                    leading<span class="logo-edje">EDJE</span><span class="logo-divider">LEAP</span>
                  </span>
                </header>
                <main>
                  <div class="card">
                    <h1>{{headline}}</h1>
                    <p>{{guidance}}</p>
                    <p class="actions"><a href="/auth/login?switchAccount=true">Sign in with a different account</a></p>
                    <hr />
                    <p class="reason"><span class="reason-label">Reason:</span> {{reason}}</p>
                  </div>
                </main>
              </body>
            </html>
            """;
    }
}
