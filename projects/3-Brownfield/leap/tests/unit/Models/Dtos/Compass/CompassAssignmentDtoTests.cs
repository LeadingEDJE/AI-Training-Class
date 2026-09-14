using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Models.Dtos.Compass;

/// <summary>
/// Property round-trip coverage for the Compass assignment write-surface DTOs, in the shape
/// <c>AdminDtoInstantiationTests</c> established for the absorbed-directory admin DTOs.
/// </summary>
/// <remarks>
/// <para>
/// Why these need their own unit tests at all. Both files are exercised end-to-end by the
/// endpoint and integration suites, but <c>scripts/check-coverage.sh</c> reads a Cobertura report
/// produced from the UNIT suite alone, where neither file was attributed a single covered line — so
/// a change to either surfaced as "No test coverage found" and failed the new-code gate. The answer
/// the repository already uses for that shape is a round-trip test, not a waiver entry.
/// </para>
/// <para>
/// The invoice-frequency members are the point (US6, #64). <c>InvoiceFrequencyTypeId</c> is
/// the assignment's own override and <c>EffectiveInvoiceFrequency</c> is what actually applies after
/// the override/client-default precedence resolves — including the case where BOTH are unset, which
/// is a legitimate "none set" rather than an error or a substituted house default (FR-037, FR-039).
/// Asserting null survives the round trip is what stops a later "helpful" default being introduced
/// at the DTO layer, where the precedence rule could not see it.
/// </para>
/// </remarks>
public class CompassAssignmentDtoTests
{
    [Fact]
    public void AssignmentRowDto_RoundTripsEveryProperty()
    {
        // Arrange
        var start = new DateOnly(2026, 4, 1);
        var end = new DateOnly(2026, 9, 30);

        // Act
        var dto = new AssignmentRowDto
        {
            Id = 3,
            EmployeeId = 9,
            EmployeeName = "Maya Alvarez",
            ClientId = 1,
            ClientName = "Buckeye Mutual",
            StartDate = start,
            EndDate = end,
            IsCurrent = true,
            Note = "Renewed through Q3",
            InvoiceFrequencyTypeId = 7,
            EffectiveInvoiceFrequency = "Weekly",
        };

        // Assert
        dto.Id.ShouldBe(3);
        dto.EmployeeId.ShouldBe(9);
        dto.EmployeeName.ShouldBe("Maya Alvarez");
        dto.ClientId.ShouldBe(1);
        dto.ClientName.ShouldBe("Buckeye Mutual");
        dto.StartDate.ShouldBe(start);
        dto.EndDate.ShouldBe(end);
        dto.IsCurrent.ShouldBeTrue();
        dto.Note.ShouldBe("Renewed through Q3");
        dto.InvoiceFrequencyTypeId.ShouldBe(7);
        dto.EffectiveInvoiceFrequency.ShouldBe("Weekly");
    }

    [Fact]
    public void AssignmentRowDto_DefaultsEveryOptionalMemberToNull()
    {
        // Arrange & Act
        var dto = new AssignmentRowDto { Id = 4 };

        // Assert -- "none set" is a real state and must not be filled in for the caller (FR-039).
        dto.EndDate.ShouldBeNull();
        dto.Note.ShouldBeNull();
        dto.InvoiceFrequencyTypeId.ShouldBeNull();
        dto.EffectiveInvoiceFrequency.ShouldBeNull();
        dto.EmployeeName.ShouldBe(string.Empty);
        dto.ClientName.ShouldBe(string.Empty);
        dto.IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public void CreateAssignmentRequest_RoundTripsEveryProperty()
    {
        // Arrange
        var start = new DateOnly(2026, 4, 1);
        var end = new DateOnly(2026, 12, 31);

        // Act
        var request = new CreateAssignmentRequest(9, 1, start, end, "Kickoff", 7);

        // Assert
        request.EmployeeId.ShouldBe(9);
        request.ClientId.ShouldBe(1);
        request.StartDate.ShouldBe(start);
        request.EndDate.ShouldBe(end);
        request.Note.ShouldBe("Kickoff");
        request.InvoiceFrequencyTypeId.ShouldBe(7);
    }

    [Fact]
    public void CreateAssignmentRequest_OmittingTheOverride_MeansNoOverride()
    {
        // Arrange & Act -- the optional parameter is what keeps every pre-US6 call site compiling.
        var request = new CreateAssignmentRequest(9, 1, new DateOnly(2026, 4, 1), null, null);

        // Assert
        request.InvoiceFrequencyTypeId.ShouldBeNull();
        request.EndDate.ShouldBeNull();
        request.Note.ShouldBeNull();
    }

    [Fact]
    public void UpdateAssignmentRequest_RoundTripsEveryProperty()
    {
        // Arrange
        var start = new DateOnly(2026, 4, 1);

        // Act
        var request = new UpdateAssignmentRequest(start, null, "Adjusted", 7);

        // Assert
        request.StartDate.ShouldBe(start);
        request.EndDate.ShouldBeNull();
        request.Note.ShouldBe("Adjusted");
        request.InvoiceFrequencyTypeId.ShouldBe(7);
    }

    [Fact]
    public void UpdateAssignmentRequest_OmittingTheOverride_MeansNoOverride()
    {
        // Arrange & Act
        var request = new UpdateAssignmentRequest(new DateOnly(2026, 4, 1), null, null);

        // Assert
        request.InvoiceFrequencyTypeId.ShouldBeNull();
    }

    [Fact]
    public void AssignmentWriteRequests_CompareByValue()
    {
        // Arrange
        var start = new DateOnly(2026, 4, 1);

        // Act
        var withOverride = new UpdateAssignmentRequest(start, null, "Adjusted", 7);
        var same = new UpdateAssignmentRequest(start, null, "Adjusted", 7);
        var cleared = withOverride with { InvoiceFrequencyTypeId = null };

        // Assert -- record equality is what makes "did the override change?" a value comparison in
        // the service's update path rather than a hand-written field-by-field check.
        same.ShouldBe(withOverride);
        cleared.ShouldNotBe(withOverride);
        cleared.InvoiceFrequencyTypeId.ShouldBeNull();
        cleared.Note.ShouldBe("Adjusted");
        withOverride.ToString().ShouldContain("InvoiceFrequencyTypeId");
        withOverride.GetHashCode().ShouldBe(same.GetHashCode());
    }
}
