using LeadingEDJE.Leap.Api.Platform.Authorization;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Authorization;

/// <summary>
/// Pins the platform's role vocabulary — the set startup validation checks configured group mappings
/// against.
/// </summary>
public class KnownRolesTests
{
    [Fact]
    public void Timesheet_ContainsExactlyTheNineHistoricalRoles()
    {
        // Assert — these nine are frozen; they are timesheet's by history.
        KnownRoles.Timesheet.Count.ShouldBe(9);
        KnownRoles.Timesheet.ShouldContain("EDJEr");
        KnownRoles.Timesheet.ShouldContain("Manager");
        KnownRoles.Timesheet.ShouldContain("TimesheetProcessor");
        KnownRoles.Timesheet.ShouldContain("Accounting");
        KnownRoles.Timesheet.ShouldContain("HR");
        KnownRoles.Timesheet.ShouldContain("Ops");
        KnownRoles.Timesheet.ShouldContain("PayrollProcessor");
        KnownRoles.Timesheet.ShouldContain("Admin");
        KnownRoles.Timesheet.ShouldContain("SuperAdmin");
    }

    [Fact]
    public void Compass_ContainsExactlyTheFourRoleStrings_WithTheirSpaces()
    {
        // Assert
        KnownRoles.Compass.Count.ShouldBe(4);
        KnownRoles.Compass.ShouldContain("Compass Super Admin");
        KnownRoles.Compass.ShouldContain("Compass Admin");
        KnownRoles.Compass.ShouldContain("Compass Ops");
        KnownRoles.Compass.ShouldContain("Compass Sales");
    }

    [Fact]
    public void All_IsTheUnionOfTheModuleSets()
    {
        // Assert — 9 + 4 (Ooto's two roles retired with the module), with no accidental overlap
        // collapsing the count.
        KnownRoles.All.Count.ShouldBe(13);
    }

    [Fact]
    public void IsKnown_RejectsAnUnrecognisedRoleString()
    {
        // Assert
        KnownRoles.IsKnown("Compass Admin").ShouldBeTrue();
        KnownRoles.IsKnown("Totally Made Up Role").ShouldBeFalse();
        KnownRoles.IsKnown("").ShouldBeFalse();
    }

    [Fact]
    public void IsKnown_MatchesCaseInsensitively_BecauseTheGroupMapperDoes()
    {
        // Assert — a casing-only difference is not a difference to the group-to-role mapper, so the
        // known-role check must agree, otherwise a casing variant could slip past validation.
        KnownRoles.IsKnown("compass admin").ShouldBeTrue();
        KnownRoles.IsKnown("SUPERADMIN").ShouldBeTrue();
    }
}
