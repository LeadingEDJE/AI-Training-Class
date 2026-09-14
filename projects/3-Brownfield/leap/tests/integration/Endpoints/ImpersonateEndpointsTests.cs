using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// The <c>ImpersonateEndpoints</c> integration test. This is the impersonation session suite that was
/// named <c>ImpersonationSessionTests</c> until Phase 48 plan 48-06 renamed it to the
/// <c>{Resource}EndpointsTests.cs</c> convention that the endpoint-test gate derives its expected
/// filename from. Search terms "impersonation
/// session" and the old class name are retained here on purpose so the file stays greppable.
/// It covers both routes of the group: <c>POST /api/impersonate/{edjeId}</c> and
/// <c>POST /api/impersonate/stop</c>.
///
/// Exercises cookie-session impersonation end to end via the real cookie pipeline. A SuperAdmin signs
/// in through the 41-02 external-scheme harness (real cookies, real MySQL), then POSTs
/// <c>/api/impersonate/{edjeId}</c>; the session cookie is re-issued as the target identity with
/// provenance claims. Mutations while impersonating attribute the audit actor to the target (parity
/// with the retired minted-JWT flow), <c>/api/impersonate/stop</c> restores the original identity from
/// the provenance claims, non-SuperAdmins are denied, and nested impersonation is blocked. No JWT is
/// minted anywhere. AllowAutoRedirect/HandleCookies are off so every Set-Cookie is inspected by hand.
/// </summary>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured + used across helpers
public class ImpersonateEndpointsTests : IClassFixture<SamlSignInTestFactory>
{
    private const string SessionCookiePrefix = "Timesheet=";
    private const string SuperAdminGroup = "Timesheet-SuperAdmin-dev";

    private readonly SamlSignInTestFactory _factory;
    private readonly HttpClient _client;

    public ImpersonateEndpointsTests(SamlSignInTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }
#pragma warning restore IDE0290

    [Fact]
    public async Task Start_AsSuperAdmin_ReissuesSessionAsTargetWithImpersonatorProvenance()
    {
        // Arrange — SuperAdmin session + an impersonation target resolvable via TPS with two roles.
        await ResetAsync();
        var (adminSession, adminEdjeId) = await SignInSuperAdminAsync();

        var targetEdjeId = Guid.NewGuid();
        SeedTargetEmployee(targetEdjeId, "Jane Target", "jane.target@leadingedje.com");
        await SeedUserRolesAsync(targetEdjeId, "EDJEr", "Manager");

        // Act
        var response = await PostImpersonateAsync(adminSession, targetEdjeId);

        // Assert — the response is the /api/me shape for the TARGET plus an impersonator block, and a
        // new session cookie is issued (no token field anywhere).
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reissued = ExtractSessionCookie(response);
        reissued.ShouldNotBeNull();

        var body = await ReadJsonAsync(response);
        body.GetProperty("edjeId").GetString().ShouldBe(targetEdjeId.ToString());
        body.GetProperty("email").GetString().ShouldBe("jane.target@leadingedje.com");
        body.GetProperty("displayName").GetString().ShouldBe("Jane Target");
        var privileges = body.GetProperty("privileges").EnumerateArray().Select(e => e.GetString()).ToList();
        privileges.ShouldContain("Manager");
        privileges.ShouldContain("EDJEr");
        body.TryGetProperty("token", out _).ShouldBeFalse();

        var impersonator = body.GetProperty("impersonator");
        impersonator.ValueKind.ShouldNotBe(JsonValueKind.Null);
        impersonator.GetProperty("edjeId").GetString().ShouldBe(adminEdjeId.ToString());

        // /api/me with the re-issued cookie reports the target identity AND the impersonator block.
        var me = await GetWithCookieAsync("/api/me", reissued!);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var meBody = await ReadJsonAsync(me);
        meBody.GetProperty("edjeId").GetString().ShouldBe(targetEdjeId.ToString());
        meBody.GetProperty("displayName").GetString().ShouldBe("Jane Target");
        meBody.GetProperty("impersonator").GetProperty("edjeId").GetString().ShouldBe(adminEdjeId.ToString());
    }

    [Fact]
    public async Task Impersonating_MutationAttributesAuditActorToTarget()
    {
        // Arrange — impersonate a target that itself holds SuperAdmin so the impersonated session can
        // exercise an audited admin mutation. This pins audit parity with the old minted-JWT flow,
        // where the JWT carried the target EdjeId and every mutation attributed to the target.
        await ResetAsync();
        var (adminSession, _) = await SignInSuperAdminAsync();

        var targetEdjeId = Guid.NewGuid();
        SeedTargetEmployee(targetEdjeId, "Target Admin", "target.admin@leadingedje.com");
        await SeedUserRolesAsync(targetEdjeId, "SuperAdmin");

        var impersonateResponse = await PostImpersonateAsync(adminSession, targetEdjeId);
        impersonateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var impersonatedSession = ExtractSessionCookie(impersonateResponse)!;

        // Act — perform an audited mutation (role assignment) while impersonating.
        var thirdEdjeId = Guid.NewGuid();
        var assign = new HttpRequestMessage(HttpMethod.Post, "/api/admin/user-roles");
        assign.Headers.Add("Cookie", impersonatedSession);
        assign.Content = JsonContent.Create(new { EdjeId = thirdEdjeId, Role = "Manager", Reason = "granted while impersonating" });
        var assignResponse = await _client.SendAsync(assign, TestContext.Current.CancellationToken);

        // Assert — the mutation succeeded and the audit actor is the TARGET's EdjeId, not the admin's.
        assignResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var userRoleAudit = await db.AuditLogs
            .Where(a => a.EntityType == "UserRole")
            .ToListAsync(TestContext.Current.CancellationToken);
        userRoleAudit.Count.ShouldBe(1);
        userRoleAudit[0].Actor.ShouldBe(targetEdjeId.ToString());
    }

    [Fact]
    public async Task Stop_RestoresOriginalIdentityAndPrivileges()
    {
        // Arrange — SuperAdmin impersonates a plain EDJEr target.
        await ResetAsync();
        var (adminSession, adminEdjeId) = await SignInSuperAdminAsync();

        var targetEdjeId = Guid.NewGuid();
        SeedTargetEmployee(targetEdjeId, "Plain User", "plain.user@leadingedje.com");
        await SeedUserRolesAsync(targetEdjeId, "EDJEr");

        var impersonateResponse = await PostImpersonateAsync(adminSession, targetEdjeId);
        var impersonatedSession = ExtractSessionCookie(impersonateResponse)!;

        // Act — stop impersonating with the impersonated session cookie.
        var stop = new HttpRequestMessage(HttpMethod.Post, "/api/impersonate/stop");
        stop.Headers.Add("Cookie", impersonatedSession);
        var stopResponse = await _client.SendAsync(stop, TestContext.Current.CancellationToken);

        // Assert — the original SuperAdmin identity is restored in a re-issued cookie, no re-login.
        stopResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var restored = ExtractSessionCookie(stopResponse);
        restored.ShouldNotBeNull();

        var body = await ReadJsonAsync(stopResponse);
        body.GetProperty("edjeId").GetString().ShouldBe(adminEdjeId.ToString());
        body.GetProperty("privileges").EnumerateArray().Select(e => e.GetString()).ShouldContain("SuperAdmin");
        body.GetProperty("impersonator").ValueKind.ShouldBe(JsonValueKind.Null);

        var me = await GetWithCookieAsync("/api/me", restored!);
        var meBody = await ReadJsonAsync(me);
        meBody.GetProperty("edjeId").GetString().ShouldBe(adminEdjeId.ToString());
        meBody.GetProperty("privileges").EnumerateArray().Select(e => e.GetString()).ShouldContain("SuperAdmin");
        meBody.GetProperty("impersonator").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Start_AsNonSuperAdmin_Returns403()
    {
        // Arrange — a plain EDJEr session (no SuperAdmin) tries to impersonate; no pre-seeded
        // person: sign-in auto-provisions one for any domain-validated email.
        await ResetAsync();
        var email = $"edjer+{Guid.NewGuid():N}@leadingedje.com";
        var session = await SignInAsync(email, groups: "");

        var targetEdjeId = Guid.NewGuid();
        SeedTargetEmployee(targetEdjeId, "Some Target", "some.target@leadingedje.com");

        // Act
        var response = await PostImpersonateAsync(session, targetEdjeId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Start_WhileAlreadyImpersonating_ReturnsConflict()
    {
        // Arrange — impersonate a SuperAdmin-holding target, then attempt a nested impersonation.
        await ResetAsync();
        var (adminSession, _) = await SignInSuperAdminAsync();

        var targetEdjeId = Guid.NewGuid();
        SeedTargetEmployee(targetEdjeId, "Target Admin", "target.admin2@leadingedje.com");
        await SeedUserRolesAsync(targetEdjeId, "SuperAdmin");
        var impersonatedSession = ExtractSessionCookie(await PostImpersonateAsync(adminSession, targetEdjeId))!;

        var secondTarget = Guid.NewGuid();
        SeedTargetEmployee(secondTarget, "Second Target", "second.target@leadingedje.com");

        // Act — a session that is already impersonating cannot start another impersonation.
        var response = await PostImpersonateAsync(impersonatedSession, secondTarget);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Start_TargetWithNoUserRoles_StillGrantsEdjErFloor()
    {
        // Arrange — target has a TPS record but zero user_roles rows.
        await ResetAsync();
        var (adminSession, _) = await SignInSuperAdminAsync();

        var targetEdjeId = Guid.NewGuid();
        SeedTargetEmployee(targetEdjeId, "No Roles", "no.roles@leadingedje.com");

        // Act
        var response = await PostImpersonateAsync(adminSession, targetEdjeId);

        // Assert — the EDJEr floor still applies even with no user_roles rows.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("privileges").EnumerateArray().Select(e => e.GetString()).ShouldContain("EDJEr");
    }

    [Fact]
    public async Task Stop_WhenNotImpersonating_ReturnsBadRequest()
    {
        // Arrange — an ordinary (non-impersonating) SuperAdmin session.
        await ResetAsync();
        var (adminSession, _) = await SignInSuperAdminAsync();

        // Act — stop with no impersonation in progress.
        var stop = new HttpRequestMessage(HttpMethod.Post, "/api/impersonate/stop");
        stop.Headers.Add("Cookie", adminSession);
        var response = await _client.SendAsync(stop, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Harness ---------------------------------------------------------------------------------

    // Auto-provisions the person (no pre-seeded row): the EdjeId is minted during sign-in, so it is
    // read back from /api/me rather than chosen ahead of time.
    private async Task<(string Session, Guid EdjeId)> SignInSuperAdminAsync()
    {
        var email = $"superadmin+{Guid.NewGuid():N}@leadingedje.com";
        var session = await SignInAsync(email, SuperAdminGroup, name: "Super Admin");
        var me = await GetWithCookieAsync("/api/me", session);
        var edjeId = Guid.Parse((await ReadJsonAsync(me)).GetProperty("edjeId").GetString()!);
        return (session, edjeId);
    }

    private async Task<string> SignInAsync(string email, string groups, string? name = null)
    {
        var externalUrl = $"/test-only/sign-in-external?email={Uri.EscapeDataString(email)}&groups={Uri.EscapeDataString(groups)}";
        if (name is not null)
        {
            externalUrl += $"&name={Uri.EscapeDataString(name)}";
        }

        var external = await _client.GetAsync(externalUrl, TestContext.Current.CancellationToken);
        external.StatusCode.ShouldBe(HttpStatusCode.OK);
        var externalCookie = SetCookies(external)
            .FirstOrDefault(h => h.StartsWith("Timesheet-External="))?.Split(';').First();

        var callback = new HttpRequestMessage(HttpMethod.Get, "/auth/login-callback");
        if (externalCookie is not null)
        {
            callback.Headers.Add("Cookie", externalCookie);
        }

        var callbackResponse = await _client.SendAsync(callback, TestContext.Current.CancellationToken);
        var session = ExtractSessionCookie(callbackResponse);
        session.ShouldNotBeNull();
        return session!;
    }

    // Seeds the impersonation target directly into `people`: ImpersonationService resolves a target
    // via IPersonRepository.GetByEdjeIdAsync, not a TPS lookup.
    private void SeedTargetEmployee(Guid edjeId, string name, string email)
    {
        var parts = name.Split(' ', 2);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        db.People.Add(new Person
        {
            Id = Guid.CreateVersion7(),
            EdjeId = edjeId,
            FirstName = parts[0],
            LastName = parts.Length > 1 ? parts[1] : string.Empty,
            Email = email,
            IsActive = true,
        });
        db.SaveChanges();
    }

    private async Task SeedUserRolesAsync(Guid edjeId, params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        foreach (var role in roles)
        {
            db.UserRoles.Add(new UserRole
            {
                EdjeId = edjeId,
                Role = role,
                CreatedBy = "test-seed",
                UpdatedBy = "test-seed",
            });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> PostImpersonateAsync(string sessionCookie, Guid targetEdjeId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/impersonate/{targetEdjeId}");
        request.Headers.Add("Cookie", sessionCookie);
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> GetWithCookieAsync(string url, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM user_roles;", TestContext.Current.CancellationToken);
        // audit_logs is append-only (BEFORE DELETE trigger blocks DELETE); TRUNCATE does not fire the
        // trigger and gives each test a clean audit slate.
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE audit_logs;", TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

    private static string? ExtractSessionCookie(HttpResponseMessage response) =>
        SetCookies(response).FirstOrDefault(h => h.StartsWith(SessionCookiePrefix))?.Split(';').First();

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var headers) ? headers : [];
}
