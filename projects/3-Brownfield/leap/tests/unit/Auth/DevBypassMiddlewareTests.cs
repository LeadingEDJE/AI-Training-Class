using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Auth;

public class DevBypassMiddlewareTests
{
    private static DevBypassOptions CreateOptions(string defaultProfile = "superadmin", string clientId = "TESTCLIENT001")
    {
        return new DevBypassOptions
        {
            DefaultProfile = defaultProfile,
            ClientId = clientId,
            Profiles = new Dictionary<string, DevUserProfile>
            {
                ["superadmin"] = new()
                {
                    EdjeId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    Email = "admin@leadingedje.com",
                    Privileges = ["EDJEr", "Manager", "SuperAdmin"]
                },
                ["edjer-alice"] = new()
                {
                    EdjeId = Guid.Parse("00000000-0000-0000-0000-000000000004"),
                    Email = "alice@leadingedje.com",
                    DisplayName = "Alice Ashworth",
                    Privileges = ["EDJEr"]
                }
            }
        };
    }

    private static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserRoleRepository, InMemoryUserRoleRepository>();
        services.AddSingleton<IAuditService, StubAuditService>();
        services.AddDbContext<LeapDbContext>(opts =>
            opts.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<IUserRoleService, UserRoleService>();
        return services.BuildServiceProvider();
    }

    private static async Task<HttpContext> InvokeMiddleware(
        DevBypassOptions options,
        Action<HttpContext>? configureContext = null)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = CreateServiceProvider();
        configureContext?.Invoke(context);

        var middleware = new DevBypassMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, Options.Create(options));

        return context;
    }

    [Fact]
    public async Task DefaultProfile_SetsClaimsFromConfig()
    {
        // Arrange
        var options = CreateOptions();

        // Act
        var context = await InvokeMiddleware(options);

        // Assert
        context.User.Identity.ShouldNotBeNull();
        context.User.Identity!.IsAuthenticated.ShouldBeTrue();
        context.User.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim).ShouldBe("00000000-0000-0000-0000-000000000001");
        context.User.FindFirstValue(ClaimTypes.Email).ShouldBe("admin@leadingedje.com");
    }

    // Issue #285. The dev identity was the only authenticated identity in the system that could carry a
    // blank display name: this middleware stamped EdjeId, Email, ClientId and privileges and no
    // DisplayName, while SignInService.BuildIdentity guards the same claim explicitly ("belt-and-braces:
    // the claim must never be blank") and falls back to the email. /api/me reads the claim with
    // `?? string.Empty`, so `make dev-all` answered `displayName: ""`.
    //
    // That is not a cosmetic gap. It produced a SERIOUS axe violation in shipped code — the Compass nav
    // avatar rendered role="img" with an empty accessible name (role-img-alt, WCAG 1.1.1) — and CI could
    // not see it, because E2E signs in through /auth/stub-login, which routes through SignInService and
    // therefore always has a name. Fixed in the consumer by #284; this is the upstream half, so the next
    // consumer of displayName does not inherit the same trap.
    [Fact]
    public async Task DefaultProfile_SetsDisplayNameClaim_FallingBackToEmail()
    {
        // Arrange — the superadmin profile deliberately omits DisplayName, as every profile in
        // appsettings.Development.json does today.
        var options = CreateOptions();

        // Act
        var context = await InvokeMiddleware(options);

        // Assert
        context.User.FindFirstValue(AuthConstants.ClaimTypes.DisplayNameClaim)
            .ShouldBe("admin@leadingedje.com");
    }

    [Fact]
    public async Task ProfileDisplayName_WhenSet_IsPreferredOverTheEmail()
    {
        // Arrange
        var options = CreateOptions();

        // Act
        var context = await InvokeMiddleware(
            options,
            ctx => ctx.Request.Headers["X-Dev-User"] = "edjer-alice");

        // Assert
        context.User.FindFirstValue(AuthConstants.ClaimTypes.DisplayNameClaim)
            .ShouldBe("Alice Ashworth");
    }

    [Fact]
    public async Task DisplayNameClaim_IsNeverBlank_ForAnyProfile()
    {
        // Arrange — the invariant SignInService.BuildIdentity states for the real sign-in path. Asserted
        // over every configured profile so a newly-added one cannot reintroduce the blank.
        var options = CreateOptions();
        options.Profiles.Count.ShouldBeGreaterThan(1, "otherwise this sweep proves nothing");

        foreach (var profileName in options.Profiles.Keys)
        {
            // Act
            var context = await InvokeMiddleware(
                options,
                ctx => ctx.Request.Headers["X-Dev-User"] = profileName);

            // Assert
            context.User.FindFirstValue(AuthConstants.ClaimTypes.DisplayNameClaim)
                .ShouldNotBeNullOrWhiteSpace($"profile '{profileName}' produced a blank DisplayName claim");
        }
    }

    [Fact]
    public async Task DefaultProfile_SetsPrivilegeClaims()
    {
        // Arrange
        var options = CreateOptions();

        // Act
        var context = await InvokeMiddleware(options);

        // Assert
        var privileges = context.User.FindAll(AuthConstants.ClaimTypes.PrivilegeClaim)
            .Select(c => c.Value)
            .ToList();
        privileges.ShouldContain("EDJEr");
        privileges.ShouldContain("Manager");
        privileges.ShouldContain("SuperAdmin");
    }

    [Fact]
    public async Task XDevUserHeader_SwitchesToNamedProfile()
    {
        // Arrange
        var options = CreateOptions();

        // Act
        var context = await InvokeMiddleware(options, ctx =>
            ctx.Request.Headers["X-Dev-User"] = "edjer-alice");

        // Assert
        context.User.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim).ShouldBe("00000000-0000-0000-0000-000000000004");
        context.User.FindFirstValue(ClaimTypes.Email).ShouldBe("alice@leadingedje.com");
        var privileges = context.User.FindAll(AuthConstants.ClaimTypes.PrivilegeClaim)
            .Select(c => c.Value)
            .ToList();
        privileges.Count.ShouldBe(1);
        privileges.ShouldContain("EDJEr");
    }

    [Fact]
    public async Task XDevRolesHeader_OverridesPrivileges()
    {
        // Arrange
        var options = CreateOptions();

        // Act
        var context = await InvokeMiddleware(options, ctx =>
            ctx.Request.Headers["X-Dev-Roles"] = "HR,Accounting");

        // Assert
        var privileges = context.User.FindAll(AuthConstants.ClaimTypes.PrivilegeClaim)
            .Select(c => c.Value)
            .ToList();
        privileges.Count.ShouldBe(2);
        privileges.ShouldContain("HR");
        privileges.ShouldContain("Accounting");
        // Original profile privileges should NOT be present
        privileges.ShouldNotContain("SuperAdmin");
    }

    [Fact]
    public async Task UnknownProfileName_DoesNotSetUser()
    {
        // Arrange
        var options = CreateOptions();

        // Act
        var context = await InvokeMiddleware(options, ctx =>
            ctx.Request.Headers["X-Dev-User"] = "nonexistent-user");

        // Assert
        context.User.Identity!.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public async Task ClientIdClaim_IsAlwaysSetFromConfig()
    {
        // Arrange
        var options = CreateOptions(clientId: "MYCLIENT123");

        // Act
        var context = await InvokeMiddleware(options);

        // Assert
        context.User.FindFirstValue(AuthConstants.ClaimTypes.ClientIdClaim).ShouldBe("MYCLIENT123");
    }

    [Fact]
    public async Task EdjeIdClaim_IsValidGuid_CompatibleWithCurrentUserContext()
    {
        // Arrange -- DevBypass produces claims, CurrentUserContext consumes them via Guid.Parse
        var options = CreateOptions();

        // Act
        var context = await InvokeMiddleware(options);

        // Assert -- this is the exact parse CurrentUserContext.EdjeId does
        var edjeIdClaim = context.User.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim);
        edjeIdClaim.ShouldNotBeNull();
        Should.NotThrow(() => Guid.Parse(edjeIdClaim));
    }

    [Fact]
    public async Task SessionCookiePresent_SkipsDevBypass()
    {
        // Arrange -- a real cookie session must win over the dev profile (cookie-era skip condition).
        var options = CreateOptions();

        // Act
        var context = await InvokeMiddleware(options, ctx =>
            ctx.Request.Headers["Cookie"] = $"{AuthConstants.Settings.SessionCookieName}=stub.session.value");

        // Assert -- middleware should NOT have overwritten context.User
        context.User.Identity!.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public async Task AlreadyAuthenticatedUser_SkipsDevBypass()
    {
        // Arrange -- an upstream-authenticated identity must not be replaced by the dev profile.
        var options = CreateOptions();
        var preAuthenticated = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, "00000000-0000-0000-0000-000000000009")],
            "PreAuthenticated"));

        // Act
        var context = await InvokeMiddleware(options, ctx => ctx.User = preAuthenticated);

        // Assert -- the pre-existing identity is preserved, not replaced by the superadmin profile
        context.User.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim)
            .ShouldBe("00000000-0000-0000-0000-000000000009");
    }

    [Fact]
    public async Task NoSessionCookie_AppliesDevBypassNormally()
    {
        // Arrange
        var options = CreateOptions();

        // Act
        var context = await InvokeMiddleware(options);

        // Assert -- middleware applies dev profile as normal
        context.User.Identity!.IsAuthenticated.ShouldBeTrue();
        context.User.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim)
            .ShouldBe("00000000-0000-0000-0000-000000000001");
    }

    [Fact]
    public async Task BearerHeader_NoLongerSkipsDevBypass_InCookieEra()
    {
        // JWT Bearer is deleted in Phase 41; a Bearer header no longer suppresses the dev profile.
        // Only an authenticated user or a session cookie does (asserted above).
        var options = CreateOptions();

        // Act
        var context = await InvokeMiddleware(options, ctx =>
            ctx.Request.Headers.Authorization = "Bearer some.jwt.token");

        // Assert
        context.User.Identity!.IsAuthenticated.ShouldBeTrue();
    }

    private class StubAuditService : IAuditService
    {
        public Task LogAsync(AuditEntry entry) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditLogResponse>> GetByEntityAsync(string entityType, string entityId) =>
            Task.FromResult<IReadOnlyList<AuditLogResponse>>([]);
        public Task<PaginatedAuditLogResponse> BrowseAsync(string? entityType, string? actor, string? employeeId, DateTime? fromDate, DateTime? toDate, int page, int pageSize) =>
            Task.FromResult(new PaginatedAuditLogResponse([], 0, page, pageSize));
        public Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync() =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
