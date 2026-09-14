using LeadingEDJE.Leap.Api.Platform.Dtos;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Models.Dtos.Admin;

/// <summary>
/// Unit tests for <see cref="AdminMutationStatusResults.ToErrorResult"/>, the shared non-success
/// mapping the admin endpoint groups delegate every failure arm to.
/// </summary>
public class AdminMutationStatusResultsTests
{
    private static int StatusCodeOf(IResult result) =>
        ((IStatusCodeHttpResult)result).StatusCode ?? 0;

    [Fact]
    public void ToErrorResult_NotFound_Maps404()
    {
        // Act
        var result = AdminMutationStatus.NotFound.ToErrorResult("missing");

        // Assert
        StatusCodeOf(result).ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ToErrorResult_Conflict_Maps409()
    {
        // Act
        var result = AdminMutationStatus.Conflict.ToErrorResult("dupe number");

        // Assert
        StatusCodeOf(result).ShouldBe(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void ToErrorResult_EmailConflict_Maps409()
    {
        // Act
        var result = AdminMutationStatus.EmailConflict.ToErrorResult("dupe email");

        // Assert
        StatusCodeOf(result).ShouldBe(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void ToErrorResult_ValidationError_Maps400()
    {
        // Act
        var result = AdminMutationStatus.ValidationError.ToErrorResult("blank name");

        // Assert
        StatusCodeOf(result).ShouldBe(StatusCodes.Status400BadRequest);
    }
}
