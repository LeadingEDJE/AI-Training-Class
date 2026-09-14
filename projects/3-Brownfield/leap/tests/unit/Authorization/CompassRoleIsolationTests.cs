using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Tests.TestData;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Authorization;

/// <summary>
/// Asserts cross-module authorization isolation in BOTH directions, as a full denial matrix.
/// </summary>
/// <remarks>
/// <para>
/// This is the criterion that fails OPEN, so it asserts DENIAL rather than access. Every module
/// gets its own roles: a timesheet SuperAdmin is NOT a Compass root, and a Compass role grants nothing
/// in timesheet.
/// </para>
/// <para>
/// The policies are driven through the REAL registered authorization service rather than
/// re-implementing the rule, so this breaks if someone later widens a Compass policy — which is the
/// whole point. A matrix that re-states the implementation would agree with any bug.
/// </para>
/// <para>
/// The principal and the wired policy set come from <see cref="CompassPrincipalBuilder"/>, which is the
/// single copy of that wiring in the test tree. It registers the 18 timesheet policies (frozen
/// vocabulary, kept after the Ooto module retirement removed its own two) as a hand-maintained mirror
/// of the inline registrations in <c>api/Program.cs</c> — see its remarks for why that mirror can
/// drift — and calls the Compass module's own <c>AddCompassAuthorization()</c>.
/// </para>
/// </remarks>
public class CompassRoleIsolationTests
{
    // The nine timesheet role strings — every non-Compass role.
    private static readonly string[] NonCompassRoles =
    [
        "EDJEr", "Manager", "TimesheetProcessor", "Accounting", "HR", "Ops",
        "PayrollProcessor", "Admin", "SuperAdmin",
    ];

    private static readonly string[] CompassRoles =
    [
        RolePolicy.CompassSuperAdminRole,
        RolePolicy.CompassAdminRole,
        RolePolicy.CompassOpsRole,
        RolePolicy.CompassSalesRole,
    ];

    private static readonly string[] CompassPolicies =
    [
        RolePolicy.CompassSuperAdmin,
        RolePolicy.CompassAdmin,
        RolePolicy.CompassOps,
        RolePolicy.CompassSales,
        RolePolicy.CompassReporting,
    ];

    // Every timesheet policy registered by the composition root (9 single-role + 9 compound = 18).
    // Compass must satisfy none of them.
    private static readonly string[] NonCompassPolicies =
    [
        RolePolicy.EDJEr, RolePolicy.Manager, RolePolicy.TimesheetProcessor, RolePolicy.Accounting,
        RolePolicy.HR, RolePolicy.Ops, RolePolicy.PayrollProcessor, RolePolicy.Admin,
        RolePolicy.SuperAdmin,
        RolePolicy.ManagerOrProcessor, RolePolicy.HROrSuperAdmin, RolePolicy.ProcessorOrAdmin,
        RolePolicy.OpsOrSuperAdmin, RolePolicy.AccountingOrSuperAdmin,
        RolePolicy.PayrollProcessorOrSuperAdmin, RolePolicy.InvoiceAccess, RolePolicy.BalanceAccess,
        RolePolicy.ReportDownload,
    ];

    [Fact]
    public async Task EveryCompassRole_SatisfiesNoTimesheetOrOotoPolicy()
    {
        // Arrange
        var failures = new List<string>();

        // Act — 4 Compass roles x 18 non-Compass policies.
        foreach (var role in CompassRoles)
        {
            using var principal = CompassPrincipalBuilder.A().WithRoles(role).Build();
            foreach (var policy in NonCompassPolicies)
            {
                if (await principal.SatisfiesAsync(policy))
                {
                    failures.Add($"'{role}' SATISFIED non-Compass policy '{policy}'");
                }
            }
        }

        // Assert
        failures.ShouldBeEmpty(
            "a Compass role must grant NOTHING in timesheet:\n" + string.Join("\n", failures));
    }

    [Fact]
    public async Task EveryTimesheetRole_SatisfiesNoCompassPolicy()
    {
        // Arrange — this is the direction that includes the root role. A timesheet SuperAdmin is
        // deliberately NOT a Compass root.
        var failures = new List<string>();

        // Act — 9 non-Compass roles x 4 Compass policies.
        foreach (var role in NonCompassRoles)
        {
            using var principal = CompassPrincipalBuilder.A().WithRoles(role).Build();
            foreach (var policy in CompassPolicies)
            {
                if (await principal.SatisfiesAsync(policy))
                {
                    failures.Add($"'{role}' SATISFIED Compass policy '{policy}'");
                }
            }
        }

        // Assert
        failures.ShouldBeEmpty(
            "Compass inherits nothing, not even root:\n" + string.Join("\n", failures));
    }

    [Fact]
    public async Task EachCompassRole_SatisfiesItsOwnPolicy()
    {
        // Arrange — the positive control. Without it, a policy set that denies EVERYTHING would pass
        // both denial matrices above and prove nothing.
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
    public async Task CompassRoot_SatisfiesTheOtherCompassPolicies_ButStillNothingOutsideCompass()
    {
        // Arrange — the Compass root is Compass's own root: internal reach, zero external reach.
        using var principal = CompassPrincipalBuilder.A()
            .WithRoles(RolePolicy.CompassSuperAdminRole)
            .Build();

        // Act & Assert — internal reach.
        foreach (var policy in CompassPolicies)
        {
            (await principal.SatisfiesAsync(policy)).ShouldBeTrue(
                $"the Compass root must satisfy the Compass policy '{policy}'");
        }

        // Act & Assert — zero external reach.
        foreach (var policy in NonCompassPolicies)
        {
            (await principal.SatisfiesAsync(policy)).ShouldBeFalse(
                $"the Compass root must NOT satisfy '{policy}' — it is not our SuperAdmin");
        }
    }

    [Fact]
    public void NoCompassRoleString_CollidesWithATimesheetRoleString()
    {
        // Arrange — the group-to-role mapper matches case-INSENSITIVELY, so a casing-only difference
        // is not a difference and must be treated as a collision.
        var collisions = KnownRoles.Compass
            .Where(c => KnownRoles.Timesheet.Contains(c))
            .ToList();

        // Assert — "Compass Ops" must never be, or become, the bare timesheet "Ops".
        collisions.ShouldBeEmpty(
            "a Compass role string that equals a timesheet role string would grant that role in "
            + "the OTHER module: " + string.Join(", ", collisions));
    }

    [Fact]
    public void EveryConfiguredCompassGroup_MapsOnlyToACompassRole()
    {
        // Arrange — read the REAL development configuration, not a fixture. The mistake being
        // prevented is concrete: a Compass operations group mapped to the bare timesheet operations
        // role ("Compass-Ops" -> "Ops") would grant Ops *in timesheet*. Nothing about that
        // config line looks wrong at a glance, and it fails OPEN.
        var config = new ConfigurationBuilder()
            .AddJsonFile(CompassPrincipalBuilder.DevelopmentSettingsPath(), optional: false)
            .Build();
        var options = config.GetSection("GoogleAuth").Get<GoogleAuthOptions>();
        options.ShouldNotBeNull();

        var compassGroups = options.Groups
            .Where(g => g.GroupName.StartsWith(KnownRoles.CompassGroupPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Assert — prod + -dev exist for each of the four roles, and each maps only to a Compass role.
        compassGroups.Count.ShouldBe(8);
        foreach (var group in compassGroups)
        {
            KnownRoles.Compass.ShouldContain(
                group.Role,
                $"Compass group '{group.GroupName}' maps to '{group.Role}', which is not a Compass role");
        }
    }

    [Fact]
    public void TheEightCompassGroupNames_AreByteExact_ProdAndDev()
    {
        // Arrange — each Compass role is granted by BOTH the production Google group and the
        // "-dev" Google group. Space-free names (like Timesheet-*-dev), verified from live SAML.
        var config = new ConfigurationBuilder()
            .AddJsonFile(CompassPrincipalBuilder.DevelopmentSettingsPath(), optional: false)
            .Build();
        var names = config.GetSection("GoogleAuth").Get<GoogleAuthOptions>()!
            .Groups.Select(g => g.GroupName).ToList();

        // Assert — literal, byte-for-byte (prod + -dev for each role).
        names.ShouldContain("Compass-SuperAdmin");
        names.ShouldContain("Compass-SuperAdmin-dev");
        names.ShouldContain("Compass-Admin");
        names.ShouldContain("Compass-Admin-dev");
        names.ShouldContain("Compass-Ops");
        names.ShouldContain("Compass-Ops-dev");
        names.ShouldContain("Compass-Sales");
        names.ShouldContain("Compass-Sales-dev");
    }
}
