using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Tests.Auth;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Verifies that GetEntityAuditTrail consults the <see cref="IAuditTrailAccessPolicy"/> rule registry
/// for a non-admin caller — the !isAdmin branch that was uncovered because TestWebApplicationFactory
/// always provides SuperAdmin claims. Production registers no <c>IAuditTrailAccessRule</c> at all
/// after the Timesheet module retirement (it owned the only one), so a stub rule is registered here,
/// reachable through the registry and nowhere else, to prove the registry-consultation branch still
/// works rather than asserting against an always-empty rule set.
/// </summary>
public class AuditLogAccessControlTests : IAsyncLifetime
{
    private readonly AuditEDJErFactory _factory = new();
    private readonly HttpClient _client;

    public AuditLogAccessControlTests()
    {
        _client = _factory.CreateClient();
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>
    /// The route consults the rule registry, proven through the API boundary.
    /// </summary>
    /// <remarks>
    /// A stub rule claims an entity type that exists nowhere in production, so only a route that
    /// asks the registry can return its refusal. Registering it here changes no production wiring;
    /// that production holds zero rules is asserted by <c>ServiceGraphResolvesTests</c>
    /// against a different host.
    /// </remarks>
    [Fact]
    public async Task GetEntityAuditTrail_EDJEr_FictitiousEntityTypeWithAStubRule_ReturnsTheStubsRefusal()
    {
        // Arrange — AuditEDJErFactory registers RefusingStubRule for this entity type only.
        // Act
        var response = await _client.GetAsync(
            $"/api/audit-logs/entity?entityType={RefusingStubRule.FictitiousEntityType}&entityId=1",
            TestContext.Current.CancellationToken);

        // Assert — the stub refuses, so the route must forbid.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// An entity type with no registered rule is allowed, not refused.
    /// </summary>
    /// <remarks>
    /// The registry's central semantic, and nothing else asserts it. Flipping the default to deny
    /// must turn this red deliberately.
    /// </remarks>
    [Fact]
    public async Task GetEntityAuditTrail_EDJEr_EntityTypeWithNoRegisteredRule_ReturnsOk()
    {
        // Arrange — no IAuditTrailAccessRule claims this entity type, in this host or in production.
        // Act
        var response = await _client.GetAsync(
            "/api/audit-logs/entity?entityType=SystemSetting&entityId=some-key",
            TestContext.Current.CancellationToken);

        // Assert — the exact status. Allowed, not refused, and not a 404.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetEntityAuditTrail_MissingEntityTypeOrId_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/audit-logs/entity?entityType=SystemSetting",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

/// <summary>
/// A rule for an entity type that exists nowhere in production, registered only in
/// <see cref="AuditEDJErFactory"/> so it is reachable through the registry and nowhere else.
/// </summary>
internal sealed class RefusingStubRule : IAuditTrailAccessRule
{
    /// <summary>An entity type no production rule claims and no audited entity uses.</summary>
    internal const string FictitiousEntityType = "FictitiousForTest019";

    /// <inheritdoc/>
    public string EntityType => FictitiousEntityType;

    /// <inheritdoc/>
    public Task<AuditTrailAccessOutcome> EvaluateAsync(AuditTrailAccessRequest request) =>
        Task.FromResult(AuditTrailAccessOutcome.Refused);
}

/// <summary>
/// WebApplicationFactory with EDJEr-only auth (no SuperAdmin/HR/Admin) so the !isAdmin
/// registry-consultation branch in GetEntityAuditTrail is exercised.
/// </summary>
internal class AuditEDJErFactory : WebApplicationFactory<Program>
{
    private readonly InMemoryAuditLogRepository _auditRepo = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, EDJErOnlyAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            var efDescriptors = services
                .Where(d => d.ServiceType.FullName != null
                    && (d.ServiceType == typeof(DbContextOptions<LeapDbContext>)
                        || d.ServiceType == typeof(DbContextOptions)
                        || d.ServiceType == typeof(LeapDbContext)
                        || d.ServiceType.FullName.Contains("EntityFrameworkCore")
                        || d.ServiceType.FullName.Contains("NamingConventions")))
                .ToList();
            foreach (var d in efDescriptors)
            {
                services.Remove(d);
            }

            RemoveService<IAuditLogRepository>(services);
            RemoveService<IPersonRepository>(services);
            RemoveService<IUserRoleRepository>(services);
            RemoveService<INotificationLogRepository>(services);
            RemoveService<ISystemSettingRepository>(services);

            services.AddDbContext<LeapDbContext>(options =>
                options.UseInMemoryDatabase($"TestDb_EDJEr_{Guid.NewGuid()}"));

            services.AddSingleton<IAuditLogRepository>(_auditRepo);
            services.AddSingleton<IPersonRepository, InMemoryPersonRepository>();
            services.AddSingleton<INotificationLogRepository, InMemoryNotificationLogRepository>();
            services.AddSingleton<ISystemSettingRepository, InMemorySystemSettingRepository>();

            var roleRepo = new InMemoryUserRoleRepository();
            roleRepo.AddAsync(new UserRole
            {
                EdjeId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Role = "EDJEr"
            }).GetAwaiter().GetResult();
            services.AddSingleton<IUserRoleRepository>(roleRepo);

            // Registered alongside production's (now empty) rule set, so the registry-consultation
            // branch has something to reach.
            services.AddScoped<IAuditTrailAccessRule, RefusingStubRule>();

            var quartzDescriptors = services
                .Where(d => d.ServiceType.FullName != null
                    && (d.ServiceType.FullName.Contains("Quartz")
                        || d.ServiceType == typeof(ISchedulerFactory)
                        || d.ServiceType == typeof(IScheduler)))
                .ToList();
            foreach (var d in quartzDescriptors)
            {
                services.Remove(d);
            }

            var hostedServiceDescriptors = services
                .Where(d => d.ImplementationType?.FullName?.Contains("Quartz") == true)
                .ToList();
            foreach (var d in hostedServiceDescriptors)
            {
                services.Remove(d);
            }
        });

        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");
        builder.UseEnvironment("Testing");
    }

    private static void RemoveService<T>(IServiceCollection services)
    {
        var d = services.SingleOrDefault(s => s.ServiceType == typeof(T));
        if (d != null)
        {
            services.Remove(d);
        }
    }
}

/// <summary>
/// Auth handler that provides only the EDJEr privilege (no SuperAdmin/HR/Admin),
/// so HasPrivilege("SuperAdmin") returns false and the registry-consultation branch is enforced.
/// </summary>
internal class EDJErOnlyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new(AuthConstants.ClaimTypes.EdjeIdClaim, "00000000-0000-0000-0000-000000000002"),
            new(ClaimTypes.Email, "edjer@leadingedje.com"),
            new(AuthConstants.ClaimTypes.ClientIdClaim, "TESTCLIENT00000000000000000000001"),
            new(AuthConstants.ClaimTypes.PrivilegeClaim, "EDJEr"),
        };
        var identity = new ClaimsIdentity(claims, TestAuthHandler.SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), TestAuthHandler.SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
