using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data.Repositories;

public class InMemoryRepositoryTests
{
    // A minimal int-keyed POCO stands in for a real entity here: InMemoryRepository<T>'s CRUD
    // behavior is generic (reflection over an "Id" property), and no surviving Platform/Compass
    // domain type is both int-keyed and a legitimate fit for this generic port.
    private sealed class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    private readonly InMemoryRepository<TestEntity> _repository;

    public InMemoryRepositoryTests()
    {
        _repository = new InMemoryRepository<TestEntity>();
    }

    [Fact]
    public async Task GetAllAsync_NoItems_ReturnsEmptyList()
    {
        // Act
        IEnumerable<TestEntity> result = await _repository.GetAllAsync();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AssignsAutoIncrementingId_ReturnsEntity()
    {
        // Arrange
        var entity = new TestEntity { Name = "Client A" };

        // Act
        TestEntity result = await _repository.CreateAsync(entity);

        // Assert
        result.Id.ShouldBe(1);
        result.Name.ShouldBe("Client A");
    }

    [Fact]
    public async Task CreateAsync_MultipleEntities_AssignsIncrementingIds()
    {
        // Arrange
        var first = new TestEntity { Name = "First" };
        var second = new TestEntity { Name = "Second" };

        // Act
        TestEntity result1 = await _repository.CreateAsync(first);
        TestEntity result2 = await _repository.CreateAsync(second);

        // Assert
        result1.Id.ShouldBe(1);
        result2.Id.ShouldBe(2);
    }

    [Fact]
    public async Task GetByIdAsync_EntityExists_ReturnsEntity()
    {
        // Arrange
        TestEntity created = await _repository.CreateAsync(new TestEntity { Name = "Test" });

        // Act
        TestEntity? result = await _repository.GetByIdAsync(created.Id);

        // Assert
        result.ShouldNotBeNull();
        result.Name.ShouldBe("Test");
    }

    [Fact]
    public async Task GetByIdAsync_EntityDoesNotExist_ReturnsNull()
    {
        // Act
        TestEntity? result = await _repository.GetByIdAsync(999);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateAsync_EntityExists_ReturnsUpdatedEntity()
    {
        // Arrange
        await _repository.CreateAsync(new TestEntity { Name = "Old Name" });
        var updated = new TestEntity { Name = "New Name", IsActive = false };

        // Act
        TestEntity? result = await _repository.UpdateAsync(1, updated);

        // Assert
        result.ShouldNotBeNull();
        result.Name.ShouldBe("New Name");
        result.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateAsync_EntityDoesNotExist_ReturnsNull()
    {
        // Arrange
        var updated = new TestEntity { Name = "New Name" };

        // Act
        TestEntity? result = await _repository.UpdateAsync(999, updated);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAsync_EntityExists_RemovesAndReturnsTrue()
    {
        // Arrange
        TestEntity created = await _repository.CreateAsync(new TestEntity { Name = "To Delete" });

        // Act
        bool result = await _repository.DeleteAsync(created.Id);

        // Assert
        result.ShouldBeTrue();
        TestEntity? afterDelete = await _repository.GetByIdAsync(created.Id);
        afterDelete.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAsync_EntityDoesNotExist_ReturnsFalse()
    {
        // Act
        bool result = await _repository.DeleteAsync(999);

        // Assert
        result.ShouldBeFalse();
    }
}
