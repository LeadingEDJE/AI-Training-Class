using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Data.Repositories;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

/// <summary>
/// Direct EF-Core repository unit tests for <see cref="PersonRepository"/> over the InMemory provider.
/// These cover the custom query members (ordering, email-existence with/without an excluded id) and
/// the AddAsync stage tracking that the shared-store endpoint factories seed around rather than through.
/// </summary>
public class AbsorbedRepositoriesTests
{
    private static LeapDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase($"AbsorbedRepoTestDb_{Guid.NewGuid():N}")
            .Options;
        return new LeapDbContext(options);
    }

    [Fact]
    public async Task PersonRepository_GetAllAsync_OrdersByLastNameThenFirstName()
    {
        // Arrange
        await using var db = CreateContext();
        db.People.AddRange(
            new Person { Id = Guid.NewGuid(), FirstName = "Zoe", LastName = "Adams", Email = "z@x.test" },
            new Person { Id = Guid.NewGuid(), FirstName = "Amy", LastName = "Adams", Email = "a@x.test" },
            new Person { Id = Guid.NewGuid(), FirstName = "Bob", LastName = "Baker", Email = "b@x.test" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = new PersonRepository(db);

        // Act
        var people = await repo.GetAllAsync();

        // Assert — Adams before Baker; within Adams, Amy before Zoe.
        people.Select(p => p.FirstName).ShouldBe(new[] { "Amy", "Zoe", "Bob" });
    }

    [Fact]
    public async Task PersonRepository_GetByIdAsync_ExistingId_ReturnsPerson()
    {
        // Arrange
        await using var db = CreateContext();
        var id = Guid.NewGuid();
        db.People.Add(new Person { Id = id, FirstName = "Sam", LastName = "Lee", Email = "s@x.test" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = new PersonRepository(db);

        // Act
        var person = await repo.GetByIdAsync(id);

        // Assert
        person.ShouldNotBeNull();
        person!.FirstName.ShouldBe("Sam");
    }

    [Fact]
    public async Task PersonRepository_GetByEdjeIdAsync_ExistingEdjeId_ReturnsPerson()
    {
        // Arrange
        await using var db = CreateContext();
        var edjeId = Guid.NewGuid();
        db.People.Add(new Person { Id = Guid.NewGuid(), EdjeId = edjeId, FirstName = "Avery", LastName = "Quinn", Email = "avery.quinn@example.com" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = new PersonRepository(db);

        // Act
        var person = await repo.GetByEdjeIdAsync(edjeId);

        // Assert
        person.ShouldNotBeNull();
        person!.FirstName.ShouldBe("Avery");
    }

    [Fact]
    public async Task PersonRepository_GetByEdjeIdAsync_UnknownEdjeId_ReturnsNull()
    {
        // Arrange
        await using var db = CreateContext();
        var repo = new PersonRepository(db);

        // Act
        var person = await repo.GetByEdjeIdAsync(Guid.NewGuid());

        // Assert
        person.ShouldBeNull();
    }

    [Fact]
    public async Task PersonRepository_EmailExistsAsync_MatchingEmail_ReturnsTrue()
    {
        // Arrange — case-insensitive comparison via lower() on both sides.
        await using var db = CreateContext();
        db.People.Add(new Person { Id = Guid.NewGuid(), FirstName = "Ann", LastName = "Poe", Email = "Ann.Poe@Example.Test" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = new PersonRepository(db);

        // Act
        var exists = await repo.EmailExistsAsync("ann.poe@example.test", excludeId: null);

        // Assert
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task PersonRepository_EmailExistsAsync_ExcludingOwningRow_ReturnsFalse()
    {
        // Arrange — the only matching row is the excluded id, so no other row collides.
        await using var db = CreateContext();
        var id = Guid.NewGuid();
        db.People.Add(new Person { Id = id, FirstName = "Ann", LastName = "Poe", Email = "ann.poe@example.test" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = new PersonRepository(db);

        // Act
        var exists = await repo.EmailExistsAsync("ann.poe@example.test", excludeId: id);

        // Assert
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task PersonRepository_EmailExistsAsync_UnknownEmail_ReturnsFalse()
    {
        // Arrange
        await using var db = CreateContext();
        var repo = new PersonRepository(db);

        // Act
        var exists = await repo.EmailExistsAsync("nobody@example.test", excludeId: null);

        // Assert
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task PersonRepository_AddAsync_TracksEntityAndPersistsOnSave()
    {
        // Arrange
        await using var db = CreateContext();
        var repo = new PersonRepository(db);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "New", LastName = "Hire", Email = "n@x.test" };

        // Act
        var returned = await repo.AddAsync(person);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        returned.ShouldBeSameAs(person);
        (await db.People.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }
}
