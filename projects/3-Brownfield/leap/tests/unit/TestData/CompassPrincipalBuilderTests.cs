using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.TestData;

/// <summary>
/// Self-tests for <see cref="CompassPrincipalBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// Why a test helper gets its own tests. US3, US4, US5, US6 and US8 all assert DENIAL through
/// this builder, and an empty role set is denied by every policy. So a builder that silently resolved
/// to nothing would turn every one of those suites green while proving nothing — the same fail-open
/// shape as a path pattern that matches zero files. These tests are what make the denial assertions
/// downstream mean something.
/// </para>
/// <para>
/// The environment cases here are the builder's own contract, not a duplicate of
/// <c>GoogleAuthServiceTests</c>: they prove the builder actually routes through the production mapper
/// instead of stamping the group names it was handed.
/// </para>
/// </remarks>
public class CompassPrincipalBuilderTests
{
    [Fact]
    public void WithRoles_StampsEachRoleAsAPrivilegeClaim()
    {
        // Arrange & Act
        using var principal = CompassPrincipalBuilder.A()
            .WithRoles(RolePolicy.CompassOpsRole, RolePolicy.CompassSalesRole)
            .Build();

        // Assert
        var claims = principal.User
            .FindAll(AuthConstants.ClaimTypes.PrivilegeClaim)
            .Select(c => c.Value)
            .ToList();
        claims.ShouldBe([RolePolicy.CompassOpsRole, RolePolicy.CompassSalesRole], ignoreOrder: true);
        principal.ShouldHoldExactly(RolePolicy.CompassOpsRole, RolePolicy.CompassSalesRole);
    }

    [Fact]
    public void Build_ByDefault_ProducesAnAuthenticatedIdentityWithTheRealAuthenticationType()
    {
        // Arrange & Act
        using var principal = CompassPrincipalBuilder.A()
            .WithRoles(RolePolicy.CompassAdminRole)
            .Build();

        // Assert — the same scheme name the cookie session issues, so a test cannot pass against an
        // identity the real pipeline would never produce.
        principal.IsAuthenticated.ShouldBeTrue();
        principal.User.Identity!.AuthenticationType
            .ShouldBe(AuthConstants.Settings.LeadingEdjeAuthenticationType);
    }

    [Fact]
    public void Build_StampsTheIdentityClaimsTheRealPipelineStamps()
    {
        // Arrange & Act
        using var principal = CompassPrincipalBuilder.A()
            .WithIdentity(email: "nadia@leadingedje.com", displayName: "Nadia")
            .WithRoles(RolePolicy.CompassOpsRole)
            .Build();

        // Assert
        principal.User.FindFirst(AuthConstants.ClaimTypes.EdjeIdClaim)!.Value
            .ShouldBe(CompassPrincipalBuilder.DefaultEdjeId.ToString());
        principal.User.FindFirst(System.Security.Claims.ClaimTypes.Email)!.Value
            .ShouldBe("nadia@leadingedje.com");
        principal.User.FindFirst(AuthConstants.ClaimTypes.DisplayNameClaim)!.Value.ShouldBe("Nadia");
    }

    [Fact]
    public async Task Unauthenticated_HoldsTheClaimsButTheIdentityIsNotAuthenticated()
    {
        // Arrange — deliberately WITH a Compass role, to prove the flag is not a shortcut for
        // "no roles". The role set is intact; only the identity is anonymous.
        using var principal = CompassPrincipalBuilder.A()
            .Unauthenticated()
            .WithRoles(RolePolicy.CompassSuperAdminRole)
            .Build();

        // Assert
        principal.IsAuthenticated.ShouldBeFalse();
        principal.ShouldHoldExactly(RolePolicy.CompassSuperAdminRole);

        // The claims-only resolver reads Privilege claims and does not itself check IsAuthenticated —
        // recorded here so nobody mistakes this builder for the endpoint-level 401. That refusal comes
        // from the authentication middleware, which only T005's integration factory exercises.
        (await principal.SatisfiesAsync(RolePolicy.CompassSuperAdmin)).ShouldBeTrue();
    }

    // ---------------------------------------------------------------- the environment flag

    [Fact]
    public void WithGoogleGroups_InProduction_ResolvesTheBareGroupThroughTheRealMappings()
    {
        // Arrange & Act — reads api/appsettings.Development.json, not a fixture.
        using var principal = CompassPrincipalBuilder.A()
            .WithGoogleGroups("Compass-Ops")
            .InProduction()
            .Build();

        // Assert
        principal.ShouldHoldExactly(RolePolicy.CompassOpsRole);
    }

    [Fact]
    public void WithGoogleGroups_DevGroupInProduction_ResolvesToNothing()
    {
        // Arrange & Act — the failure mode that grants rather than withholds. If the builder stamped
        // the group names it was handed instead of mapping them, this would come back with a role.
        using var principal = CompassPrincipalBuilder.A()
            .WithGoogleGroups("Compass-Ops-dev")
            .InProduction()
            .Build();

        // Assert
        principal.Roles.ShouldBeEmpty();
    }

    [Fact]
    public void WithGoogleGroups_DevGroupInNonProduction_ResolvesToTheRole()
    {
        // Arrange & Act — the mirror, so the previous test cannot pass by resolving nothing ever.
        using var principal = CompassPrincipalBuilder.A()
            .WithGoogleGroups("Compass-Ops-dev")
            .InNonProduction()
            .Build();

        // Assert
        principal.ShouldHoldExactly(RolePolicy.CompassOpsRole);
    }

    [Fact]
    public void WithGoogleGroups_BareGroupInNonProduction_ResolvesToNothing()
    {
        // Arrange & Act — the derivation runs in both directions: a bare name is production-only.
        using var principal = CompassPrincipalBuilder.A()
            .WithGoogleGroups("Compass-Ops")
            .InNonProduction()
            .Build();

        // Assert
        principal.Roles.ShouldBeEmpty();
    }

    [Fact]
    public void WithGoogleGroups_TheIssue53Shape_ResolvesInProductionToOpsAlone()
    {
        // Arrange & Act — the named regression from issue #53, reached through the builder so US4 and
        // US8 can assert the same shape without re-deriving it.
        using var principal = CompassPrincipalBuilder.A()
            .WithGoogleGroups("Compass-Ops", "Compass-SuperAdmin-dev")
            .InProduction()
            .Build();

        // Assert — the dev group's HIGHER-privilege role must not leak alongside a lower-privilege
        // production group.
        principal.ShouldHoldExactly(RolePolicy.CompassOpsRole);
    }

    [Fact]
    public void InProduction_IsTheDefault()
    {
        // Arrange & Act — a test that forgets to state the environment gets the stricter one.
        using var principal = CompassPrincipalBuilder.A()
            .WithGoogleGroups("Compass-Admin")
            .Build();

        // Assert
        principal.ShouldHoldExactly(RolePolicy.CompassAdminRole);
    }

    [Fact]
    public void WithGroupMappings_ReplacesTheRealConfiguration()
    {
        // Arrange & Act — the mechanism, not the shipped config: a hypothetical mapping with no "-dev"
        // suffix is honoured in production...
        using var inProduction = CompassPrincipalBuilder.A()
            .WithGroupMappings(new GroupRoleMapping
            {
                GroupName = "Hypothetical-Compass-Group",
                Role = RolePolicy.CompassSalesRole,
            })
            .WithGoogleGroups("Hypothetical-Compass-Group")
            .InProduction()
            .Build();

        // Assert
        inProduction.ShouldHoldExactly(RolePolicy.CompassSalesRole);

        // Act — ...and nowhere else, purely because it lacks the suffix.
        using var inDev = CompassPrincipalBuilder.A()
            .WithGroupMappings(new GroupRoleMapping
            {
                GroupName = "Hypothetical-Compass-Group",
                Role = RolePolicy.CompassSalesRole,
            })
            .WithGoogleGroups("Hypothetical-Compass-Group")
            .InNonProduction()
            .Build();

        // Assert
        inDev.Roles.ShouldBeEmpty();
    }

    [Fact]
    public void WithRolesAndWithGoogleGroups_UnionWithoutDuplicating()
    {
        // Arrange & Act — an explicit role plus a group that maps to the SAME role.
        using var principal = CompassPrincipalBuilder.A()
            .WithRoles(RolePolicy.CompassOpsRole)
            .WithGoogleGroups("Compass-Ops", "Compass-Sales")
            .InProduction()
            .Build();

        // Assert — "Compass Ops" appears once, not twice.
        principal.ShouldHoldExactly(RolePolicy.CompassOpsRole, RolePolicy.CompassSalesRole);
    }

    // ---------------------------------------------------------------- the wired policies

    [Fact]
    public async Task EachCompassRole_SatisfiesItsOwnPolicy_ThroughTheRealRegistrations()
    {
        // Arrange — the positive control. Without it every denial test below would pass against a
        // builder whose policies denied everything.
        using var admin = CompassPrincipalBuilder.A().WithRoles(RolePolicy.CompassAdminRole).Build();
        using var ops = CompassPrincipalBuilder.A().WithRoles(RolePolicy.CompassOpsRole).Build();
        using var sales = CompassPrincipalBuilder.A().WithRoles(RolePolicy.CompassSalesRole).Build();
        using var root = CompassPrincipalBuilder.A().WithRoles(RolePolicy.CompassSuperAdminRole).Build();

        // Act & Assert
        (await admin.SatisfiesAsync(RolePolicy.CompassAdmin)).ShouldBeTrue();
        (await ops.SatisfiesAsync(RolePolicy.CompassOps)).ShouldBeTrue();
        (await sales.SatisfiesAsync(RolePolicy.CompassSales)).ShouldBeTrue();
        (await root.SatisfiesAsync(RolePolicy.CompassSuperAdmin)).ShouldBeTrue();
    }

    [Fact]
    public async Task ARoleLessPrincipal_IsAuthenticatedAndSatisfiesNoCompassPolicy()
    {
        // Arrange — the implicit-EDJEr shape: authenticated, holding no Compass group.
        using var principal = CompassPrincipalBuilder.A().Build();

        // Assert — authenticated, but the empty set satisfies nothing. This is exactly why
        // ShouldHoldExactly exists: this state is indistinguishable from a misconfigured builder
        // unless the test asserts the role set too.
        principal.IsAuthenticated.ShouldBeTrue();
        principal.Roles.ShouldBeEmpty();
        (await principal.SatisfiesAsync(RolePolicy.CompassAdmin)).ShouldBeFalse();
        (await principal.SatisfiesAsync(RolePolicy.CompassOps)).ShouldBeFalse();
        (await principal.SatisfiesAsync(RolePolicy.CompassSales)).ShouldBeFalse();
        (await principal.SatisfiesAsync(RolePolicy.CompassSuperAdmin)).ShouldBeFalse();
    }

    [Fact]
    public async Task ATimesheetRootPrincipal_SatisfiesNoCompassPolicy()
    {
        // Arrange — the direction that fails OPEN, reachable through the builder so US5 gets it for
        // free rather than re-deriving it.
        using var principal = CompassPrincipalBuilder.A().WithRoles("SuperAdmin").Build();

        // Assert
        principal.ShouldHoldExactly("SuperAdmin");
        (await principal.SatisfiesAsync(RolePolicy.CompassSuperAdmin)).ShouldBeFalse();
        (await principal.SatisfiesAsync(RolePolicy.CompassAdmin)).ShouldBeFalse();
    }

    [Fact]
    public async Task ACompassRootPrincipal_SatisfiesNoTimesheetPolicy()
    {
        // Arrange — and the reverse direction.
        using var principal = CompassPrincipalBuilder.A()
            .WithRoles(RolePolicy.CompassSuperAdminRole)
            .Build();

        // Assert
        (await principal.SatisfiesAsync(RolePolicy.SuperAdmin)).ShouldBeFalse();
        (await principal.SatisfiesAsync(RolePolicy.EDJEr)).ShouldBeFalse();
    }

    [Fact]
    public void DevelopmentSettingsPath_ResolvesToAFileThatExists()
    {
        // Arrange & Act — the group-resolution path is silently useless if this walk fails, and
        // AddJsonFile(optional: false) would throw somewhere less obvious.
        var path = CompassPrincipalBuilder.DevelopmentSettingsPath();

        // Assert
        File.Exists(path).ShouldBeTrue($"expected the dev settings file at {path}");
    }
}
