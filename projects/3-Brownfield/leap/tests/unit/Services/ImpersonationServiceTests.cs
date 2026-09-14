using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Inner-loop unit tests for <see cref="ImpersonationService"/>: claim-set construction for the start
/// swap, the EDJEr floor, provenance capture/restore on stop, and the guard branches (unknown target,
/// nested impersonation, stop-when-not-impersonating). A recording <see cref="IAuthenticationService"/>
/// captures the re-issued cookie principal without a real cookie handler. The full HTTP round-trip is
/// covered by ImpersonateEndpointsTests (integration).
/// </summary>
public class ImpersonationServiceTests
{
    private static readonly Guid AdminEdjeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TargetEdjeId = Guid.Parse("00000000-0000-0000-0000-000000000042");

    private static ImpersonationService CreateService(
        InMemoryPersonRepository people, StubUserRoleService roles, RecordingAuditService audit) =>
        new(people, roles, audit, NullLogger<ImpersonationService>.Instance);

    private static void AddPerson(
        InMemoryPersonRepository people, string firstName, string lastName, string? email = null, Guid? edjeId = null) =>
        people.AddAsync(new Person
        {
            Id = Guid.NewGuid(),
            EdjeId = edjeId,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            IsActive = true,
        }).GetAwaiter().GetResult();

    private static (HttpContext Context, RecordingAuthenticationService Auth) CreateContext()
    {
        var auth = new RecordingAuthenticationService();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(auth);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        return (context, auth);
    }

    private static ClaimsPrincipal SuperAdminPrincipal(params string[] privileges)
    {
        var identity = new ClaimsIdentity(AuthConstants.Settings.LeadingEdjeAuthenticationType);
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, AdminEdjeId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Email, "admin@leadingedje.com"));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.DisplayNameClaim, "Super Admin"));
        foreach (var privilege in privileges.Length == 0 ? ["SuperAdmin", "EDJEr"] : privileges)
        {
            identity.AddClaim(new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, privilege));
        }

        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task StartAsync_SuperAdmin_ReissuesSessionAsTargetWithProvenanceClaims()
    {
        // Arrange
        var people = new InMemoryPersonRepository();
        AddPerson(people, "Jane", "Target", email: "jane.target@leadingedje.com", edjeId: TargetEdjeId);
        var roles = new StubUserRoleService { Roles = ["EDJEr", "Manager"] };
        var audit = new RecordingAuditService();
        var service = CreateService(people, roles, audit);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.StartAsync(SuperAdminPrincipal(), TargetEdjeId, context);

        // Assert — outcome carries the target /api/me shape plus the impersonator block.
        outcome.Status.ShouldBe(ImpersonationStatus.Started);
        outcome.Identity.ShouldNotBeNull();
        outcome.Identity!.EdjeId.ShouldBe(TargetEdjeId);
        outcome.Identity.Email.ShouldBe("jane.target@leadingedje.com");
        outcome.Identity.DisplayName.ShouldBe("Jane Target");
        outcome.Identity.Privileges.ShouldContain("Manager");
        outcome.Identity.Privileges.ShouldContain("EDJEr");
        outcome.Identity.Impersonator.ShouldNotBeNull();
        outcome.Identity.Impersonator!.EdjeId.ShouldBe(AdminEdjeId);
        outcome.Identity.Impersonator.DisplayName.ShouldBe("Super Admin");

        // Assert — the re-issued cookie principal is the target identity with provenance claims.
        auth.SignedInScheme.ShouldBe(AuthConstants.Settings.LeadingEdjeAuthenticationType);
        var reissued = auth.SignedInPrincipal!;
        reissued.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim).ShouldBe(TargetEdjeId.ToString());
        reissued.FindAll(AuthConstants.ClaimTypes.PrivilegeClaim).Select(c => c.Value)
            .ShouldBe(["EDJEr", "Manager"], ignoreOrder: true);
        reissued.FindFirstValue(AuthConstants.ClaimTypes.ImpersonatorEdjeId).ShouldBe(AdminEdjeId.ToString());
        reissued.FindFirstValue(AuthConstants.ClaimTypes.ImpersonatorName).ShouldBe("Super Admin");
        reissued.FindFirstValue(AuthConstants.ClaimTypes.ImpersonatorEmail).ShouldBe("admin@leadingedje.com");
        reissued.FindAll(AuthConstants.ClaimTypes.OriginalPrivilege).Select(c => c.Value)
            .ShouldBe(["SuperAdmin", "EDJEr"], ignoreOrder: true);
    }

    [Fact]
    public async Task StartAsync_TargetWithNoRoles_GrantsEdjErFloor()
    {
        // Arrange
        var people = new InMemoryPersonRepository();
        AddPerson(people, "No", "Roles", email: "no.roles@leadingedje.com", edjeId: TargetEdjeId);
        var service = CreateService(people, new StubUserRoleService { Roles = [] }, new RecordingAuditService());
        var (context, _) = CreateContext();

        // Act
        var outcome = await service.StartAsync(SuperAdminPrincipal(), TargetEdjeId, context);

        // Assert
        outcome.Status.ShouldBe(ImpersonationStatus.Started);
        outcome.Identity!.Privileges.ShouldBe(["EDJEr"]);
    }

    [Fact]
    public async Task StartAsync_TargetWithoutName_UsesEmailAsDisplayName()
    {
        // Arrange
        var people = new InMemoryPersonRepository();
        AddPerson(people, string.Empty, string.Empty, email: "only.email@leadingedje.com", edjeId: TargetEdjeId);
        var service = CreateService(people, new StubUserRoleService(), new RecordingAuditService());
        var (context, _) = CreateContext();

        // Act
        var outcome = await service.StartAsync(SuperAdminPrincipal(), TargetEdjeId, context);

        // Assert
        outcome.Identity!.DisplayName.ShouldBe("only.email@leadingedje.com");
    }

    [Fact]
    public async Task StartAsync_TargetWithoutNameOrEmail_UsesEdjeIdAsDisplayName()
    {
        // Arrange
        var people = new InMemoryPersonRepository();
        AddPerson(people, string.Empty, string.Empty, edjeId: TargetEdjeId);
        var service = CreateService(people, new StubUserRoleService(), new RecordingAuditService());
        var (context, _) = CreateContext();

        // Act
        var outcome = await service.StartAsync(SuperAdminPrincipal(), TargetEdjeId, context);

        // Assert
        outcome.Identity!.DisplayName.ShouldBe(TargetEdjeId.ToString());
        outcome.Identity.Email.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task StartAsync_UnknownTarget_ReturnsTargetNotFoundAndDoesNotSignIn()
    {
        // Arrange — no person seeded for the target.
        var audit = new RecordingAuditService();
        var service = CreateService(new InMemoryPersonRepository(), new StubUserRoleService(), audit);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.StartAsync(SuperAdminPrincipal(), TargetEdjeId, context);

        // Assert
        outcome.Status.ShouldBe(ImpersonationStatus.TargetNotFound);
        outcome.Identity.ShouldBeNull();
        auth.SignedInPrincipal.ShouldBeNull();
        audit.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyImpersonating_ReturnsAlreadyImpersonating()
    {
        // Arrange — a principal that already carries impersonation provenance.
        var people = new InMemoryPersonRepository();
        AddPerson(people, "Jane", "Target", edjeId: TargetEdjeId);
        var service = CreateService(people, new StubUserRoleService(), new RecordingAuditService());
        var (context, auth) = CreateContext();

        var identity = new ClaimsIdentity(AuthConstants.Settings.LeadingEdjeAuthenticationType);
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, TargetEdjeId.ToString()));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.ImpersonatorEdjeId, AdminEdjeId.ToString()));
        var impersonating = new ClaimsPrincipal(identity);

        // Act
        var outcome = await service.StartAsync(impersonating, Guid.NewGuid(), context);

        // Assert
        outcome.Status.ShouldBe(ImpersonationStatus.AlreadyImpersonating);
        auth.SignedInPrincipal.ShouldBeNull();
    }

    [Fact]
    public async Task StartAsync_WritesAuditEntryWithOriginalActor()
    {
        // Arrange
        var people = new InMemoryPersonRepository();
        AddPerson(people, "Jane", "Target", edjeId: TargetEdjeId);
        var audit = new RecordingAuditService();
        var service = CreateService(people, new StubUserRoleService { Roles = ["EDJEr"] }, audit);
        var (context, _) = CreateContext();

        // Act
        await service.StartAsync(SuperAdminPrincipal(), TargetEdjeId, context);

        // Assert — start is attributed to the ORIGINAL SuperAdmin actor.
        audit.Entries.Count.ShouldBe(1);
        audit.Entries[0].EntityType.ShouldBe("Impersonate");
        audit.Entries[0].Action.ShouldBe("create");
        audit.Entries[0].Actor.ShouldBe(AdminEdjeId.ToString());
        audit.Entries[0].EntityId.ShouldBe(TargetEdjeId.ToString());
    }

    [Fact]
    public async Task StartAsync_PrincipalMissingEdjeId_Throws()
    {
        // Arrange — a valid target, but the caller's principal lacks the EdjeId claim.
        var people = new InMemoryPersonRepository();
        AddPerson(people, "Jane", "Target", edjeId: TargetEdjeId);
        var service = CreateService(people, new StubUserRoleService(), new RecordingAuditService());
        var (context, _) = CreateContext();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(AuthConstants.Settings.LeadingEdjeAuthenticationType));

        // Act / Assert
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await service.StartAsync(principal, TargetEdjeId, context));
    }

    [Fact]
    public async Task StopAsync_RestoresOriginalIdentityFromProvenanceClaims()
    {
        // Arrange — an impersonating principal carrying full provenance.
        var audit = new RecordingAuditService();
        var service = CreateService(new InMemoryPersonRepository(), new StubUserRoleService(), audit);
        var (context, auth) = CreateContext();

        var identity = new ClaimsIdentity(AuthConstants.Settings.LeadingEdjeAuthenticationType);
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, TargetEdjeId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Email, "jane.target@leadingedje.com"));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, "EDJEr"));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.ImpersonatorEdjeId, AdminEdjeId.ToString()));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.ImpersonatorName, "Super Admin"));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.ImpersonatorEmail, "admin@leadingedje.com"));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.OriginalPrivilege, "SuperAdmin"));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.OriginalPrivilege, "EDJEr"));

        // Act
        var outcome = await service.StopAsync(new ClaimsPrincipal(identity), context);

        // Assert — original identity restored, impersonator cleared, audit attributes to original actor.
        outcome.Status.ShouldBe(ImpersonationStatus.Stopped);
        outcome.Identity!.EdjeId.ShouldBe(AdminEdjeId);
        outcome.Identity.Email.ShouldBe("admin@leadingedje.com");
        outcome.Identity.DisplayName.ShouldBe("Super Admin");
        outcome.Identity.Privileges.ShouldBe(["SuperAdmin", "EDJEr"], ignoreOrder: true);
        outcome.Identity.Impersonator.ShouldBeNull();

        var reissued = auth.SignedInPrincipal!;
        reissued.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim).ShouldBe(AdminEdjeId.ToString());
        reissued.FindFirst(AuthConstants.ClaimTypes.ImpersonatorEdjeId).ShouldBeNull();

        audit.Entries.Count.ShouldBe(1);
        audit.Entries[0].Action.ShouldBe("delete");
        audit.Entries[0].Actor.ShouldBe(AdminEdjeId.ToString());
        audit.Entries[0].EntityId.ShouldBe(TargetEdjeId.ToString());
    }

    [Fact]
    public async Task StopAsync_WhenNotImpersonating_ReturnsNotImpersonating()
    {
        // Arrange — an ordinary session with no provenance claims.
        var audit = new RecordingAuditService();
        var service = CreateService(new InMemoryPersonRepository(), new StubUserRoleService(), audit);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.StopAsync(SuperAdminPrincipal(), context);

        // Assert
        outcome.Status.ShouldBe(ImpersonationStatus.NotImpersonating);
        outcome.Identity.ShouldBeNull();
        auth.SignedInPrincipal.ShouldBeNull();
        audit.Entries.ShouldBeEmpty();
    }

    /// <summary>Captures the cookie sign-in so re-issued claim sets can be asserted without a handler.</summary>
    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public string? SignedInScheme { get; private set; }
        public ClaimsPrincipal? SignedInPrincipal { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            SignedInScheme = scheme;
            SignedInPrincipal = principal;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }

    /// <summary>Captures audit entries written during impersonation start/stop.</summary>
    private sealed class RecordingAuditService : IAuditService
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task LogAsync(AuditEntry entry)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditLogResponse>> GetByEntityAsync(string entityType, string entityId) =>
            Task.FromResult<IReadOnlyList<AuditLogResponse>>([]);

        public Task<PaginatedAuditLogResponse> BrowseAsync(
            string? entityType, string? actor, string? employeeId, DateTime? fromDate, DateTime? toDate, int page, int pageSize) =>
            Task.FromResult(new PaginatedAuditLogResponse([], 0, page, pageSize));

        public Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync() =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    /// <summary>Returns a configurable role set for GetRolesAsync; all other members are unused no-ops.</summary>
    private sealed class StubUserRoleService : IUserRoleService
    {
        public IReadOnlyList<string> Roles { get; init; } = [];

        public Task<IReadOnlyList<string>> GetRolesAsync(Guid edjeId) => Task.FromResult(Roles);
        public Task<bool> HasRoleAsync(Guid edjeId, string role) => Task.FromResult(false);
        public Task<bool> HasAnyRoleAsync(Guid edjeId, params string[] roles) => Task.FromResult(false);
        public Task AssignRoleAsync(Guid edjeId, string role, string actor, string reason) => Task.CompletedTask;
        public Task RemoveRoleAsync(Guid edjeId, string role, string actor, string reason) => Task.CompletedTask;
        public Task<IReadOnlyList<UserRole>> GetUsersByRoleAsync(string role) =>
            Task.FromResult<IReadOnlyList<UserRole>>([]);
        public Task SyncFromProfileAsync(Guid edjeId, IEnumerable<string> privileges, string actor = "dev-sync") =>
            Task.CompletedTask;
        public Task<int> EnsureBaseRoleForAllEmployeesAsync(IEnumerable<(Guid EdjeId, string Name)> employees) =>
            Task.FromResult(0);
    }
}
