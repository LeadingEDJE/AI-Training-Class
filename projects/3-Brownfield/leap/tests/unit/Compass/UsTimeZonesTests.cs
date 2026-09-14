using LeadingEDJE.Leap.Api.Modules.Compass;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// Pins the closed vocabulary of timezones an EDJEr may carry (issue #420, PRD v9 FR-8.1/FR-8.6).
/// </summary>
/// <remarks>
/// <para>
/// The expected ids are written out literally here rather than read from the class under test.
/// A test that asks <c>UsTimeZones</c> what it holds and then agrees with the answer asserts nothing;
/// the point of this file is that the six ids are the six OOTO already uses
/// (<c>web/ooto/src/utils/date.ts</c>), so a change to either side has to be a deliberate edit in two
/// places. Same discipline as the local state array in
/// <c>tests/unit/Data/CompassDirectorySeederTests.cs</c>.
/// </para>
/// <para>
/// No display labels here, on purpose. The dropdown's label-to-id pairing belongs to the screen
/// that renders it (issue #421); this class is the stored vocabulary only. If #421 wants the labels in
/// C# rather than in the SPA, it adds them and extends this file.
/// </para>
/// </remarks>
public class UsTimeZonesTests
{
    /// <summary>
    /// The six, in the order OOTO lists them. Independent transcription — see the class remarks.
    /// </summary>
    private static readonly string[] ExpectedIanaIds =
    [
        "America/New_York",
        "America/Chicago",
        "America/Denver",
        "America/Los_Angeles",
        "Pacific/Honolulu",
        "America/Anchorage",
    ];

    [Fact]
    public void All_IsTheSixUsZonesOotoAlreadyUses()
    {
        // Act + Assert — order matters: it is the order the dropdown will present (FR-8.1b).
        UsTimeZones.All.ShouldBe(ExpectedIanaIds);
    }

    [Fact]
    public void All_HoldsNoDuplicates()
    {
        // A duplicate would render twice in the dropdown and inflate any count taken over the list.
        UsTimeZones.All.Distinct(StringComparer.Ordinal).Count().ShouldBe(UsTimeZones.All.Length);
    }

    [Fact]
    public void All_AreResolvableIanaIdentifiers()
    {
        // FR-8.6 — the stored value must be usable for date manipulation, not merely a string that
        // looks like a zone. .NET resolves IANA ids on every platform we run on (ICU), so this is the
        // executable form of "is a usable IANA value".
        foreach (var id in UsTimeZones.All)
        {
            Should.NotThrow(
                () => TimeZoneInfo.FindSystemTimeZoneById(id),
                $"'{id}' is not a resolvable IANA timezone identifier"
            );
        }
    }

    [Fact]
    public void Default_IsEasternAndIsOneOfTheSix()
    {
        // The load default (FR-8.3) and the transitional entity default are the same value, so the
        // two cannot drift apart into "Eastern" meaning two different strings.
        UsTimeZones.Default.ShouldBe("America/New_York");
        UsTimeZones.All.ShouldContain(UsTimeZones.Default);
    }
}
