using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Authorization;

/// <summary>
/// Startup validation of configured Google-group-to-role mappings.
/// </summary>
/// <remarks>
/// A configuration typo must fail the app before it serves traffic. Silently granting nothing is the
/// fail-open shape this project has spent several phases eliminating.
/// </remarks>
public class GoogleAuthGroupValidationTests
{
    private static GoogleAuthOptions LoadRealDevelopmentConfig()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "leap.slnx")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("could not locate the repository root from the test output directory");

        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(dir.FullName, "api", "appsettings.Development.json"), optional: false)
            .Build();

        return config.GetSection("GoogleAuth").Get<GoogleAuthOptions>()!;
    }

    [Fact]
    public void Validate_AcceptsTheRealDevelopmentConfiguration()
    {
        // Arrange — the REAL config, not a fixture. This proves the gate PASSES on live configuration
        // rather than merely existing: a validator that rejected real config would break every
        // environment on arrival, and one that is never exercised against real config proves nothing.
        var options = LoadRealDevelopmentConfig();

        // Act & Assert
        Should.NotThrow(() => GroupRoleMappingValidator.Validate(options));
    }

    [Fact]
    public void Validate_AcceptsAllSixteenConfiguredMappings()
    {
        // Arrange — eight timesheet (OOTO's two removed with its module) + eight Compass (prod +
        // -dev per role).
        var options = LoadRealDevelopmentConfig();

        // Assert
        options.Groups.Count.ShouldBe(16);
        options.Groups.ShouldAllBe(g => KnownRoles.All.Contains(g.Role));
    }

    [Fact]
    public void Validate_ThrowsNamingBothGroupAndRole_WhenRoleIsUnknown()
    {
        // Arrange
        var options = new GoogleAuthOptions
        {
            AllowedDomain = "example.test",
            Groups =
            [
                new GroupRoleMapping { GroupName = "Timesheet-Admin-dev", Role = "HR" },
                new GroupRoleMapping { GroupName = "Some-New-Group-dev", Role = "Totally Made Up" },
            ],
        };

        // Act
        var ex = Should.Throw<InvalidOperationException>(
            () => GroupRoleMappingValidator.Validate(options));

        // Assert — the message must name BOTH, or it is not actionable.
        ex.Message.ShouldContain("Some-New-Group-dev");
        ex.Message.ShouldContain("Totally Made Up");
    }

    [Fact]
    public void Validate_ThrowsWhenACompassGroupMapsToABareTimesheetRole()
    {
        // Arrange — THE escalation that fails open. "Compass-Ops" -> "Ops" would grant the
        // timesheet Ops role. "Ops" IS a known role, so the first check passes; only the
        // cross-module check catches it.
        var options = new GoogleAuthOptions
        {
            AllowedDomain = "example.test",
            Groups = [new GroupRoleMapping { GroupName = "Compass-Ops", Role = "Ops" }],
        };

        // Act
        var ex = Should.Throw<InvalidOperationException>(
            () => GroupRoleMappingValidator.Validate(options));

        // Assert
        ex.Message.ShouldContain("Compass-Ops");
        ex.Message.ShouldContain("Ops");
        ex.Message.ShouldContain("not a Compass role");
    }

    [Fact]
    public void Validate_ThrowsWhenACompassGroupMapsToTheRootTimesheetRole()
    {
        // Arrange — the worst case: a Compass group granting timesheet root.
        var options = new GoogleAuthOptions
        {
            AllowedDomain = "example.test",
            Groups =
            [
                new GroupRoleMapping { GroupName = "Compass-SuperAdmin", Role = "SuperAdmin" },
            ],
        };

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => GroupRoleMappingValidator.Validate(options))
            .Message.ShouldContain("not a Compass role");
    }

    [Fact]
    public void Validate_AllowsANonCompassGroupToKeepItsTimesheetRole()
    {
        // Arrange — the cross-module rule must not over-reach and break existing mappings. OOTO is
        // gone with its module, so a second Timesheet mapping stands in for "another non-Compass
        // role" here.
        var options = new GoogleAuthOptions
        {
            AllowedDomain = "example.test",
            Groups =
            [
                new GroupRoleMapping { GroupName = "Timesheet-Ops-dev", Role = "Ops" },
                new GroupRoleMapping { GroupName = "Timesheet-Admin-dev", Role = "HR" },
            ],
        };

        // Act & Assert
        Should.NotThrow(() => GroupRoleMappingValidator.Validate(options));
    }

    [Fact]
    public void Validate_IgnoresAnEmptyGroupName()
    {
        // Arrange — an unset indexed Helm slot must not be treated as a typo.
        var options = new GoogleAuthOptions
        {
            AllowedDomain = "example.test",
            Groups = [new GroupRoleMapping { GroupName = "", Role = "" }],
        };

        // Act & Assert
        Should.NotThrow(() => GroupRoleMappingValidator.Validate(options));
    }

    [Fact]
    public void Validate_ThrowsOnNullOptions()
    {
        // Assert
        Should.Throw<ArgumentNullException>(() => GroupRoleMappingValidator.Validate(null!));
    }
}
