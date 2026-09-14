using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Models.Dtos.Compass;

/// <summary>
/// Property round-trip coverage for <see cref="SowExtensionRowDto"/> — issue #534.
/// </summary>
/// <remarks>
/// Mirrors <c>AssignmentStartRowDtoTests</c>: the DTO is exercised end-to-end by the integration suite,
/// but the unit-suite coverage gate (<c>scripts/check-coverage-ci.sh</c>) only ever sees this type as
/// the element type of a repository double's return value, never constructed — so a round-trip test is
/// needed to exercise its accessors at all.
/// </remarks>
public class SowExtensionRowDtoTests
{
    [Fact]
    public void RoundTripsEveryProperty()
    {
        // Arrange
        var startDate = new DateOnly(2026, 3, 15);

        // Act
        var dto = new SowExtensionRowDto
        {
            EmployeeName = "Maya Alvarez",
            EmployeeType = "Full Time",
            ClientName = "Buckeye Mutual",
            ExtensionStartDate = startDate,
        };

        // Assert
        dto.EmployeeName.ShouldBe("Maya Alvarez");
        dto.EmployeeType.ShouldBe("Full Time");
        dto.ClientName.ShouldBe("Buckeye Mutual");
        dto.ExtensionStartDate.ShouldBe(startDate);
    }

    [Fact]
    public void EmployeeTypeIsNullByDefault()
    {
        // Arrange & Act -- the FK backing employee type is required in practice, but the wire type
        // stays nullable, matching AssignmentStartRowDto and AssignmentDurationRowDto.
        var dto = new SowExtensionRowDto { ExtensionStartDate = new DateOnly(2026, 1, 1) };

        // Assert
        dto.EmployeeType.ShouldBeNull();
    }

    [Fact]
    public void DefaultsTheNamesToEmpty_NotNull()
    {
        // Arrange & Act
        var dto = new SowExtensionRowDto { ExtensionStartDate = new DateOnly(2026, 1, 1) };

        // Assert -- an unset name must render as an empty cell, never the word "null".
        dto.EmployeeName.ShouldBe(string.Empty);
        dto.ClientName.ShouldBe(string.Empty);
    }

    [Fact]
    public void CarriesADateOnly_SoThereIsNoTimeComponentToMisinterpret()
    {
        // Arrange & Act -- an extension SOW starts on a DAY, matching Sow.SowStartDate.
        var dto = new SowExtensionRowDto { ExtensionStartDate = new DateOnly(2026, 12, 31) };

        // Assert
        dto.ExtensionStartDate.Year.ShouldBe(2026);
        dto.ExtensionStartDate.Month.ShouldBe(12);
        dto.ExtensionStartDate.Day.ShouldBe(31);
    }
}
