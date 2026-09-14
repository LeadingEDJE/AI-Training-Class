using LeadingEDJE.Leap.Api.Platform.Authorization;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

public class RolePolicyTests
{
    [Fact]
    public void ProcessorOrAdmin_ConstantExists()
    {
        // Arrange & Act & Assert
        RolePolicy.ProcessorOrAdmin.ShouldBe("ProcessorOrAdmin");
    }
}
