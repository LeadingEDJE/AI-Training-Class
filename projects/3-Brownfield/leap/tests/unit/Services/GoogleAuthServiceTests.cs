using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.Extensions.Options;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Unit tests for the pure Google SAML mapping core: email-domain validation and the
/// Google-group-display-name → timesheet-role translation. Mirrors TPS's GoogleAuthServiceTests
/// with the timesheet dev group table (Timesheet-*-dev → RolePolicy constants).
/// </summary>
public class GoogleAuthServiceTests
{
    private const string AllowedDomain = "leadingedje.com";

    private static GoogleAuthService CreateService(string allowedDomain = AllowedDomain)
    {
        var options = new GoogleAuthOptions
        {
            AllowedDomain = allowedDomain,
            Groups =
            [
                new GroupRoleMapping { GroupName = "Timesheet-SuperAdmin-dev", Role = RolePolicy.SuperAdmin },
                new GroupRoleMapping { GroupName = "Timesheet-Admin-dev", Role = RolePolicy.HR },
                new GroupRoleMapping { GroupName = "Timesheet-Config-dev", Role = RolePolicy.Admin },
                new GroupRoleMapping { GroupName = "Timesheet-Approval-dev", Role = RolePolicy.Manager },
                new GroupRoleMapping { GroupName = "Timesheet-Invoicing-dev", Role = RolePolicy.TimesheetProcessor },
                new GroupRoleMapping { GroupName = "Timesheet-Payroll-dev", Role = RolePolicy.PayrollProcessor },
                new GroupRoleMapping { GroupName = "Timesheet-Accounting-dev", Role = RolePolicy.Accounting },
                new GroupRoleMapping { GroupName = "Timesheet-Ops-dev", Role = RolePolicy.Ops },
            ],
        };
        return new GoogleAuthService(Options.Create(options));
    }

    // Mirrors the real deployed Compass mapping table (deploy/helm/leap/values.yaml): each role is
    // registered under BOTH its bare production group name and its "-dev" counterpart, exactly the
    // shape that was unconditionally unioned before this fix.
    private static GoogleAuthService CreateCompassService()
    {
        var options = new GoogleAuthOptions
        {
            AllowedDomain = AllowedDomain,
            Groups =
            [
                new GroupRoleMapping { GroupName = "Compass-SuperAdmin", Role = RolePolicy.CompassSuperAdminRole },
                new GroupRoleMapping { GroupName = "Compass-SuperAdmin-dev", Role = RolePolicy.CompassSuperAdminRole },
                new GroupRoleMapping { GroupName = "Compass-Admin", Role = RolePolicy.CompassAdminRole },
                new GroupRoleMapping { GroupName = "Compass-Admin-dev", Role = RolePolicy.CompassAdminRole },
                new GroupRoleMapping { GroupName = "Compass-Ops", Role = RolePolicy.CompassOpsRole },
                new GroupRoleMapping { GroupName = "Compass-Ops-dev", Role = RolePolicy.CompassOpsRole },
                new GroupRoleMapping { GroupName = "Compass-Sales", Role = RolePolicy.CompassSalesRole },
                new GroupRoleMapping { GroupName = "Compass-Sales-dev", Role = RolePolicy.CompassSalesRole },
            ],
        };
        return new GoogleAuthService(Options.Create(options));
    }

    // -- ValidateDomain --

    [Fact]
    public void ValidateDomain_ExactLeadingEdjeEmail_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.ValidateDomain("someone@leadingedje.com");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void ValidateDomain_UppercaseDomain_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.ValidateDomain("Someone@LEADINGEDJE.COM");

        // Assert
        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData("someone@gmail.com")]
    [InlineData("someone@notleadingedje.com")]
    [InlineData("user@evil-leadingedje.com")]
    [InlineData("someone@leadingedje.com.evil.com")]
    [InlineData("user@x.leadingedje.com")]
    [InlineData("no-at-sign")]
    [InlineData("trailing@")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateDomain_InvalidForeignOrSpoofedEmail_ReturnsFalse(string? email)
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.ValidateDomain(email!);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void ValidateDomain_EmptyAllowedDomainConfig_RejectsEveryEmail()
    {
        // Arrange
        var service = CreateService(allowedDomain: "");

        // Act
        var result = service.ValidateDomain("someone@leadingedje.com");

        // Assert
        result.ShouldBeFalse();
    }

    // -- MapGroupsToRoles --

    [Fact]
    public void MapGroupsToRoles_KnownSingleGroup_ReturnsMappedRole()
    {
        // Arrange
        var service = CreateService();

        // Act
        var roles = service.MapGroupsToRoles(["Timesheet-SuperAdmin-dev"], isProduction: false);

        // Assert
        roles.ShouldHaveSingleItem().ShouldBe(RolePolicy.SuperAdmin);
    }

    [Fact]
    public void MapGroupsToRoles_UnknownGroup_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();

        // Act
        var roles = service.MapGroupsToRoles(["Some-Other-Group"], isProduction: false);

        // Assert
        roles.ShouldBeEmpty();
    }

    [Fact]
    public void MapGroupsToRoles_MultipleGroups_ReturnsUnionOfRoles()
    {
        // Arrange
        var service = CreateService();

        // Act
        var roles = service.MapGroupsToRoles(["Timesheet-Approval-dev", "Timesheet-Ops-dev"], isProduction: false);

        // Assert
        roles.ShouldBe([RolePolicy.Manager, RolePolicy.Ops], ignoreOrder: true);
    }

    [Fact]
    public void MapGroupsToRoles_MixedKnownAndUnknownGroups_ReturnsOnlyMapped()
    {
        // Arrange
        var service = CreateService();

        // Act
        var roles = service.MapGroupsToRoles(["Timesheet-Accounting-dev", "Unknown-Group"], isProduction: false);

        // Assert
        roles.ShouldHaveSingleItem().ShouldBe(RolePolicy.Accounting);
    }

    [Fact]
    public void MapGroupsToRoles_GroupValueCaseInsensitive_MatchesMapping()
    {
        // Arrange
        var service = CreateService();

        // Act
        var roles = service.MapGroupsToRoles(["timesheet-superadmin-DEV"], isProduction: false);

        // Assert
        roles.ShouldHaveSingleItem().ShouldBe(RolePolicy.SuperAdmin);
    }

    [Fact]
    public void MapGroupsToRoles_AdminAndConfigGroupsBothPresent_ReturnsHrAndAdmin()
    {
        // Arrange
        var service = CreateService();

        // Act — authoritative org mapping: Timesheet-Admin-dev is the HR-role group
        // (NOT an Admin alias); Timesheet-Config-dev is the Admin-role group.
        var roles = service.MapGroupsToRoles(["Timesheet-Admin-dev", "Timesheet-Config-dev"], isProduction: false);

        // Assert
        roles.ShouldBe([RolePolicy.HR, RolePolicy.Admin], ignoreOrder: true);
    }

    [Fact]
    public void MapGroupsToRoles_DuplicateGroupValues_DoesNotDuplicateRole()
    {
        // Arrange
        var service = CreateService();

        // Act
        var roles = service.MapGroupsToRoles(["Timesheet-Ops-dev", "Timesheet-Ops-dev"], isProduction: false);

        // Assert
        roles.ShouldHaveSingleItem().ShouldBe(RolePolicy.Ops);
    }

    [Fact]
    public void MapGroupsToRoles_NullInput_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();

        // Act
        var roles = service.MapGroupsToRoles(null!, isProduction: false);

        // Assert
        roles.ShouldBeEmpty();
    }

    [Fact]
    public void MapGroupsToRoles_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();

        // Act
        var roles = service.MapGroupsToRoles([], isProduction: false);

        // Assert
        roles.ShouldBeEmpty();
    }

    [Fact]
    public void MapGroupsToRoles_WhitespaceAndBlankEntries_AreIgnored()
    {
        // Arrange
        var service = CreateService();

        // Act
        var roles = service.MapGroupsToRoles(["", "   ", null!, "Timesheet-Payroll-dev"], isProduction: false);

        // Assert
        roles.ShouldHaveSingleItem().ShouldBe(RolePolicy.PayrollProcessor);
    }

    [Fact]
    public void MapGroupsToRoles_ReturnedRoleStrings_MatchRolePolicyConstantsExactly()
    {
        // Arrange
        var service = CreateService();

        // Act
        var roles = service.MapGroupsToRoles(
        [
            "Timesheet-SuperAdmin-dev",
            "Timesheet-Admin-dev",
            "Timesheet-Config-dev",
            "Timesheet-Approval-dev",
            "Timesheet-Invoicing-dev",
            "Timesheet-Payroll-dev",
            "Timesheet-Accounting-dev",
            "Timesheet-Ops-dev",
        ],
        isProduction: false);

        // Assert
        roles.ShouldBe(
            [
                RolePolicy.SuperAdmin,
                RolePolicy.HR,
                RolePolicy.Admin,
                RolePolicy.Manager,
                RolePolicy.TimesheetProcessor,
                RolePolicy.PayrollProcessor,
                RolePolicy.Accounting,
                RolePolicy.Ops,
            ],
            ignoreOrder: true);
    }

    // -- MapGroupsToRoles: environment-scoped resolution (issue #53) --

    // Named regression from issue #53: a user holding both a real production group and a
    // dev-only group for a DIFFERENT (higher-privilege) role must resolve, in Production, to the
    // production group's role alone. The dev group must never leak a role in Production, even
    // alongside a lower-privilege production group held by the same user.
    [Fact]
    public void MapGroupsToRoles_ProdAndDevGroupsBothPresentInProduction_ReturnsProdRoleOnlyNotDevRole()
    {
        // Arrange
        var service = CreateCompassService();

        // Act
        var roles = service.MapGroupsToRoles(["Compass-Ops", "Compass-SuperAdmin-dev"], isProduction: true);

        // Assert
        roles.ShouldHaveSingleItem().ShouldBe(RolePolicy.CompassOpsRole);
    }

    // Mirror of the above in non-production: the bare production group name is out of scope there,
    // so only the dev group's role survives.
    [Fact]
    public void MapGroupsToRoles_ProdAndDevGroupsBothPresentInNonProduction_ReturnsDevRoleOnlyNotProdRole()
    {
        // Arrange
        var service = CreateCompassService();

        // Act
        var roles = service.MapGroupsToRoles(["Compass-Ops", "Compass-SuperAdmin-dev"], isProduction: false);

        // Assert
        roles.ShouldHaveSingleItem().ShouldBe(RolePolicy.CompassSuperAdminRole);
    }

    // Proves matching is full-string, never substring/prefix: "Compass-Admin" is a literal
    // substring of "Compass-Admin-dev", so a substring match would incorrectly honor the dev
    // group's role here. It must not — the dev-suffixed mapping is also out of scope in Production,
    // so no role should be granted at all.
    [Fact]
    public void MapGroupsToRoles_OnlyDevGroupAssertedInProduction_ReturnsEmpty_NotSubstringMatched()
    {
        // Arrange
        var service = CreateCompassService();

        // Act
        var roles = service.MapGroupsToRoles(["Compass-Admin-dev"], isProduction: true);

        // Assert
        roles.ShouldBeEmpty();
    }

    [Fact]
    public void MapGroupsToRoles_ProdGroupWithSurroundingWhitespace_IsTrimmedAndMatched()
    {
        // Arrange
        var service = CreateCompassService();

        // Act
        var roles = service.MapGroupsToRoles([" Compass-Admin "], isProduction: true);

        // Assert
        roles.ShouldHaveSingleItem().ShouldBe(RolePolicy.CompassAdminRole);
    }

    // -- MapGroupsToRoles: isolation from unrelated groups (US3/#52, spec 002 T025) --

    // A user's Compass authority must derive from the four Compass groups alone (FR-010, FR-011).
    // Mixing in a large set of unrelated groups -- including the nine bare Timesheet role strings
    // used AS IF they were group names -- must contribute nothing, because none of them is a
    // configured mapping for this service. RolePolicy constants are role names, not Google group
    // identifiers, and this guards against ever conflating the two.
    [Fact]
    public void MapGroupsToRoles_CompassGroupsPlusManyUnrelatedGroups_ResolvesToCompassRolesAlone()
    {
        // Arrange
        var service = CreateCompassService();

        // Act
        var roles = service.MapGroupsToRoles(
        [
            "Compass-Ops",
            "Compass-Sales",
            // The nine bare Timesheet role strings, used as if they were group names. None is a
            // mapping configured on this service, so none should resolve to anything.
            RolePolicy.EDJEr,
            RolePolicy.Manager,
            RolePolicy.TimesheetProcessor,
            RolePolicy.Accounting,
            RolePolicy.HR,
            RolePolicy.Ops,
            RolePolicy.PayrollProcessor,
            RolePolicy.Admin,
            RolePolicy.SuperAdmin,
            // Ordinary unrelated Google groups with no configured mapping at all.
            "All-Employees",
            "Finance-Team",
            "Random-Slack-Integration-Group",
        ],
        isProduction: true);

        // Assert
        roles.ShouldBe([RolePolicy.CompassOpsRole, RolePolicy.CompassSalesRole], ignoreOrder: true);
    }
}
