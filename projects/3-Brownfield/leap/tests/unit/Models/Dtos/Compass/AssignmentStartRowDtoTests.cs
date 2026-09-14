using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Models.Dtos.Compass;

/// <summary>
/// Property round-trip coverage for <see cref="AssignmentStartRowDto"/> — feature 007 US4, AC-40,
/// issue #78.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists. The DTO is exercised end-to-end by the integration suite, but
/// <c>scripts/check-coverage-ci.sh</c> reads a Cobertura report produced from the UNIT suite alone.
/// There the type is only ever referenced — as the element type of the repository doubles'
/// return values — and never constructed, so not one of its accessors runs and a new-code
/// coverage gate sees nothing. The answer this repository already uses for that shape is a round-trip
/// test, not a waiver entry (see <c>CompassAssignmentDtoTests</c>).
/// </para>
/// <para>
/// The string defaults are the part worth pinning: a row whose names came back <c>null</c> would put
/// "null" through a table cell rather than an empty one, and the defaults are what stop that.
/// </para>
/// </remarks>
public class AssignmentStartRowDtoTests
{
    [Fact]
    public void RoundTripsEveryProperty()
    {
        // Arrange
        var startDate = new DateOnly(2026, 3, 15);

        // Act
        var dto = new AssignmentStartRowDto
        {
            EmployeeName = "Maya Alvarez",
            EmployeeType = "Full Time",
            ClientName = "Buckeye Mutual",
            StartDate = startDate,
        };

        // Assert
        dto.EmployeeName.ShouldBe("Maya Alvarez");
        dto.EmployeeType.ShouldBe("Full Time");
        dto.ClientName.ShouldBe("Buckeye Mutual");
        dto.StartDate.ShouldBe(startDate);
    }

    [Fact]
    public void EmployeeTypeIsNullByDefault()
    {
        // Arrange & Act -- the FK backing employee type is required in practice, but the wire type stays
        // nullable (issue #386), matching ConfirmedRolloutRowDto and AssignmentDurationRowDto.
        var dto = new AssignmentStartRowDto { StartDate = new DateOnly(2026, 1, 1) };

        // Assert
        dto.EmployeeType.ShouldBeNull();
    }

    [Fact]
    public void DefaultsTheNamesToEmpty_NotNull()
    {
        // Arrange & Act
        var dto = new AssignmentStartRowDto { StartDate = new DateOnly(2026, 1, 1) };

        // Assert -- an unset name must render as an empty cell, never the word "null".
        dto.EmployeeName.ShouldBe(string.Empty);
        dto.ClientName.ShouldBe(string.Empty);
    }

    [Fact]
    public void CarriesADateOnly_SoThereIsNoTimeComponentToMisinterpret()
    {
        // Arrange & Act -- an assignment starts on a DAY. A DateTime here would invite a timezone
        // question the business rule does not have, and this row is rendered in America/New_York while
        // the value is stored as a native Postgres `date`.
        var dto = new AssignmentStartRowDto { StartDate = new DateOnly(2026, 12, 31) };

        // Assert
        dto.StartDate.Year.ShouldBe(2026);
        dto.StartDate.Month.ShouldBe(12);
        dto.StartDate.Day.ShouldBe(31);
    }
}
