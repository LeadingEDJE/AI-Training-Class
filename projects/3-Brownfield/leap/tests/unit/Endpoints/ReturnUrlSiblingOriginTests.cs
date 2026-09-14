using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Outer acceptance coverage for the sibling-origin open-redirect guard (Phase 45, D-02). Drives the
/// Development-only <c>/auth/stub-login</c> endpoint (the same <c>CompleteSignIn</c> pipeline SAML
/// uses) end-to-end and asserts the 302 Location: with <c>Auth:AllowedReturnHostSuffix</c> configured
/// a matching absolute preview URL is honored; without it the absolute URL falls back to "/". Proves
/// the Helm-runtime <c>Auth__*</c> config path flows into <c>SignInService.SafeReturnUrl</c>.
/// </summary>
public class ReturnUrlSiblingOriginTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory = factory;

    private const string PreviewUrl = "https://app-leap-app-pr-7.dev.leadingedje.com/ooto/";
    private const string DevSuffix = ".dev.leadingedje.com";
    private const string SignInEmail = "dev@leadingedje.com";

    // A Development host (so stub-login is mapped) with a valid domain, optionally carrying the
    // sibling-origin host-suffix config. AllowAutoRedirect=false so the 302 Location is observable.
    // No person needs seeding: IPersonProvisioningService auto-creates an active row for any
    // domain-validated email with no existing one, which is what SignInEmail exercises here.
    private HttpClient CreateDevClient(string? hostSuffix)
    {
        var configured = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("GoogleAuth:AllowedDomain", "leadingedje.com");
            if (hostSuffix is not null)
            {
                builder.UseSetting("Auth:AllowedReturnHostSuffix", hostSuffix);
            }
        });

        return configured.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task StubLogin_WithHostSuffixConfigured_RedirectsToAbsolutePreviewUrl()
    {
        // Arrange
        var client = CreateDevClient(DevSuffix);
        var url = $"/auth/stub-login?email={Uri.EscapeDataString(SignInEmail)}"
                  + $"&returnUrl={Uri.EscapeDataString(PreviewUrl)}";

        // Act
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        // Assert — the preview host matches the configured dot-suffix, so it is honored verbatim.
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.ToString().ShouldBe(PreviewUrl);
    }

    [Fact]
    public async Task StubLogin_WithoutHostSuffixConfigured_FallsBackToRoot()
    {
        // Arrange — no sibling config: the absolute preview URL must not be honored.
        var client = CreateDevClient(hostSuffix: null);
        var url = $"/auth/stub-login?email={Uri.EscapeDataString(SignInEmail)}"
                  + $"&returnUrl={Uri.EscapeDataString(PreviewUrl)}";

        // Act
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.ToString().ShouldBe("/");
    }
}
