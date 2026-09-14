using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

public class InMemoryUserRoleRepositoryTests
{
    private readonly InMemoryUserRoleRepository _repo = new();

    [Fact]
    public async Task AddAsync_AssignsId_AndReturnsRole()
    {
        // Arrange
        var role = new UserRole { EdjeId = Guid.NewGuid(), Role = "Manager" };

        // Act
        var result = await _repo.AddAsync(role);

        // Assert
        result.Id.ShouldBeGreaterThan(0);
        result.Role.ShouldBe("Manager");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllRoles()
    {
        // Arrange
        await _repo.AddAsync(new UserRole { EdjeId = Guid.NewGuid(), Role = "EDJEr" });
        await _repo.AddAsync(new UserRole { EdjeId = Guid.NewGuid(), Role = "Manager" });

        // Act
        var result = await _repo.GetAllAsync();

        // Assert
        result.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetByEdjeIdAsync_ReturnsMatchingRoles()
    {
        // Arrange
        var edjeId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await _repo.AddAsync(new UserRole { EdjeId = edjeId, Role = "EDJEr" });
        await _repo.AddAsync(new UserRole { EdjeId = edjeId, Role = "Manager" });
        await _repo.AddAsync(new UserRole { EdjeId = otherId, Role = "SuperAdmin" });

        // Act
        var result = await _repo.GetByEdjeIdAsync(edjeId);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldAllBe(r => r.EdjeId == edjeId);
    }

    [Fact]
    public async Task GetByRoleAsync_ReturnsMatchingRoles()
    {
        // Arrange
        await _repo.AddAsync(new UserRole { EdjeId = Guid.NewGuid(), Role = "Manager" });
        await _repo.AddAsync(new UserRole { EdjeId = Guid.NewGuid(), Role = "Manager" });
        await _repo.AddAsync(new UserRole { EdjeId = Guid.NewGuid(), Role = "EDJEr" });

        // Act
        var result = await _repo.GetByRoleAsync("Manager");

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldAllBe(r => r.Role == "Manager");
    }

    [Fact]
    public async Task FindAsync_ReturnsMatch_WhenExists()
    {
        // Arrange
        var edjeId = Guid.NewGuid();
        await _repo.AddAsync(new UserRole { EdjeId = edjeId, Role = "Manager" });

        // Act
        var result = await _repo.FindAsync(edjeId, "Manager");

        // Assert
        result.ShouldNotBeNull();
        result.Role.ShouldBe("Manager");
    }

    [Fact]
    public async Task FindAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _repo.FindAsync(Guid.NewGuid(), "NonExistent");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task RemoveAsync_DeletesRole()
    {
        // Arrange
        var role = await _repo.AddAsync(new UserRole { EdjeId = Guid.NewGuid(), Role = "Manager" });

        // Act
        await _repo.RemoveAsync(role);
        var all = await _repo.GetAllAsync();

        // Assert
        all.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetByEdjeIdAsync_ReturnsEmpty_WhenNoMatch()
    {
        // Act
        var result = await _repo.GetByEdjeIdAsync(Guid.NewGuid());

        // Assert
        result.Count.ShouldBe(0);
    }
}
