using LeadingEDJE.Leap.Api.Platform.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Pins the email -> display-name derivation the bootstrap SuperAdmin seeder uses so configured admins
/// are not listed with a blank NAME in Admin -> People before they have ever signed in (smoke-test
/// finding P2, 2026-07-30).
/// </summary>
/// <remarks>
/// This is deliberately CONSERVATIVE. A derived name is a guess, and D-08's "never a fabricated name"
/// instinct is right — so derivation only fires when the local part clearly encodes two name tokens
/// separated by <c>.</c>, <c>_</c> or <c>-</c>. Anything else (<c>jkirk@</c>, <c>admin@</c>,
/// <c>svc-deploy-01@</c>) yields null and the row stays identity-only. Whatever this produces is a
/// PLACEHOLDER: the row carries a non-null provenance <c>Source</c>, so the authoritative Google
/// assertion name replaces it on first sign-in (see <see cref="PersonDisplayNameTests"/>).
/// </remarks>
public class EmailDerivedNameTests
{
    [Theory]
    [InlineData("avery.quinn@example.com", "Avery Quinn")]
    [InlineData("wren.castellan@leadingedje.com", "Wren Castellan")]
    [InlineData("nova.brightwater@leadingedje.com", "Nova Brightwater")]
    [InlineData("felix.andrada@leadingedje.com", "Felix Andrada")]
    [InlineData("ada_lovelace@leadingedje.com", "Ada Lovelace")]
    [InlineData("grace-hopper@leadingedje.com", "Grace Hopper")]
    // Mixed/upper input is normalized, not echoed.
    [InlineData("AVERY.QUINN@EXAMPLE.COM", "Avery Quinn")]
    // Three tokens: first token is the given name, the remainder is kept whole (same rule the
    // assertion-name splitter uses for "Ada B. Lovelace").
    [InlineData("mary.jane.watson@leadingedje.com", "Mary Jane Watson")]
    public void TryDerive_LocalPartEncodesTwoNameTokens_DerivesTitleCasedName(string email, string expected)
    {
        // Act
        var derived = EmailDerivedName.TryDerive(email);

        // Assert
        derived.ShouldBe(expected);
    }

    [Theory]
    // No separator — cannot be split without guessing where the surname starts.
    [InlineData("jkirk@leadingedje.com")]
    [InlineData("admin@leadingedje.com")]
    // Role / service accounts: a derived "Svc Deploy 01" would be actively misleading in a people list.
    [InlineData("svc-deploy-01@leadingedje.com")]
    [InlineData("no-reply@leadingedje.com")]
    // Digits are not names.
    [InlineData("user.1234@leadingedje.com")]
    // Single-character tokens carry no information.
    [InlineData("a.b@leadingedje.com")]
    // Unusable input.
    [InlineData("@leadingedje.com")]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData(null)]
    public void TryDerive_LocalPartIsNotAName_ReturnsNullRatherThanGuessing(string? email)
    {
        // Act
        var derived = EmailDerivedName.TryDerive(email);

        // Assert
        derived.ShouldBeNull();
    }
}
