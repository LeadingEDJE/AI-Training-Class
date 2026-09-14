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
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Auth;

public class CurrentUserContextTests
{
    private static (ICurrentUserContext context, InMemoryUserRoleRepository repo) CreateContext(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, AuthConstants.Settings.LeadingEdjeAuthenticationType);
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = new StubHttpContextAccessor(httpContext);
        var roleRepo = new InMemoryUserRoleRepository();
        var auditService = new StubAuditService();
        var dbContext = new LeapDbContext(
            new DbContextOptionsBuilder<LeapDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var roleService = new UserRoleService(roleRepo, auditService, dbContext);
        return (new CurrentUserContext(accessor, roleService), roleRepo);
    }

    [Fact]
    public void EdjeId_ReturnsParsedGuidFromEdjeIdClaim()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        var (context, _) = CreateContext(
            new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, expectedId.ToString()));

        // Act
        var result = context.EdjeId;

        // Assert
        result.ShouldBe(expectedId);
    }

    [Fact]
    public void Email_ReturnsEmailClaimValue()
    {
        // Arrange
        var (context, _) = CreateContext(
            new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Email, "test@leadingedje.com"));

        // Act
        var result = context.Email;

        // Assert
        result.ShouldBe("test@leadingedje.com");
    }

    [Fact]
    public void Email_ReturnsEmptyString_WhenMissing()
    {
        // Arrange
        var (context, _) = CreateContext(
            new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, Guid.NewGuid().ToString()));

        // Act
        var result = context.Email;

        // Assert
        result.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Privileges_ReturnsLocalRolesUnionedWithJwtSuperAdmin()
    {
        // Arrange
        var edjeId = Guid.NewGuid();
        var (context, repo) = CreateContext(
            new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, edjeId.ToString()),
            new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, "SuperAdmin"));

        // Seed local roles
        await repo.AddAsync(new UserRole { EdjeId = edjeId, Role = "EDJEr" });
        await repo.AddAsync(new UserRole { EdjeId = edjeId, Role = "Manager" });

        // Act
        var result = context.Privileges;

        // Assert - local roles + JWT SuperAdmin
        result.Count.ShouldBe(3);
        result.ShouldContain("EDJEr");
        result.ShouldContain("Manager");
        result.ShouldContain("SuperAdmin");
    }

    [Fact]
    public async Task HasPrivilege_ReturnsTrue_WhenRoleInLocalDb()
    {
        // Arrange
        var edjeId = Guid.NewGuid();
        var (context, repo) = CreateContext(
            new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, edjeId.ToString()));

        await repo.AddAsync(new UserRole { EdjeId = edjeId, Role = "EDJEr" });

        // Act
        var result = context.HasPrivilege("EDJEr");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void HasPrivilege_ReturnsFalse_WhenPrivilegeAbsent()
    {
        // Arrange
        var (context, _) = CreateContext(
            new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, Guid.NewGuid().ToString()));

        // Act
        var result = context.HasPrivilege("SuperAdmin");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void HasPrivilege_ReturnsTrue_ForJwtSuperAdmin_EvenWithEmptyLocalRoles()
    {
        // Arrange
        var (context, _) = CreateContext(
            new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, Guid.NewGuid().ToString()),
            new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, "SuperAdmin"));

        // Act
        var result = context.HasPrivilege("SuperAdmin");

        // Assert - JWT SuperAdmin is always honored
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task Privileges_CachesResult_SecondAccessDoesNotRequery()
    {
        // Arrange
        var edjeId = Guid.NewGuid();
        var (context, repo) = CreateContext(
            new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, edjeId.ToString()));

        await repo.AddAsync(new UserRole { EdjeId = edjeId, Role = "EDJEr" });

        // Act - access Privileges twice
        var first = context.Privileges;
        var second = context.Privileges;

        // Assert - same instance returned (cached)
        ReferenceEquals(first, second).ShouldBeTrue();
        first.Count.ShouldBe(1);
        first.ShouldContain("EDJEr");
    }

    [Fact]
    public async Task Privileges_IncludesAllJwtPrivilegeClaims_NotJustSuperAdmin()
    {
        // Arrange
        var edjeId = Guid.NewGuid();
        var (context, repo) = CreateContext(
            new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, edjeId.ToString()),
            new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, "Manager"),
            new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, "HR"));

        // Seed a local-only role
        await repo.AddAsync(new UserRole { EdjeId = edjeId, Role = "Ops" });

        // Act
        var result = context.Privileges;

        // Assert — JWT Manager + HR merged with local Ops
        result.ShouldContain("Manager");
        result.ShouldContain("HR");
        result.ShouldContain("Ops");
        result.Count.ShouldBe(3);
    }

    [Fact]
    public void Privileges_JwtRolesHonoredEvenWithEmptyLocalDb()
    {
        // Arrange
        var (context, _) = CreateContext(
            new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, Guid.NewGuid().ToString()),
            new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, "EDJEr"),
            new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, "Manager"));

        // Act
        var result = context.Privileges;

        // Assert — JWT roles present even with no local DB entries
        result.ShouldContain("EDJEr");
        result.ShouldContain("Manager");
        result.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Privileges_DeduplicatesWhenJwtAndLocalOverlap()
    {
        // Arrange
        var edjeId = Guid.NewGuid();
        var (context, repo) = CreateContext(
            new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, edjeId.ToString()),
            new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, "Manager"),
            new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, "SuperAdmin"));

        // Same role in local DB as in JWT
        await repo.AddAsync(new UserRole { EdjeId = edjeId, Role = "Manager" });
        await repo.AddAsync(new UserRole { EdjeId = edjeId, Role = "EDJEr" });

        // Act
        var result = context.Privileges;

        // Assert — no duplicates, union of both sources
        result.ShouldContain("Manager");
        result.ShouldContain("SuperAdmin");
        result.ShouldContain("EDJEr");
        result.Count.ShouldBe(3); // Manager not duplicated
    }

    [Fact]
    public void EdjeId_ThrowsInvalidOperationException_WhenMissing()
    {
        // Arrange
        var (context, _) = CreateContext(
            new Claim(ClaimTypes.Email, "test@leadingedje.com"));

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => context.EdjeId);
    }

    /// <summary>
    /// Simple IHttpContextAccessor stub (no mocking framework per project convention).
    /// </summary>
    private sealed class StubHttpContextAccessor(HttpContext httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
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
