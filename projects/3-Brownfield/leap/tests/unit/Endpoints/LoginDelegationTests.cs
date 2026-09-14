using System.Collections.Specialized;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using LeadingEDJE.Leap.Api.Platform.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// HTTP-level tests for the preview-to-dev login delegation on <c>/auth/login</c> (CONTEXT D-02).
/// Previews run Staging with SAML unregistered and cannot register a per-PR Google app, so when
/// <c>Auth:SharedLoginBaseUrl</c> is configured the endpoint 302s to the stable dev host's
/// <c>/auth/login</c> carrying an ABSOLUTE returnUrl back at this (preview) host. When the delegate
/// is NOT configured the existing 503 "SAML not configured" problem is retained — a documented,
/// detectable degraded state (never a silent redirect loop, R-3 / T-45-06).
/// </summary>
public class LoginDelegationTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private const string SharedBase = "https://leap.dev.leadingedje.com";

    private readonly TestWebApplicationFactory _factory = factory;

    /// <summary>No-redirect client with the given <c>Auth:SharedLoginBaseUrl</c> (null = unset).</summary>
    private HttpClient CreateClient(string? sharedLoginBaseUrl)
    {
        var configured = _factory.WithWebHostBuilder(builder =>
        {
            if (sharedLoginBaseUrl is not null)
            {
                builder.UseSetting("Auth:SharedLoginBaseUrl", sharedLoginBaseUrl);
            }
        });

        return configured.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static NameValueCollection LocationQuery(HttpResponseMessage response) =>
        HttpUtility.ParseQueryString(response.Headers.Location!.Query);

    [Fact]
    public async Task Login_SamlOff_WithSharedLoginBaseUrl_DelegatesWithAbsolutizedReturnUrl()
    {
        // Arrange
        var client = CreateClient(SharedBase);

        // Act — relative returnUrl, SAML unregistered.
        var response = await client.GetAsync("/auth/login?returnUrl=%2Footo%2F", TestContext.Current.CancellationToken);

        // Assert — 302 to the configured shared dev host's /auth/login.
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var location = response.Headers.Location!;
        location.Host.ShouldBe("leap.dev.leadingedje.com");
        location.AbsolutePath.ShouldBe("/auth/login");

        // The forwarded returnUrl is absolutized against THIS host's scheme+host and ends with /ooto/.
        var returnUrl = LocationQuery(response)["returnUrl"];
        returnUrl.ShouldNotBeNull();
        returnUrl.ShouldStartWith(client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        returnUrl.ShouldEndWith("/ooto/");
    }

    [Fact]
    public async Task Login_SamlOff_NoReturnUrl_DelegatesWithAbsolutizedRoot()
    {
        // Arrange
        var client = CreateClient(SharedBase);

        // Act — no returnUrl param.
        var response = await client.GetAsync("/auth/login", TestContext.Current.CancellationToken);

        // Assert — returnUrl absolutizes to the host root.
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var returnUrl = LocationQuery(response)["returnUrl"];
        returnUrl.ShouldBe($"{client.BaseAddress!.GetLeftPart(UriPartial.Authority)}/");
    }

    [Fact]
    public async Task Login_SamlOff_AbsoluteReturnUrl_PassedThroughVerbatim()
    {
        // Arrange
        var client = CreateClient(SharedBase);
        const string absolute = "https://somewhere.example/x";

        // Act — an already-absolute returnUrl is forwarded verbatim (the dev host's SafeReturnUrl guards it).
        var response = await client.GetAsync(
            $"/auth/login?returnUrl={Uri.EscapeDataString(absolute)}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        LocationQuery(response)["returnUrl"].ShouldBe(absolute);
    }

    [Fact]
    public async Task Login_SamlOff_NoSharedLoginBaseUrl_Returns503Problem()
    {
        // Arrange — delegate NOT configured.
        var client = CreateClient(sharedLoginBaseUrl: null);

        // Act
        var response = await client.GetAsync("/auth/login?returnUrl=%2Footo%2F", TestContext.Current.CancellationToken);

        // Assert — existing detectable degraded state is retained (never a loop).
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("SAML not configured");
    }

    [Fact]
    public async Task Login_SamlOff_SwitchAccount_ForwardsSwitchAccountFlagToDelegate()
    {
        // Arrange
        var client = CreateClient(SharedBase);

        // Act — switchAccount=true with SAML unregistered (the preview path the reporter tests, #671).
        var response = await client.GetAsync(
            "/auth/login?switchAccount=true&returnUrl=%2Ftimesheet%2F", TestContext.Current.CancellationToken);

        // Assert — the delegate target carries switchAccount=true so the dev host forces the chooser.
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        LocationQuery(response)["switchAccount"].ShouldBe("true");
    }

    [Fact]
    public async Task Login_SwitchAccountLinkOnDeniedPage_IsAcceptedNotBindingError()
    {
        // The wrong-account visitor (#671) clicks the denial page's "Sign in with a different
        // account" link. GET the EXACT href the page ships, so a value the endpoint cannot bind is
        // caught here as the 400 it produces, rather than in front of that visitor.
        var client = CreateClient(SharedBase);
        var html = AccessDeniedPage.Render(SignInDenialReasons.UnauthorizedDomain);
        var href = Regex.Match(html, "href=\"(/auth/login[^\"]*)\"").Groups[1].Value;
        href.ShouldNotBeEmpty();

        // Act
        var response = await client.GetAsync(href, TestContext.Current.CancellationToken);

        // Assert — the handler ran and delegated; not a binding-failure 400.
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        LocationQuery(response)["switchAccount"].ShouldBe("true");
    }

    [Fact]
    public async Task Login_SwitchAccountOne_IsTreatedAsTrue()
    {
        // "1" must force the chooser too: it is the value the recovery instructions hand out, and a
        // bool parameter would 400 on it.
        var client = CreateClient(SharedBase);

        // Act
        var response = await client.GetAsync(
            "/auth/login?switchAccount=1&returnUrl=%2F", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        LocationQuery(response)["switchAccount"].ShouldBe("true");
    }

    [Fact]
    public async Task Login_SamlOff_TrailingSlashBaseUrl_ProducesNoDoubleSlash()
    {
        // Arrange — base URL with a trailing slash.
        var client = CreateClient(SharedBase + "/");

        // Act
        var response = await client.GetAsync("/auth/login?returnUrl=%2Footo%2F", TestContext.Current.CancellationToken);

        // Assert — the delegated path is exactly /auth/login (no // from the trailing slash).
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.AbsolutePath.ShouldBe("/auth/login");
    }
}
