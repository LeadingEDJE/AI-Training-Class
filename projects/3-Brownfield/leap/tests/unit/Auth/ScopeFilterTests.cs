using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Auth;

/// <summary>
/// Proves the ICurrentUserContext scope filtering pattern (AUTH-07).
/// Uses a stub service that filters items by EdjeId, demonstrating how
/// feature services will implement data scoping in later phases.
/// </summary>
public class ScopeFilterTests
{
    private static readonly Guid AliceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BobId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly (Guid ownerId, string name)[] MixedOwnerItems =
    [
        (AliceId, "Alice Item 1"),
        (AliceId, "Alice Item 2"),
        (BobId, "Bob Item 1"),
    ];

    [Fact]
    public void GetItemsForCurrentUser_AsEDJEr_ReturnsOnlyOwnItems()
    {
        // Arrange
        var userContext = new StubCurrentUserContext(AliceId, "alice@test.com", ["EDJEr"]);
        var service = new StubScopeAwareService(userContext);

        // Act
        var result = service.GetItemsForCurrentUser(MixedOwnerItems);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldAllBe(item => item.name.StartsWith("Alice"));
    }

    [Fact]
    public void GetItemsForCurrentUser_AsManager_ReturnsAllItems()
    {
        // Arrange
        var userContext = new StubCurrentUserContext(AliceId, "alice@test.com", ["EDJEr", "Manager"]);
        var service = new StubScopeAwareService(userContext);

        // Act
        var result = service.GetItemsForCurrentUser(MixedOwnerItems);

        // Assert
        result.Count.ShouldBe(3);
    }

    [Fact]
    public void HasPrivilege_WithMatchingPrivilege_ReturnsTrue()
    {
        // Arrange
        var userContext = new StubCurrentUserContext(AliceId, "alice@test.com", ["EDJEr", "Manager"]);

        // Act & Assert
        userContext.HasPrivilege("EDJEr").ShouldBeTrue();
        userContext.HasPrivilege("Manager").ShouldBeTrue();
        userContext.HasPrivilege("SuperAdmin").ShouldBeFalse();
    }
}

/// <summary>
/// Stub implementation of ICurrentUserContext for unit testing scope filtering.
/// </summary>
internal class StubCurrentUserContext(Guid edjeId, string email, IReadOnlyList<string> privileges) : ICurrentUserContext
{
    public Guid EdjeId => edjeId;
    public string Email => email;
    public string TpsEmployeeId => edjeId.ToString();
    public IReadOnlyList<string> Privileges => privileges;
    public bool HasPrivilege(string privilege) => privileges.Contains(privilege);
}

/// <summary>
/// Stub service demonstrating the scope filtering pattern.
/// EDJEr users see only their own items; Manager+ users see all items.
/// </summary>
internal class StubScopeAwareService(ICurrentUserContext currentUserContext)
{
    public List<(Guid ownerId, string name)> GetItemsForCurrentUser(
        IEnumerable<(Guid ownerId, string name)> items)
    {
        if (currentUserContext.HasPrivilege("Manager") ||
            currentUserContext.HasPrivilege("SuperAdmin"))
        {
            return items.ToList();
        }

        return items.Where(i => i.ownerId == currentUserContext.EdjeId).ToList();
    }
}
