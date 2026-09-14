using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

public class AuditableEntityTests : IDisposable
{
    private readonly LeapDbContext _context;

    public AuditableEntityTests()
    {
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new LeapDbContext(options);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task SaveChangesAsync_AddedEntity_SetsCreatedAtAndUpdatedAtToUtcNow()
    {
        // Arrange
        var before = DateTime.UtcNow;
        var role = new UserRole { EdjeId = Guid.NewGuid(), Role = "EDJEr" };
        _context.UserRoles.Add(role);

        // Act
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        var after = DateTime.UtcNow;
        role.CreatedAt.ShouldBeGreaterThanOrEqualTo(before);
        role.CreatedAt.ShouldBeLessThanOrEqualTo(after);
        role.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
        role.UpdatedAt.ShouldBeLessThanOrEqualTo(after);
    }

    [Fact]
    public async Task SaveChangesAsync_AddedEntity_SetsCreatedByAndUpdatedByToSystemWhenNull()
    {
        // Arrange
        var role = new UserRole { EdjeId = Guid.NewGuid(), Role = "EDJEr" };
        _context.UserRoles.Add(role);

        // Act
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        role.CreatedBy.ShouldBe("system");
        role.UpdatedBy.ShouldBe("system");
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_UpdatesOnlyUpdatedAtAndUpdatedBy()
    {
        // Arrange
        var role = new UserRole { EdjeId = Guid.NewGuid(), Role = "EDJEr" };
        _context.UserRoles.Add(role);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var originalCreatedAt = role.CreatedAt;
        var originalCreatedBy = role.CreatedBy;

        // Act
        role.Role = "Manager";
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        role.CreatedAt.ShouldBe(originalCreatedAt);
        role.CreatedBy.ShouldBe(originalCreatedBy);
        role.UpdatedAt.ShouldBeGreaterThanOrEqualTo(originalCreatedAt);
        role.UpdatedBy.ShouldBe("system");
    }

    [Fact]
    public async Task SaveChangesAsync_AddedEntity_DoesNotOverwriteCreatedByIfAlreadySet()
    {
        // Arrange
        var role = new UserRole { EdjeId = Guid.NewGuid(), Role = "EDJEr", CreatedBy = "emp-001", UpdatedBy = "emp-001" };
        _context.UserRoles.Add(role);

        // Act
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        role.CreatedBy.ShouldBe("emp-001");
        role.UpdatedBy.ShouldBe("emp-001");
    }

    [Fact]
    public async Task AuditableEntity_Inheritance_ProvidesAuditProperties()
    {
        // Arrange & Act
        var role = new UserRole();

        // Assert - UserRole inherits from AuditableEntity and has audit properties
        role.ShouldBeAssignableTo<AuditableEntity>();
        role.CreatedAt.ShouldBe(default(DateTime));
        role.UpdatedAt.ShouldBe(default(DateTime));
        role.CreatedBy.ShouldBeNull();
        role.UpdatedBy.ShouldBeNull();

        await Task.CompletedTask;
    }
}
