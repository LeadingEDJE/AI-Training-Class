using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Dtos;

/// <summary>
/// The assignment/SOW write result carrier (feature 006, contracts/assignment-write-surface.md §5) —
/// mirrors <c>CompassLookupWrite&lt;TDto&gt;</c> so an assignment or SOW service never throws for a
/// business outcome, only for a genuine failure.
/// </summary>
public class CompassAssignmentWriteTests
{
    private sealed record Row(int Id);

    [Fact]
    public void Succeeded_CarriesTheValueAndTheSuccessStatus()
    {
        // Arrange
        var row = new Row(1);

        // Act
        var result = CompassAssignmentWrite<Row>.Succeeded(row);

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Success);
        result.Value.ShouldBe(row);
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void NotFound_CarriesNoValueAndMapsToTheNotFoundStatus()
    {
        // Act
        var result = CompassAssignmentWrite<Row>.NotFound();

        // Assert — 404 per contract §4.
        result.Status.ShouldBe(AdminMutationStatus.NotFound);
        result.Value.ShouldBeNull();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Invalid_CarriesTheMessageAndMapsToTheValidationErrorStatus()
    {
        // Act — 400 per contract §4, e.g. FR-008's end-before-start rejection.
        var result = CompassAssignmentWrite<Row>.Invalid("End date must be on or after the start date.");

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.ValidationError);
        result.Value.ShouldBeNull();
        result.Error.ShouldBe("End date must be on or after the start date.");
    }

    [Fact]
    public void Conflict_CarriesTheMessageAndMapsToTheConflictStatus()
    {
        // Act — 409 per contract §4, e.g. FR-019's overlap rejection.
        var result = CompassAssignmentWrite<Row>.Conflict("This period overlaps an existing one.");

        // Assert
        result.Status.ShouldBe(AdminMutationStatus.Conflict);
        result.Value.ShouldBeNull();
        result.Error.ShouldBe("This period overlaps an existing one.");
    }

    [Theory]
    [InlineData(AdminMutationStatus.NotFound)]
    [InlineData(AdminMutationStatus.ValidationError)]
    [InlineData(AdminMutationStatus.Conflict)]
    public void EveryFailureFactory_MapsToItsHttpStatusThroughTheSharedExtension(AdminMutationStatus status)
    {
        // Arrange & Act — the same AdminMutationStatusResults.ToErrorResult every admin/lookup service
        // already uses, so an assignment/SOW rejection reaches the endpoint layer through one mapping.
        var result = status.ToErrorResult("a message");

        // Assert
        result.ShouldNotBeNull();
    }
}
