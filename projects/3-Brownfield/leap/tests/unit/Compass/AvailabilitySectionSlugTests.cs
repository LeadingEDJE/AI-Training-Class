using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The one table mapping an Availability Report section to its kebab-case name (issue #337).
/// </summary>
/// <remarks>
/// It has two consumers in different layers — the endpoint parses a query string through it, the
/// service names a file with it — and the two must agree, so the round trip is the property worth
/// asserting rather than either direction alone.
/// </remarks>
public class AvailabilitySectionSlugTests
{
    [Theory]
    [InlineData(AvailabilitySection.CurrentlyAvailable, "currently-available")]
    [InlineData(AvailabilitySection.ConfirmedRollouts, "confirmed-rollouts")]
    [InlineData(AvailabilitySection.UnconfirmedSows, "unconfirmed-sows")]
    public void Of_RendersTheKebabCaseName(AvailabilitySection section, string expected)
    {
        AvailabilitySectionSlug.Of(section).ShouldBe(expected);
    }

    [Fact]
    public void EverySection_RoundTrips()
    {
        // The invariant both consumers depend on. Enumerating the enum rather than listing the three
        // means a fourth section added without a slug fails here instead of at a call site.
        foreach (var section in Enum.GetValues<AvailabilitySection>())
        {
            AvailabilitySectionSlug.TryParse(AvailabilitySectionSlug.Of(section), out var parsed)
                .ShouldBeTrue($"{section} has no slug");
            parsed.ShouldBe(section);
        }
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    [InlineData("CurrentlyAvailable")]
    public void TryParse_RejectsAnythingNotInTheTable(string slug)
    {
        // "CurrentlyAvailable" is the interesting one: it is the ENUM MEMBER name, which is what
        // minimal-API binding would have accepted. Rejecting it keeps one spelling of each section in
        // the URL, which matters because the same slug names the downloaded file.
        AvailabilitySectionSlug.TryParse(slug, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Confirmed-Rollouts")]
    [InlineData("CONFIRMED-ROLLOUTS")]
    public void TryParse_IsCaseSensitive(string slug)
    {
        // Ordinal on purpose. These URLs are built by our own frontend, and accepting near-misses
        // makes the canonical spelling ambiguous — and therefore the filenames ambiguous too.
        AvailabilitySectionSlug.TryParse(slug, out _).ShouldBeFalse();
    }

    [Fact]
    public void Of_ThrowsForAnUndefinedSection()
    {
        // The switch's default. Unreachable today; the guard is what makes a fourth section a loud
        // failure rather than a silently mis-named file.
        Should.Throw<ArgumentOutOfRangeException>(() =>
            AvailabilitySectionSlug.Of((AvailabilitySection)99));
    }
}
