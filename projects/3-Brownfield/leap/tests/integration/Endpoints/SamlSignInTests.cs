using System.Net;
using System.Text.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// Exercises the Google SAML + cookie sign-in pipeline end to end via the real
/// <c>/auth/login-callback</c>. <see cref="ExternalSchemeSignInStartupFilter"/> signs a principal
/// into the SAML external holding cookie (Sustainsys stand-in); the callback then runs group
/// extraction, domain validation, group→role mapping, person auto-provisioning, user_roles sync,
/// and the cookie swap for real against Postgres — no live Google IdP. The deny paths must leave no
/// session.
/// </summary>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured + used across helpers
public class SamlSignInTests : IClassFixture<SamlSignInTestFactory>
{
    private const string SessionCookiePrefix = "Timesheet=";
    private const string ExternalCookiePrefix = "Timesheet-External=";
    private const string SuperAdminGroup = "Timesheet-SuperAdmin-dev";

    private readonly SamlSignInTestFactory _factory;
    private readonly HttpClient _client;

    public SamlSignInTests(SamlSignInTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }
#pragma warning restore IDE0290

    [Fact]
    public async Task LoginCallback_ActivePersonInSuperAdminGroup_IssuesSessionAndMeReturnsIdentityAndPrivileges()
    {
        // Arrange — no pre-seeded person: sign-in auto-provisions one for any domain-validated email,
        // taking the display name from the assertion's name claim.
        await ResetUserRolesAsync();
        var email = $"superadmin+{Guid.NewGuid():N}@leadingedje.com";

        // Act — drive the real callback with a SuperAdmin group membership
        var response = await SignInViaCallbackAsync(email, SuperAdminGroup, name: "Super Admin");

        // Assert — a session cookie is issued and the redirect lands locally, not on AccessDenied
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        DecodedLocation(response).ShouldNotContain("access-denied");
        response.Headers.Location!.ToString().ShouldBe("/");
        var sessionCookie = ExtractSessionCookie(response);
        sessionCookie.ShouldNotBeNull();

        // /api/me with the session cookie carries identity + mapped privileges (incl. automatic EDJEr)
        var me = await GetJsonWithCookieAsync("/api/me", sessionCookie);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        body.GetProperty("email").GetString().ShouldBe(email);
        Guid.Parse(body.GetProperty("edjeId").GetString()!).ShouldNotBe(Guid.Empty);
        body.GetProperty("displayName").GetString().ShouldBe("Super Admin");
        var privileges = body.GetProperty("privileges").EnumerateArray().Select(e => e.GetString()).ToList();
        privileges.ShouldContain("SuperAdmin");
        privileges.ShouldContain("EDJEr");
    }

    [Fact]
    public async Task LoginCallback_ActivePersonWithNoMappedGroups_IssuesSessionWithEdjErOnly()
    {
        // Arrange — no pre-seeded person: sign-in auto-provisions one for any domain-validated email.
        await ResetUserRolesAsync();
        var email = $"baseline+{Guid.NewGuid():N}@leadingedje.com";

        // Act — no groups in the assertion
        var response = await SignInViaCallbackAsync(email, groups: "");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.ToString().ShouldBe("/");
        var sessionCookie = ExtractSessionCookie(response);
        sessionCookie.ShouldNotBeNull();

        var me = await GetJsonWithCookieAsync("/api/me", sessionCookie);
        var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        var privileges = body.GetProperty("privileges").EnumerateArray().Select(e => e.GetString()).ToList();
        privileges.ShouldContain("EDJEr");
        privileges.ShouldNotContain("SuperAdmin");
        privileges.ShouldNotContain("Admin");
    }

    [Fact]
    public async Task LoginCallback_UnknownEmailWithNoPersonRow_AutoCreatesPersonAndIssuesSession()
    {
        // Arrange — quick task 260729-sqc (D-05). Nothing in the directory stub and no `people` row:
        // this is EXACTLY the deployed first-sign-in case that previously dead-ended at "No Matching
        // Person Record" and made a fresh environment permanently unreachable.
        await ResetUserRolesAsync();
        var email = $"nobody+{Guid.NewGuid():N}@leadingedje.com";

        // Act
        var response = await SignInViaCallbackAsync(email, SuperAdminGroup);

        // Assert — a session is issued and the redirect is local, not access-denied.
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        DecodedLocation(response).ShouldNotContain("access-denied");
        response.Headers.Location!.ToString().ShouldBe("/");
        var sessionCookie = ExtractSessionCookie(response);
        sessionCookie.ShouldNotBeNull();
        ExternalCookieIsCleared(response).ShouldBeTrue();

        // Assert — /api/me returns 200 (never 500) with a REAL EdjeId claim.
        var me = await GetJsonWithCookieAsync("/api/me", sessionCookie);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonDocument.Parse(
            await me.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        body.GetProperty("email").GetString().ShouldBe(email);
        body.GetProperty("edjeId").GetString().ShouldNotBeNullOrWhiteSpace();
        Guid.Parse(body.GetProperty("edjeId").GetString()!).ShouldNotBe(Guid.Empty);

        // Assert — exactly one active `people` row carrying the SAML auto-create provenance source.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var people = await db.People
            .Where(p => p.Email != null && p.Email.ToLower() == email.ToLower())
            .ToListAsync(TestContext.Current.CancellationToken);
        people.Count.ShouldBe(1);
        people[0].IsActive.ShouldBeTrue();
        people[0].Source.ShouldBe(PersonSource.SamlAutoCreate);
    }

    [Fact]
    public async Task LoginCallback_DeactivatedPersonRowWithNullEdjeId_DeniesWithoutCreatingASecondRow()
    {
        // Arrange — an inactive row whose EdjeId is null. A naive insert here would resurrect a
        // deactivated person AND violate the LOWER(email) unique index (D-06 / T-sqc-03).
        await ResetUserRolesAsync();
        var email = $"deactivated+{Guid.NewGuid():N}@leadingedje.com";
        using (var arrangeScope = _factory.Services.CreateScope())
        {
            var arrangeDb = arrangeScope.ServiceProvider.GetRequiredService<LeapDbContext>();
            arrangeDb.People.Add(new Person
            {
                Id = Guid.CreateVersion7(),
                EdjeId = null,
                Email = email,
                IsActive = false,
            });
            await arrangeDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        var response = await SignInViaCallbackAsync(email, SuperAdminGroup);

        // Assert — denied with the inactive reason and no session.
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        DecodedLocation(response).ShouldContain("access-denied");
        DecodedLocation(response).ShouldContain("Inactive Person Record");
        SessionCookieIsAbsentOrCleared(response).ShouldBeTrue();
        ExternalCookieIsCleared(response).ShouldBeTrue();

        // Assert — still exactly one row, still inactive, still no minted EdjeId.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var people = await db.People
            .Where(p => p.Email != null && p.Email.ToLower() == email.ToLower())
            .ToListAsync(TestContext.Current.CancellationToken);
        people.Count.ShouldBe(1);
        people[0].IsActive.ShouldBeFalse();
        people[0].EdjeId.ShouldBeNull();
    }

    [Fact]
    public async Task LoginCallback_InactivePerson_DeniesWithNoSession()
    {
        // Arrange — an inactive row that already carries an EdjeId (the counterpart to
        // LoginCallback_DeactivatedPersonRowWithNullEdjeId_DeniesWithoutCreatingASecondRow's null-EdjeId
        // case; EnsurePersonAsync's existing-row branch denies both the same way).
        await ResetUserRolesAsync();
        var edjeId = Guid.NewGuid();
        var email = $"inactive+{edjeId:N}@leadingedje.com";
        using (var arrangeScope = _factory.Services.CreateScope())
        {
            var arrangeDb = arrangeScope.ServiceProvider.GetRequiredService<LeapDbContext>();
            arrangeDb.People.Add(new Person
            {
                Id = Guid.CreateVersion7(),
                EdjeId = edjeId,
                Email = email,
                IsActive = false,
            });
            await arrangeDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        var response = await SignInViaCallbackAsync(email, SuperAdminGroup);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        DecodedLocation(response).ShouldContain("access-denied");
        DecodedLocation(response).ShouldContain("Inactive Person Record");
        SessionCookieIsAbsentOrCleared(response).ShouldBeTrue();
    }

    [Fact]
    public async Task LoginCallback_ForeignDomain_DeniesWithNoSession()
    {
        // Arrange — the domain gate runs before any person lookup, so no person row is needed at all.
        await ResetUserRolesAsync();
        var email = "someone@gmail.com";

        // Act
        var response = await SignInViaCallbackAsync(email, SuperAdminGroup);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        DecodedLocation(response).ShouldContain("access-denied");
        DecodedLocation(response).ShouldContain("Unauthorized domain");
        SessionCookieIsAbsentOrCleared(response).ShouldBeTrue();
    }

    [Fact]
    public async Task LoginCallback_SuccessfulSignIn_SyncsUserRolesForGrantedRoles()
    {
        // Arrange — no pre-seeded person: sign-in auto-provisions one for any domain-validated email.
        await ResetUserRolesAsync();
        var email = $"rolesync+{Guid.NewGuid():N}@leadingedje.com";

        // Act
        var response = await SignInViaCallbackAsync(email, SuperAdminGroup);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var sessionCookie = ExtractSessionCookie(response);
        sessionCookie.ShouldNotBeNull();

        // The auto-provisioned EdjeId is minted during sign-in, so read it back from the session
        // rather than assuming one ahead of time.
        var me = await GetJsonWithCookieAsync("/api/me", sessionCookie);
        var edjeId = Guid.Parse(
            JsonDocument.Parse(await me.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
                .RootElement.GetProperty("edjeId").GetString()!);

        // Assert — user_roles rows exist for the granted role set (mapped SuperAdmin + automatic EDJEr)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var roles = await db.UserRoles
            .Where(r => r.EdjeId == edjeId)
            .Select(r => r.Role)
            .ToListAsync(TestContext.Current.CancellationToken);
        roles.ShouldContain("SuperAdmin");
        roles.ShouldContain("EDJEr");
    }

    [Fact]
    public async Task Logout_ClearsSession_LandsOnSignedOutPage_AndMeIsUnauthorized()
    {
        // Arrange — establish a session first (auto-provisions the person)
        await ResetUserRolesAsync();
        var email = $"logout+{Guid.NewGuid():N}@leadingedje.com";
        var loginResponse = await SignInViaCallbackAsync(email, SuperAdminGroup);
        var sessionCookie = ExtractSessionCookie(loginResponse);
        sessionCookie.ShouldNotBeNull();

        // Act — logout with the session cookie
        var logoutRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/logout");
        logoutRequest.Headers.Add("Cookie", sessionCookie);
        var logoutResponse = await _client.SendAsync(logoutRequest, TestContext.Current.CancellationToken);

        // Assert — redirect to the server-rendered signed-out page and the session cookie is cleared
        logoutResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
        logoutResponse.Headers.Location!.ToString().ShouldBe("/auth/signed-out");
        SessionCookieIsAbsentOrCleared(logoutResponse).ShouldBeTrue();

        // The signed-out page is anonymous text, never a SPA route
        var signedOut = await _client.GetAsync("/auth/signed-out", TestContext.Current.CancellationToken);
        signedOut.StatusCode.ShouldBe(HttpStatusCode.OK);
        signedOut.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");

        // After logout the browser drops the cleared cookie → /api/me is unauthorized
        var me = await _client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithoutSession_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StubLogin_OutsideDevelopment_Returns404()
    {
        // The factory runs under "Testing", so the Development-only stub-login must 404.
        var response = await _client.GetAsync(
            "/auth/stub-login?email=someone@leadingedje.com", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Login_WhenSamlNotConfigured_Returns503()
    {
        // SAML config is intentionally absent in the test host, so the challenge cannot be issued.
        var response = await _client.GetAsync("/auth/login", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task LoginCallback_ReturnUrlIsExternalAbsoluteUrl_RedirectsToRoot()
    {
        // Arrange — no pre-seeded person: sign-in auto-provisions one for any domain-validated email.
        await ResetUserRolesAsync();
        var email = $"openredirect+{Guid.NewGuid():N}@leadingedje.com";

        // Act — attacker-controlled absolute external returnUrl
        var response = await SignInViaCallbackAsync(email, SuperAdminGroup, "https://evil.example");

        // Assert — open-redirect guard falls back to "/"
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        DecodedLocation(response).ShouldBe("/");
    }

    [Fact]
    public async Task LoginCallback_ReturnUrlIsLocalPath_RedirectsToIt()
    {
        // Arrange — no pre-seeded person: sign-in auto-provisions one for any domain-validated email.
        await ResetUserRolesAsync();
        var email = $"localreturn+{Guid.NewGuid():N}@leadingedje.com";

        // Act
        var response = await SignInViaCallbackAsync(email, SuperAdminGroup, "/timesheet/week");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        DecodedLocation(response).ShouldBe("/timesheet/week");
    }

    // --- Harness ---------------------------------------------------------------------------------

    private async Task<HttpResponseMessage> SignInViaCallbackAsync(
        string email, string groups, string? returnUrl = null, string? name = null)
    {
        var externalUrl = $"/test-only/sign-in-external?email={Uri.EscapeDataString(email)}&groups={Uri.EscapeDataString(groups)}";
        if (name is not null)
        {
            externalUrl += $"&name={Uri.EscapeDataString(name)}";
        }

        var externalResponse = await _client.GetAsync(externalUrl, TestContext.Current.CancellationToken);
        externalResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var externalCookie = ExtractExternalSchemeCookie(externalResponse);

        var callbackUrl = returnUrl is null
            ? "/auth/login-callback"
            : $"/auth/login-callback?returnUrl={Uri.EscapeDataString(returnUrl)}";
        var callbackRequest = new HttpRequestMessage(HttpMethod.Get, callbackUrl);
        if (externalCookie is not null)
        {
            callbackRequest.Headers.Add("Cookie", externalCookie);
        }

        return await _client.SendAsync(callbackRequest, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> GetJsonWithCookieAsync(string url, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task ResetUserRolesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM user_roles;", TestContext.Current.CancellationToken);
    }

    private static string DecodedLocation(HttpResponseMessage response) =>
        Uri.UnescapeDataString(response.Headers.Location?.ToString() ?? string.Empty);

    private static string? ExtractSessionCookie(HttpResponseMessage response) =>
        SetCookies(response).FirstOrDefault(h => h.StartsWith(SessionCookiePrefix))?.Split(';').First();

    private static string? ExtractExternalSchemeCookie(HttpResponseMessage response) =>
        SetCookies(response).FirstOrDefault(h => h.StartsWith(ExternalCookiePrefix))?.Split(';').First();

    private static bool SessionCookieIsAbsentOrCleared(HttpResponseMessage response) =>
        CookieIsAbsentOrCleared(response, SessionCookiePrefix);

    private static bool ExternalCookieIsCleared(HttpResponseMessage response) =>
        CookieIsAbsentOrCleared(response, ExternalCookiePrefix);

    private static bool CookieIsAbsentOrCleared(HttpResponseMessage response, string prefix)
    {
        var cookie = SetCookies(response).FirstOrDefault(h => h.StartsWith(prefix));
        if (cookie is null)
        {
            return true; // never issued
        }

        // ASP.NET Core clears a cookie by re-issuing it with an empty value and an expired date.
        return cookie.StartsWith($"{prefix};")
            || cookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var headers) ? headers : [];
}
