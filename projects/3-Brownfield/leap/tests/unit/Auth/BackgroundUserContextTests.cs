using LeadingEDJE.Leap.Api.Platform.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Auth;

/// <summary>
/// Unit coverage for <see cref="BackgroundUserContext"/>, the current-user context a scope with no
/// HTTP request resolves so a background job (the weekly summary) can read the Compass directory.
/// </summary>
/// <remarks>
/// The split is the whole point: privilege resolution is background-safe (empty set, Baseline tier),
/// while identity throws so a caller-identity read from a background scope fails loudly rather than
/// silently becoming "no match".
/// </remarks>
public class BackgroundUserContextTests
{
    [Fact]
    public void Privileges_IsEmpty_SoDirectoryReadsResolveAtBaseline()
    {
        // Arrange & Act
        var context = new BackgroundUserContext();

        // Assert
        context.Privileges.ShouldBeEmpty();
    }

    [Fact]
    public void HasPrivilege_IsAlwaysFalse()
    {
        // Arrange & Act
        var context = new BackgroundUserContext();

        // Assert — deny-all is the safe-closed answer for a caller with no authenticated identity.
        context.HasPrivilege("SuperAdmin").ShouldBeFalse();
    }

    [Fact]
    public void Identity_Throws_BecauseABackgroundScopeHasNoCaller()
    {
        // Arrange
        var context = new BackgroundUserContext();

        // Act & Assert — reading an identity in a background scope is a bug, and must be loud.
        Should.Throw<InvalidOperationException>(() => context.EdjeId);
        Should.Throw<InvalidOperationException>(() => context.Email);
        Should.Throw<InvalidOperationException>(() => context.TpsEmployeeId);
    }
}
