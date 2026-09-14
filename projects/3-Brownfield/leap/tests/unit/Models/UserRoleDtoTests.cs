using LeadingEDJE.Leap.Api.Platform.Dtos;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Models;

public class UserRoleDtoTests
{
    [Fact]
    public void UserRoleDto_Properties_RoundTrip()
    {
        // Arrange
        var edjeId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        var dto = new UserRoleDto
        {
            Id = 42,
            EdjeId = edjeId,
            Role = "Manager",
            CreatedAt = createdAt,
            CreatedBy = "admin@leadingedje.com"
        };

        // Act & Assert
        dto.Id.ShouldBe(42);
        dto.EdjeId.ShouldBe(edjeId);
        dto.Role.ShouldBe("Manager");
        dto.CreatedAt.ShouldBe(createdAt);
        dto.CreatedBy.ShouldBe("admin@leadingedje.com");
    }

    [Fact]
    public void UserRoleDto_Defaults_AreCorrect()
    {
        // Act
        var dto = new UserRoleDto();

        // Assert
        dto.Id.ShouldBe(0);
        dto.EdjeId.ShouldBe(Guid.Empty);
        dto.Role.ShouldBe(string.Empty);
        dto.CreatedAt.ShouldBe(default);
        dto.CreatedBy.ShouldBeNull();
    }

    [Fact]
    public void UserRoleRequest_Properties_RoundTrip()
    {
        // Arrange
        var edjeId = Guid.NewGuid();

        var request = new UserRoleRequest
        {
            EdjeId = edjeId,
            Role = "SuperAdmin",
            Reason = "Initial setup"
        };

        // Act & Assert
        request.EdjeId.ShouldBe(edjeId);
        request.Role.ShouldBe("SuperAdmin");
        request.Reason.ShouldBe("Initial setup");
    }

    [Fact]
    public void UserRoleRequest_Defaults_AreCorrect()
    {
        // Act
        var request = new UserRoleRequest();

        // Assert
        request.EdjeId.ShouldBe(Guid.Empty);
        request.Role.ShouldBe(string.Empty);
        request.Reason.ShouldBe(string.Empty);
    }
}
