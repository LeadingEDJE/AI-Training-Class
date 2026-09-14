using System.Net;
using LeadingEDJE.Leap.Api.Platform.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Acceptance coverage for the config-gated session-cookie <c>Domain</c> attribute (Phase 45, D-02 —
/// a port of TPS's <c>AuthDomain</c> semantics). Drives the Development-only <c>/auth/stub-login</c>
/// endpoint (the same <c>CompleteSignIn</c> pipeline SAML uses) and inspects the raw <c>Set-Cookie</c>
/// header so the parent-domain widening is observable end-to-end. The dot-prefix gate is the whole
/// point: a value like <c>.dev.leadingedje.com</c> lets the dev-issued cookie ride to the
/// <c>*-pr-N</c> preview siblings, while an unset / non-dot value keeps the cookie host-only so
/// localhost and dev-machine flows are byte-identical to pre-Phase-45 behavior. The short-lived
/// external holding cookie must NEVER carry a Domain — it must not travel cross-host.
/// </summary>
public class CookieDomainTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory = factory;

    private const string SignInEmail = "dev@leadingedje.com";
    private const string DotPrefixedDomain = ".dev.leadingedje.com";

    // A Development host (so /auth/stub-login is mapped) with a valid domain, optionally carrying
    // the Auth:CookieDomain config. AllowAutoRedirect=false so the 302 and its Set-Cookie headers are
    // observable. No person needs seeding: IPersonProvisioningService auto-creates an active row for
    // any domain-validated email with no existing one, which is what SignInEmail exercises here.
    private HttpClient CreateDevClient(string? cookieDomain)
    {
        var configured = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("GoogleAuth:AllowedDomain", "leadingedje.com");
            if (cookieDomain is not null)
            {
                builder.UseSetting("Auth:CookieDomain", cookieDomain);
            }
        });

        // HandleCookies=false so Set-Cookie headers survive on the response instead of being
        // swallowed into the client's cookie container (which would strip them from Headers).
        return configured.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    private static async Task<HttpResponseMessage> StubLoginAsync(HttpClient client) =>
        await client.GetAsync(
            $"/auth/stub-login?email={Uri.EscapeDataString(SignInEmail)}",
            TestContext.Current.CancellationToken);

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var headers) ? headers : [];

    // Match the session cookie EXACTLY (name + '='): "Timesheet=" excludes "Timesheet-External=".
    private static string? SessionSetCookie(HttpResponseMessage response) =>
        SetCookies(response).FirstOrDefault(c =>
            c.StartsWith(AuthConstants.Settings.SessionCookieName + "=", StringComparison.Ordinal));

    private static string? ExternalSetCookie(HttpResponseMessage response) =>
        SetCookies(response).FirstOrDefault(c =>
            c.StartsWith(AuthConstants.Settings.ExternalCookieName + "=", StringComparison.Ordinal));

    private static bool HasDomainAttribute(string setCookieHeader) =>
        setCookieHeader.Contains("domain=", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task StubLogin_WithDotPrefixedCookieDomain_SessionCookieCarriesDomain()
    {
        // Arrange
        var client = CreateDevClient(DotPrefixedDomain);

        // Act
        var response = await StubLoginAsync(client);

        // Assert — the dot-prefixed value widens the session cookie to the parent domain.
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var session = SessionSetCookie(response);
        session.ShouldNotBeNull();
        session.ShouldContain("domain=.dev.leadingedje.com", Case.Insensitive);
    }

    [Fact]
    public async Task StubLogin_WithoutCookieDomain_SessionCookieHasNoDomain()
    {
        // Arrange — no config: local behavior, host-only cookie.
        var client = CreateDevClient(cookieDomain: null);

        // Act
        var response = await StubLoginAsync(client);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var session = SessionSetCookie(response);
        session.ShouldNotBeNull();
        HasDomainAttribute(session).ShouldBeFalse();
    }

    [Fact]
    public async Task StubLogin_WithNonDotPrefixedCookieDomain_SessionCookieHasNoDomain()
    {
        // Arrange — a misconfiguration (no leading dot). The dot-prefix gate must reject it and
        // fall back to a host-only cookie rather than emitting a host-scoped Domain attribute.
        var client = CreateDevClient("dev.leadingedje.com");

        // Act
        var response = await StubLoginAsync(client);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var session = SessionSetCookie(response);
        session.ShouldNotBeNull();
        HasDomainAttribute(session).ShouldBeFalse();
    }

    [Fact]
    public async Task StubLogin_WithCookieDomain_ExternalCookieNeverCarriesDomain()
    {
        // Arrange — even with the session domain configured, the external holding cookie must stay
        // host-only. CompleteSignIn always signs out the external scheme, which emits a deletion
        // Set-Cookie for the external cookie name — that header must carry no Domain attribute.
        var client = CreateDevClient(DotPrefixedDomain);

        // Act
        var response = await StubLoginAsync(client);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var external = ExternalSetCookie(response);
        external.ShouldNotBeNull();
        HasDomainAttribute(external).ShouldBeFalse();
    }
}
