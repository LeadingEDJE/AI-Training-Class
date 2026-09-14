using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Inner-loop unit tests for <see cref="SignInService"/> — the logic backing the /auth endpoints.
/// Uses a fake <see cref="IAuthenticationService"/> to capture cookie sign-in/out without a cookie
/// handler or DB, so deny reasons, claim construction, role union, and the open-redirect guard are
/// pinned in isolation. The full HTTP pipeline is covered by SamlSignInTests (integration).
/// </summary>
public class AuthEndpointsTests
{
    private const string Domain = "leadingedje.com";

    private static SignInService CreateService(
        FakePersonProvisioningService people,
        RecordingUserRoleService roles,
        GoogleAuthOptions? googleAuthOptions = null,
        string environmentName = "Development")
    {
        var googleOptions = Options.Create(googleAuthOptions ?? new GoogleAuthOptions
        {
            AllowedDomain = Domain,
            Groups =
            [
                new GroupRoleMapping { GroupName = "Timesheet-SuperAdmin-dev", Role = "SuperAdmin" },
                new GroupRoleMapping { GroupName = "Timesheet-Approval-dev", Role = "Manager" },
            ],
        });
        var google = new GoogleAuthService(googleOptions);
        return new SignInService(
            google, roles, NullLogger<SignInService>.Instance,
            Options.Create(new AuthReturnUrlOptions()),
            people,
            Options.Create(new BootstrapAdminOptions()),
            new StubHostEnvironment(environmentName));
    }

    /// <summary>Reports a fixed environment name; only <see cref="EnvironmentName"/> is exercised here.</summary>
    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "LeadingEDJE.Leap.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// Serves whatever is seeded via <see cref="AddPerson"/>; an unseeded email is Rejected — provisioning's
    /// own auto-create branch is exercised in <c>SignInServiceTests</c>, not here (Quick task 260729-sqc).
    /// </summary>
    private sealed class FakePersonProvisioningService : IPersonProvisioningService
    {
        private readonly Dictionary<string, (Guid EdjeId, bool IsActive, string DisplayName)> _people =
            new(StringComparer.OrdinalIgnoreCase);

        public void AddPerson(string email, Guid edjeId, bool isActive, string? displayName = null) =>
            _people[email] = (edjeId, isActive, displayName ?? email);

        public Task<PersonProvisionResult> EnsurePersonAsync(string? email, string? displayName, string source)
        {
            if (email is not null && _people.TryGetValue(email, out var person))
            {
                return Task.FromResult(person.IsActive
                    ? PersonProvisionResult.ForExisting(person.EdjeId, email, person.DisplayName)
                    : PersonProvisionResult.ForInactive(email));
            }

            return Task.FromResult(PersonProvisionResult.ForRejected());
        }

        // Mirrors the real service's no-row fallback: hand the caller's already-resolved directory
        // name straight back. AddPerson's displayName is what CompleteSignIn resolves as
        // provisioned.DisplayName, so this is already authoritative without an override.
        public Task<string> EnsureDisplayNameAsync(Guid edjeId, string? assertionName, string resolvedDisplayName) =>
            Task.FromResult(resolvedDisplayName);

        public Task TryFillMissingDisplayNameAsync(Guid edjeId, string? candidateName) =>
            Task.CompletedTask;
    }

    private static (HttpContext Context, RecordingAuthenticationService Auth) CreateContext()
    {
        var auth = new RecordingAuthenticationService();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(auth);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        return (context, auth);
    }

    [Fact]
    public async Task CompleteSignIn_ActivePersonInSuperAdminGroup_SignsInSessionWithMappedRolesAndEdjErAndEdjeIdClaim()
    {
        // Arrange
        var edjeId = Guid.NewGuid();
        var people = new FakePersonProvisioningService();
        people.AddPerson("user@leadingedje.com", edjeId, isActive: true, displayName: "The User");
        var roles = new RecordingUserRoleService();
        var service = CreateService(people, roles);
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync(
            "user@leadingedje.com", "assertion-name", ["Timesheet-SuperAdmin-dev"], context, returnUrl: null);

        // Assert
        outcome.Succeeded.ShouldBeTrue();
        outcome.Location.ShouldBe("/");
        auth.SignedInScheme.ShouldBe(AuthConstants.Settings.LeadingEdjeAuthenticationType);
        auth.SignedOutSchemes.ShouldContain(AuthConstants.Settings.ExternalScheme);

        var identity = auth.SignedInPrincipal!;
        identity.FindFirstValue(AuthConstants.ClaimTypes.EdjeIdClaim).ShouldBe(edjeId.ToString());
        identity.FindFirstValue(ClaimTypes.Email).ShouldBe("user@leadingedje.com");
        // DisplayName is authoritative from the person record, not the assertion name.
        identity.FindFirstValue(AuthConstants.ClaimTypes.DisplayNameClaim).ShouldBe("The User");

        var privileges = identity.FindAll(AuthConstants.ClaimTypes.PrivilegeClaim).Select(c => c.Value).ToList();
        privileges.ShouldContain("SuperAdmin");
        privileges.ShouldContain("EDJEr");

        // user_roles synced with the same role set
        roles.SyncedRoles.ShouldContain("SuperAdmin");
        roles.SyncedRoles.ShouldContain("EDJEr");
        roles.SyncedEdjeId.ShouldBe(edjeId);
    }

    // Issue #53 named regression: a user holding both a real production Compass group and a
    // dev-only Compass group must resolve, in Production, to the production-only role — the
    // dev group must never be honored outside non-production, even alongside a lower-privilege
    // production group held by the same user.
    [Fact]
    public async Task CompleteSignIn_ProdAndDevCompassGroupsBothPresentInProduction_GrantsProdRoleOnlyNotDevRole()
    {
        // Arrange
        var people = new FakePersonProvisioningService();
        people.AddPerson("compass-ops@leadingedje.com", Guid.NewGuid(), isActive: true);
        var googleAuthOptions = new GoogleAuthOptions
        {
            AllowedDomain = Domain,
            Groups =
            [
                new GroupRoleMapping { GroupName = "Compass-SuperAdmin", Role = "Compass Super Admin" },
                new GroupRoleMapping { GroupName = "Compass-SuperAdmin-dev", Role = "Compass Super Admin" },
                new GroupRoleMapping { GroupName = "Compass-Ops", Role = "Compass Ops" },
                new GroupRoleMapping { GroupName = "Compass-Ops-dev", Role = "Compass Ops" },
            ],
        };
        var service = CreateService(
            people, new RecordingUserRoleService(), googleAuthOptions: googleAuthOptions, environmentName: "Production");
        var (context, auth) = CreateContext();

        // Act — holds a real prod group (Compass-Ops) AND a dev-only group for a HIGHER-privilege role.
        var outcome = await service.CompleteSignInAsync(
            "compass-ops@leadingedje.com",
            null,
            ["Compass-Ops", "Compass-SuperAdmin-dev"],
            context,
            returnUrl: null);

        // Assert
        outcome.Succeeded.ShouldBeTrue();
        var privileges = auth.SignedInPrincipal!.FindAll(AuthConstants.ClaimTypes.PrivilegeClaim)
            .Select(c => c.Value).ToList();
        privileges.ShouldContain("Compass Ops");
        privileges.ShouldNotContain("Compass Super Admin");
    }

    // Mirror of the above in non-production: the bare production group name (Compass-Ops) is out
    // of scope there, so only the dev group's role survives — the opposite outcome from Production,
    // proving the filter is actually environment-scoped and not just "prod always wins".
    [Fact]
    public async Task CompleteSignIn_ProdAndDevCompassGroupsBothPresentInNonProduction_GrantsDevRoleOnlyNotProdRole()
    {
        // Arrange
        var people = new FakePersonProvisioningService();
        people.AddPerson("compass-ops@leadingedje.com", Guid.NewGuid(), isActive: true);
        var googleAuthOptions = new GoogleAuthOptions
        {
            AllowedDomain = Domain,
            Groups =
            [
                new GroupRoleMapping { GroupName = "Compass-SuperAdmin", Role = "Compass Super Admin" },
                new GroupRoleMapping { GroupName = "Compass-SuperAdmin-dev", Role = "Compass Super Admin" },
                new GroupRoleMapping { GroupName = "Compass-Ops", Role = "Compass Ops" },
                new GroupRoleMapping { GroupName = "Compass-Ops-dev", Role = "Compass Ops" },
            ],
        };
        var service = CreateService(
            people, new RecordingUserRoleService(), googleAuthOptions: googleAuthOptions, environmentName: "Development");
        var (context, auth) = CreateContext();

        // Act — same two groups as the Production case above, but the host environment is now dev.
        var outcome = await service.CompleteSignInAsync(
            "compass-ops@leadingedje.com",
            null,
            ["Compass-Ops", "Compass-SuperAdmin-dev"],
            context,
            returnUrl: null);

        // Assert
        outcome.Succeeded.ShouldBeTrue();
        var privileges = auth.SignedInPrincipal!.FindAll(AuthConstants.ClaimTypes.PrivilegeClaim)
            .Select(c => c.Value).ToList();
        privileges.ShouldContain("Compass Super Admin");
        privileges.ShouldNotContain("Compass Ops");
    }

    [Fact]
    public async Task CompleteSignIn_NoMappedGroups_GrantsEdjErOnly()
    {
        // Arrange
        var people = new FakePersonProvisioningService();
        people.AddPerson("baseline@leadingedje.com", Guid.NewGuid(), isActive: true);
        var service = CreateService(people, new RecordingUserRoleService());
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync(
            "baseline@leadingedje.com", null, [], context, returnUrl: null);

        // Assert
        outcome.Succeeded.ShouldBeTrue();
        var privileges = auth.SignedInPrincipal!.FindAll(AuthConstants.ClaimTypes.PrivilegeClaim)
            .Select(c => c.Value).ToList();
        privileges.ShouldBe(["EDJEr"]);
    }

    [Fact]
    public async Task CompleteSignIn_ForeignDomain_DeniesAndSignsOutBothSchemesWithoutSession()
    {
        // Arrange — a person exists, but the domain is not allowed
        var people = new FakePersonProvisioningService();
        people.AddPerson("intruder@gmail.com", Guid.NewGuid(), isActive: true);
        var service = CreateService(people, new RecordingUserRoleService());
        var (context, auth) = CreateContext();

        // Act
        var outcome = await service.CompleteSignInAsync(
            "intruder@gmail.com", null, ["Timesheet-SuperAdmin-dev"], context, returnUrl: null);

        // Assert
        outcome.Succeeded.ShouldBeFalse();
        outcome.DenyReason.ShouldBe("Unauthorized domain");
        outcome.Location.ShouldContain("access-denied");
        auth.SignedInPrincipal.ShouldBeNull();
        auth.SignedOutSchemes.ShouldContain(AuthConstants.Settings.ExternalScheme);
        auth.SignedOutSchemes.ShouldContain(AuthConstants.Settings.LeadingEdjeAuthenticationType);
    }

    [Fact]
    public async Task CompleteSignIn_UnknownEmail_DeniesNoMatchingPersonRecord()
    {
        var service = CreateService(new FakePersonProvisioningService(), new RecordingUserRoleService());
        var (context, auth) = CreateContext();

        var outcome = await service.CompleteSignInAsync(
            "ghost@leadingedje.com", null, [], context, returnUrl: null);

        outcome.Succeeded.ShouldBeFalse();
        outcome.DenyReason.ShouldBe("No Matching Person Record");
        auth.SignedInPrincipal.ShouldBeNull();
    }

    [Fact]
    public async Task CompleteSignIn_InactivePerson_DeniesInactivePersonRecord()
    {
        var people = new FakePersonProvisioningService();
        people.AddPerson("inactive@leadingedje.com", Guid.NewGuid(), isActive: false);
        var service = CreateService(people, new RecordingUserRoleService());
        var (context, auth) = CreateContext();

        var outcome = await service.CompleteSignInAsync(
            "inactive@leadingedje.com", null, [], context, returnUrl: null);

        outcome.Succeeded.ShouldBeFalse();
        outcome.DenyReason.ShouldBe("Inactive Person Record");
        auth.SignedInPrincipal.ShouldBeNull();
    }

    [Theory]
    [InlineData("/timesheet/week", "/timesheet/week")]  // local path passes
    [InlineData("https://evil.example", "/")]           // absolute external → root
    [InlineData("//evil.example", "/")]                 // protocol-relative → root
    [InlineData("/\\evil.example", "/")]                // backslash trick → root
    [InlineData(null, "/")]                             // none → root
    public async Task CompleteSignIn_ReturnUrl_IsSanitizedToLocalPathsOnly(string? returnUrl, string expectedLocation)
    {
        var people = new FakePersonProvisioningService();
        people.AddPerson("redir@leadingedje.com", Guid.NewGuid(), isActive: true);
        var service = CreateService(people, new RecordingUserRoleService());
        var (context, _) = CreateContext();

        var outcome = await service.CompleteSignInAsync(
            "redir@leadingedje.com", null, [], context, returnUrl);

        outcome.Succeeded.ShouldBeTrue();
        outcome.Location.ShouldBe(expectedLocation);
    }

    /// <summary>Fake auth service capturing cookie sign-in/out so claim construction can be asserted without a handler.</summary>
    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public string? SignedInScheme { get; private set; }
        public ClaimsPrincipal? SignedInPrincipal { get; private set; }
        public List<string> SignedOutSchemes { get; } = [];

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

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            if (scheme is not null)
            {
                SignedOutSchemes.Add(scheme);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Records the sign-in role sync; all other members are unused no-ops.</summary>
    private sealed class RecordingUserRoleService : IUserRoleService
    {
        public Guid? SyncedEdjeId { get; private set; }
        public List<string> SyncedRoles { get; } = [];

        public Task SyncFromProfileAsync(Guid edjeId, IEnumerable<string> privileges, string actor = "dev-sync")
        {
            SyncedEdjeId = edjeId;
            SyncedRoles.AddRange(privileges);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetRolesAsync(Guid edjeId) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<bool> HasRoleAsync(Guid edjeId, string role) => Task.FromResult(false);
        public Task<bool> HasAnyRoleAsync(Guid edjeId, params string[] roles) => Task.FromResult(false);
        public Task AssignRoleAsync(Guid edjeId, string role, string actor, string reason) => Task.CompletedTask;
        public Task RemoveRoleAsync(Guid edjeId, string role, string actor, string reason) => Task.CompletedTask;
        public Task<IReadOnlyList<UserRole>> GetUsersByRoleAsync(string role) =>
            Task.FromResult<IReadOnlyList<UserRole>>([]);
        public Task<int> EnsureBaseRoleForAllEmployeesAsync(IEnumerable<(Guid EdjeId, string Name)> employees) =>
            Task.FromResult(0);
    }
}
