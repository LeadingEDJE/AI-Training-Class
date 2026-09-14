using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>The mockup's duration rendering (feature 007, T064, FR-030, RPT-4).</summary>
public class AssignmentDurationFormatTests
{
    [Fact]
    public void Describe_TheMockupsOwnExample_MatchesItExactly()
    {
        // FR-030 quotes this string verbatim from mockup RPT-4, so it is asserted verbatim:
        // 1238 / 365 = 3 years, remainder 143 / 30 = 4 months.
        AssignmentDurationFormat.Describe(1238).ShouldBe("3 yrs 4 mos (1,238 days)");
    }

    [Fact]
    public void Describe_ThousandsAreSeparated_Invariantly()
    {
        // InvariantCulture, not the ambient one: a developer machine set to de-DE would otherwise
        // render "1.238" and the assertion above would fail only on their machine.
        AssignmentDurationFormat.Describe(4000).ShouldBe("10 yrs 11 mos (4,000 days)");
    }

    [Fact]
    public void Describe_ExactlyOneYear_IsSingular()
    {
        AssignmentDurationFormat.Describe(365).ShouldBe("1 yr (365 days)");
    }

    [Fact]
    public void Describe_ExactlyOneMonth_IsSingular()
    {
        AssignmentDurationFormat.Describe(30).ShouldBe("1 mo (30 days)");
    }

    [Fact]
    public void Describe_UnderOneMonth_IsJustTheDayCount()
    {
        // No years/months part to lead with, so the bare count stands alone rather than being rendered
        // as an empty prefix with dangling parentheses.
        AssignmentDurationFormat.Describe(9).ShouldBe("9 days");
    }

    [Fact]
    public void Describe_OneDay_IsSingular()
    {
        // The inclusive same-day assignment (FR-015) lands here.
        AssignmentDurationFormat.Describe(1).ShouldBe("1 day");
    }

    [Fact]
    public void Describe_Zero_IsZeroDays()
    {
        AssignmentDurationFormat.Describe(0).ShouldBe("0 days");
    }

    [Fact]
    public void Describe_ANegativeValue_IsTreatedAsZero_NeverRenderedNegative()
    {
        // Defensive rather than reachable: the query excludes not-yet-started assignments, so a
        // negative span cannot arrive today. Pinned so a future caller cannot render "-4 days".
        AssignmentDurationFormat.Describe(-4).ShouldBe("0 days");
    }

    /// <summary>
    /// The 360–364 band, where the months figure would otherwise read as a thirteenth month.
    /// </summary>
    /// <remarks>
    /// The remainder after whole years is 0..364 and 364 / 30 is 12, so the uncapped form rendered
    /// "12 mos (360 days)" and "1 yr 12 mos (729 days)" — each announcing a year the year count did not
    /// claim. Untested until a reviewer pointed at the gap on 2026-08-20: every earlier case
    /// (1238, 4000, 365, 30, 9, 1, 0) happens to miss the band, which is five days in every 365.
    /// </remarks>
    [Theory]
    [InlineData(359, "11 mos (359 days)")]
    [InlineData(360, "11 mos (360 days)")]
    [InlineData(364, "11 mos (364 days)")]
    [InlineData(729, "1 yr 11 mos (729 days)")]
    [InlineData(1094, "2 yrs 11 mos (1,094 days)")]
    public void Describe_NeverRendersATwelfthMonth(int totalDays, string expected)
    {
        AssignmentDurationFormat.Describe(totalDays).ShouldBe(expected);
    }

    [Fact]
    public void Describe_RollsIntoTheNextYearOnlyAtAFullYear()
    {
        // The boundary either side of the capped band: 364 is still under a year, 365 is a year, and
        // 729/730 is the same step one year up. Capping must not make the sequence go backwards.
        AssignmentDurationFormat.Describe(364).ShouldBe("11 mos (364 days)");
        AssignmentDurationFormat.Describe(365).ShouldBe("1 yr (365 days)");
        AssignmentDurationFormat.Describe(729).ShouldBe("1 yr 11 mos (729 days)");
        AssignmentDurationFormat.Describe(730).ShouldBe("2 yrs (730 days)");
    }

    [Fact]
    public void Describe_NeverClaimsAYearItHasNotServed()
    {
        // Why the cap rather than rolling 12 months into a year: 360 days is NOT a year, and
        // "1 yr (360 days)" would be a worse inaccuracy than a month of rounding. Asserted as a
        // property over the whole band rather than by example.
        for (var days = 1; days < 365; days++)
        {
            AssignmentDurationFormat.Describe(days).Contains("yr", StringComparison.Ordinal)
                .ShouldBeFalse($"{days} days is under a year and must not render one");
        }
    }

    [Fact]
    public void Describe_IsOrderedByDayCount_NotLexically()
    {
        // The trap FR-030 names: "10 yrs" sorts BEFORE "3 yrs" as a string, which looks right and is
        // wrong. This documents that the display strings are not orderable, which is why TotalDays is
        // the sort key.
        var ten = AssignmentDurationFormat.Describe(3650);
        var three = AssignmentDurationFormat.Describe(1095);

        string.Compare(ten, three, StringComparison.Ordinal).ShouldBeLessThan(
            0, "lexically '10 yrs' precedes '3 yrs' — never sort on this string");
    }
}
