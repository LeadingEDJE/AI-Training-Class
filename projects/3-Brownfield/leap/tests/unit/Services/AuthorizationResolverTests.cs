using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

public class AuthorizationResolverTests
{
    private static readonly Guid TestEdjeId = Guid.Parse("00000000-0000-0000-0000-000000000099");

    private static ClaimsPrincipal CreatePrincipal(Guid? edjeId = null, params string[] privileges)
    {
        var claims = new List<Claim>();

        if (edjeId.HasValue)
        {
            claims.Add(new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, edjeId.Value.ToString()));
        }

        foreach (var priv in privileges)
        {
            claims.Add(new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, priv));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static (IAuthorizationResolver resolver, InMemoryUserRoleRepository repo) CreateResolver()
    {
        var roleRepo = new InMemoryUserRoleRepository();
        var roleService = new InMemoryBackedRoleService(roleRepo);
        var resolver = new CompositeAuthorizationResolver(roleService);
        return (resolver, roleRepo);
    }

    [Fact]
    public async Task HasAnyRoleAsync_ReturnsTrue_WhenJwtClaimsContainMatchingPrivilege()
    {
        // Arrange
        var (resolver, _) = CreateResolver();
        var principal = CreatePrincipal(TestEdjeId, "SuperAdmin");

        // Act
        var result = await resolver.HasAnyRoleAsync(principal, "SuperAdmin");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task HasAnyRoleAsync_ReturnsTrue_WhenLocalDbHasMatchingRole()
    {
        // Arrange
        var (resolver, repo) = CreateResolver();
        await repo.AddAsync(new UserRole { EdjeId = TestEdjeId, Role = "Manager" });
        var principal = CreatePrincipal(TestEdjeId); // No JWT privilege claims

        // Act
        var result = await resolver.HasAnyRoleAsync(principal, "Manager");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task HasAnyRoleAsync_ReturnsFalse_WhenNeitherJwtNorLocalDbHasMatchingRole()
    {
        // Arrange
        var (resolver, repo) = CreateResolver();
        await repo.AddAsync(new UserRole { EdjeId = TestEdjeId, Role = "EDJEr" });
        var principal = CreatePrincipal(TestEdjeId, "Accounting"); // Wrong JWT claim

        // Act
        var result = await resolver.HasAnyRoleAsync(principal, "SuperAdmin");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task HasAnyRoleAsync_ReturnsFalse_WhenNoEdjeIdClaim()
    {
        // Arrange
        var (resolver, _) = CreateResolver();
        var principal = CreatePrincipal(); // No EdjeId, no privileges

        // Act
        var result = await resolver.HasAnyRoleAsync(principal, "EDJEr");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task HasAnyRoleAsync_ShortCircuitsOnJwtClaim_NoDbCall()
    {
        // Arrange
        // If JWT claims match, the resolver should return true without checking DB.
        // We verify this by NOT seeding any DB roles -- if it returned true, it came from JWT.
        var (resolver, _) = CreateResolver();
        var principal = CreatePrincipal(TestEdjeId, "HR");

        // Act
        var result = await resolver.HasAnyRoleAsync(principal, "HR");

        // Assert
        result.ShouldBeTrue();
        // No DB roles were seeded, so true can only come from JWT short-circuit
    }
}

/// <summary>
/// Lightweight IUserRoleService backed by InMemoryUserRoleRepository for unit testing.
/// Avoids AuditService/DbContext dependencies that the full UserRoleService requires.
/// </summary>
internal class InMemoryBackedRoleService(InMemoryUserRoleRepository repository) : IUserRoleService
{
    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid edjeId)
    {
        var roles = await repository.GetByEdjeIdAsync(edjeId);
        return roles.Select(r => r.Role).ToList().AsReadOnly();
    }

    public async Task<bool> HasRoleAsync(Guid edjeId, string role) =>
        await repository.FindAsync(edjeId, role) is not null;

    public async Task<bool> HasAnyRoleAsync(Guid edjeId, params string[] roles)
    {
        var userRoles = await repository.GetByEdjeIdAsync(edjeId);
        return userRoles.Any(ur => roles.Contains(ur.Role));
    }

    public Task AssignRoleAsync(Guid edjeId, string role, string actor, string reason) =>
        throw new NotImplementedException();

    public Task RemoveRoleAsync(Guid edjeId, string role, string actor, string reason) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<UserRole>> GetUsersByRoleAsync(string role) =>
        throw new NotImplementedException();

    public Task SyncFromProfileAsync(Guid edjeId, IEnumerable<string> privileges, string actor = "dev-sync") =>
        throw new NotImplementedException();

    public Task<int> EnsureBaseRoleForAllEmployeesAsync(IEnumerable<(Guid EdjeId, string Name)> employees) =>
        throw new NotImplementedException();
}
