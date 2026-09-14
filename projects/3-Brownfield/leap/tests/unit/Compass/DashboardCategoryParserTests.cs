using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// Maps the four route segments <c>GET /api/compass/dashboard/breakdown/{category}</c> accepts to
/// <see cref="DashboardCategory"/> — feature 007, contract <c>dashboard-read-surface.md</c>.
/// </summary>
/// <remarks>
/// An unrecognised value MUST 404, never fall back silently (contract: "unlike 005's status filter,
/// which may safely fall back because it can only narrow — here a wrong fallback shows one category's
/// data under another's heading"). This is the parser's whole job: fail closed on anything it does not
/// recognise, exactly.
/// </remarks>
public class DashboardCategoryParserTests
{
    [Theory]
    [InlineData("active-sows", DashboardCategory.ActiveSows)]
    [InlineData("expiring-sows", DashboardCategory.ExpiringSows)]
    [InlineData("confirmed-rollouts", DashboardCategory.ConfirmedRollouts)]
    [InlineData("beach", DashboardCategory.Beach)]
    public void TryParse_TheFourDocumentedSegments_Succeed(string route, DashboardCategory expected)
    {
        // Act
        var parsed = DashboardCategoryParser.TryParse(route, out var category);

        // Assert
        parsed.ShouldBeTrue();
        category.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Beach")]
    [InlineData("ACTIVE-SOWS")]
    [InlineData("expiring-sow")]
    [InlineData("rpt-2")]
    [InlineData("active_sows")]
    public void TryParse_AnythingElse_Fails(string route)
    {
        // Act
        var parsed = DashboardCategoryParser.TryParse(route, out _);

        // Assert — the caller 404s rather than guessing (no silent fallback).
        parsed.ShouldBeFalse();
    }
}
