using System.Security.Claims;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Models;

/// <summary>Unit tests for <see cref="ImpersonatorInfo.FromClaims"/> — the /api/me impersonator block derivation.</summary>
public class MeResponseTests
{
    [Fact]
    public void FromClaims_WithImpersonationProvenance_ReturnsPopulatedBlock()
    {
        // Arrange
        var impersonatorId = Guid.NewGuid();
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.ImpersonatorEdjeId, impersonatorId.ToString()));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.ImpersonatorName, "Super Admin"));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.ImpersonatorEmail, "admin@leadingedje.com"));

        // Act
        var info = ImpersonatorInfo.FromClaims(new ClaimsPrincipal(identity));

        // Assert
        info.ShouldNotBeNull();
        info!.EdjeId.ShouldBe(impersonatorId);
        info.DisplayName.ShouldBe("Super Admin");
        info.Email.ShouldBe("admin@leadingedje.com");
    }

    [Fact]
    public void FromClaims_WithImpersonatorEdjeIdOnly_DefaultsNameAndEmailToEmpty()
    {
        // Arrange — provenance EdjeId present but name/email claims absent.
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.ImpersonatorEdjeId, Guid.NewGuid().ToString()));

        // Act
        var info = ImpersonatorInfo.FromClaims(new ClaimsPrincipal(identity));

        // Assert
        info.ShouldNotBeNull();
        info!.DisplayName.ShouldBe(string.Empty);
        info.Email.ShouldBe(string.Empty);
    }

    [Fact]
    public void FromClaims_WithoutImpersonationProvenance_ReturnsNull()
    {
        // Arrange — an ordinary (non-impersonating) principal.
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, Guid.NewGuid().ToString()));

        // Act
        var info = ImpersonatorInfo.FromClaims(new ClaimsPrincipal(identity));

        // Assert
        info.ShouldBeNull();
    }
}
