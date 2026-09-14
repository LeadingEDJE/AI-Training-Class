using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The single shared rule for building a person's display name from a Compass
/// <see cref="Employee"/>'s first and last name.
/// </summary>
/// <remarks>
/// Extracted from <c>CompassDirectoryService.BuildDisplayName</c> once a second and third call site
/// (<c>CompassAssignmentService</c>, <c>CompassEmployeeService</c>) turned out to build the exact
/// same string by hand, each without the blank-part filter the original had — the Rule of Three this
/// project's own conventions call for. Reused only where an <see cref="Employee"/> is already
/// materialized in memory; the several EF LINQ projections that build the same string inline
/// (<c>CompassReportRepository</c>, <c>CompassDashboardRepository</c>, ...) must keep the raw
/// <c>a + " " + b</c> form, because Npgsql translates that to SQL and would not translate a call
/// into this helper.
/// </remarks>
public class CompassDisplayNameTests
{
    private static Employee BuildEmployee(string first, string last) => new()
    {
        FirstName = first,
        LastName = last,
        Email = "person@example.test",
        HireDate = new DateOnly(2020, 1, 1),
        EmployeeTypeId = 1,
        StateOfResidence = "OH",
    };

    [Fact]
    public void For_JoinsFirstAndLastName()
    {
        // Arrange
        var employee = BuildEmployee("Ada", "Lovelace");

        // Act
        var displayName = CompassDisplayName.For(employee);

        // Assert
        displayName.ShouldBe("Ada Lovelace");
    }

    [Fact]
    public void For_WhenLastNameIsBlank_OmitsTheTrailingSeparator()
    {
        // Arrange -- the columns are non-nullable, not non-empty; a blank surname must not leave a
        // dangling trailing space.
        var employee = BuildEmployee("Solo", string.Empty);

        // Act
        var displayName = CompassDisplayName.For(employee);

        // Assert
        displayName.ShouldBe("Solo");
    }

    [Fact]
    public void For_WhenFirstNameIsBlank_OmitsTheLeadingSeparator()
    {
        // Arrange
        var employee = BuildEmployee(string.Empty, "Placeholder");

        // Act
        var displayName = CompassDisplayName.For(employee);

        // Assert
        displayName.ShouldBe("Placeholder");
    }
}
