using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LeadingEDJE.Leap.Api.Platform.Endpoints;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// HTTP-level unit tests for the synchronous <c>AuthEndpoints</c> handlers: the <c>/api/me</c> session
/// probe, the anonymous <c>text/plain</c> signed-out page, and the anonymous <c>text/html</c>
/// access-denied page. The two anonymous pages deliberately differ in kind -- only the denial page was
/// branded (#104), because it is the one an outsider or a deactivated EDJEr is sent to, whereas
/// signing out is an ordinary end to a working session. The SAML callback + logout handlers are async
/// (SAML-driven) and covered by SamlSignInTests (integration).
/// </summary>
public class AuthEndpointsHttpTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Me_WithAuthenticatedSession_ReturnsIdentityWithNullImpersonator()
    {
        // Act — the default TestAuthHandler principal is an authenticated SuperAdmin.
        var response = await _client.GetAsync("/api/me", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("edjeId").GetString().ShouldBe("00000000-0000-0000-0000-000000000001");
        body.GetProperty("email").GetString().ShouldBe("test@leadingedje.com");
        body.GetProperty("privileges").EnumerateArray().Select(e => e.GetString()).ShouldContain("SuperAdmin");
        body.GetProperty("impersonator").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Me_WithPrivilegeOverride_ReflectsExactlyThoseRolesAndStampsDisplayName()
    {
        // Arrange — override the default all-9 privileges with a specific pair, mirroring how a
        // real cookie session carries only its group-mapped roles. Parity check for the claim set
        // CompleteSignIn stamps (EdjeId + email + DisplayName + Privilege-per-role).
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add(TestAuthHandler.PrivilegeOverrideHeader, "Manager,Accounting");

        // Act
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert — DisplayName is stamped and privileges reflect exactly the override set.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("displayName").GetString().ShouldBe("Test User");
        var privileges = body.GetProperty("privileges").EnumerateArray().Select(e => e.GetString()).ToList();
        privileges.ShouldContain("Manager");
        privileges.ShouldContain("Accounting");
        privileges.ShouldNotContain("SuperAdmin");
    }

    [Fact]
    public async Task Me_WithoutAuthenticatedSession_ReturnsUnauthorized()
    {
        // Arrange — force the request to run unauthenticated.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add(TestAuthHandler.AnonymousHeader, "1");

        // Act
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ClearsSessionAndRedirectsToSignedOutPage()
    {
        // Arrange — no-redirect client so the 302 is observable.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/auth/logout", TestContext.Current.CancellationToken);

        // Assert — lands on the server-rendered signed-out page, never a SPA route.
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.ToString().ShouldBe("/auth/signed-out");
    }

    [Fact]
    public async Task SignedOut_ReturnsAnonymousPlainTextPage()
    {
        // Act
        var response = await _client.GetAsync("/auth/signed-out", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.ShouldContain("signed out");
    }

    // ---------------------------------------------------------------- the branded denial page (#104)
    //
    // These four tests replace two that asserted `text.ShouldBe("Inactive Person Record")` against a
    // text/plain body. The status code and the reason text are unchanged and still asserted; what
    // changed is that the reason now arrives inside an LE-branded HTML document (AC 1.5, AC 2.2).

    /// <summary>Every denial reason SignInService can produce, plus the no-message default.</summary>
    public static TheoryData<string, string> DenialReasons() =>
        new()
        {
            { "Unauthorized domain", "Unauthorized domain" },
            { "Inactive Person Record", "Inactive Person Record" },
            { "No Matching Person Record", "No Matching Person Record" },
            { "Sign-in failed", "Sign-in failed" },
        };

    [Theory]
    [MemberData(nameof(DenialReasons))]
    public async Task AccessDenied_ForEachKnownReason_Returns403WithBrandedPageCarryingTheReason(
        string reason, string expectedText)
    {
        // Act
        var response = await _client.GetAsync(
            $"/auth/access-denied?msg={Uri.EscapeDataString(reason)}",
            TestContext.Current.CancellationToken);

        // Assert — status and reason text preserved exactly as the SAML e2e suite asserts them.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.ShouldContain(expectedText);

        // ...and it is a real branded document, not a bare string with a new content type.
        html.ShouldStartWith("<!DOCTYPE html>");
        html.ShouldContain("leading");
        html.ShouldContain("EDJE");
        html.ShouldContain("/auth/login");
    }

    [Fact]
    public async Task AccessDenied_WithoutMessage_Returns403WithBrandedDefaultPage()
    {
        // Act
        var response = await _client.GetAsync("/auth/access-denied", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.ShouldContain("Access denied");
        html.ShouldStartWith("<!DOCTYPE html>");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Your account is locked. Call 1-800-555-0100 to restore access.")]
    [InlineData("\"><img src=x onerror=alert(1)>")]
    public async Task AccessDenied_WithUnrecognisedMessage_RendersTheGenericDenialAndNeverEchoesIt(
        string injected)
    {
        // `msg` is a query parameter, so ANY visitor can craft this URL. The old text/plain page
        // could echo it harmlessly; an HTML page cannot. Encoding alone would stop the script tags
        // but NOT the second case -- a plausible support-desk instruction rendered inside a genuine
        // LE-branded page on the real domain is a phishing primitive, and no amount of escaping
        // makes attacker-authored prose safe. So an unrecognised reason is not rendered at all.

        // Act
        var response = await _client.GetAsync(
            $"/auth/access-denied?msg={Uri.EscapeDataString(injected)}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.ShouldContain("Access denied");
        html.ShouldNotContain("1-800-555-0100");
        html.ShouldNotContain("onerror");
        html.ShouldNotContain("<script>");
    }

    [Fact]
    public void ExtractGroups_WithConfiguredAttribute_ReturnsMatchingClaimValuesCaseInsensitively()
    {
        // Arrange — group claims under a custom attribute name plus an unrelated claim.
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim("Groups", "Timesheet-SuperAdmin-dev"));
        identity.AddClaim(new Claim("groups", "Timesheet-Approval-dev"));
        identity.AddClaim(new Claim("unrelated", "ignored"));

        // Act — attribute matching is case-insensitive.
        var groups = AuthEndpoints.ExtractGroups(new ClaimsPrincipal(identity), "groups").ToList();

        // Assert
        groups.ShouldBe(["Timesheet-SuperAdmin-dev", "Timesheet-Approval-dev"], ignoreOrder: true);
    }

    [Fact]
    public void ExtractGroups_WithBlankAttribute_DefaultsToGroups()
    {
        // Arrange
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim("groups", "Timesheet-Admin-dev"));

        // Act — a null/blank attribute name falls back to the default "groups".
        var groups = AuthEndpoints.ExtractGroups(new ClaimsPrincipal(identity), null).ToList();

        // Assert
        groups.ShouldBe(["Timesheet-Admin-dev"]);
    }
}
