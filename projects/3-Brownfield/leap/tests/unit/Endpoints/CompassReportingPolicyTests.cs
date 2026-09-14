using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Tests.TestData;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// The fifth Compass authorization policy — <c>RolePolicy.CompassReporting</c> — feature 007, T012.
/// </summary>
/// <remarks>
/// <para>
/// None of the four shipped Compass policies expresses this feature's audience: "Sales OR Ops OR
/// Super Admin" (FR-018). <c>CompassSales</c> admits Sales and the root but not Ops;
/// <c>CompassOps</c> admits Ops and the root but not Sales.
/// </para>
/// <para>
/// The load-bearing negative case is Compass Admin — FR-019: a Compass Admin holding no other
/// Compass role must be refused by the server, not merely denied a nav link. The name reads as
/// administrative but the role is READ-ONLY in Compass (AC-44), and this feature's four surfaces are
/// among the things it does not administer.
/// </para>
/// <para>
/// This is a policy-resolution test, driven through the REAL registered authorization service via
/// <see cref="CompassPrincipalBuilder"/> rather than re-stating the rule — so it breaks if someone
/// later widens the policy, which is the point. The endpoint-level assertions (every route, every
/// role, T020/T051a/T098) come once the endpoints exist; this is the inner-loop test for the policy
/// itself (Principle VI).
/// </para>
/// </remarks>
public class CompassReportingPolicyTests
{
    public static TheoryData<string> PermittedRoles => new()
    {
        RolePolicy.CompassSalesRole,
        RolePolicy.CompassOpsRole,
        RolePolicy.CompassSuperAdminRole,
    };

    [Theory]
    [MemberData(nameof(PermittedRoles))]
    public async Task CompassReporting_AdmitsEachOfTheThreePermittedRoles(string role)
    {
        // Arrange
        using var principal = CompassPrincipalBuilder.A().WithRoles(role).Build();

        // Act / Assert
        (await principal.SatisfiesAsync(RolePolicy.CompassReporting)).ShouldBeTrue(
            $"{role} must be admitted by CompassReporting");
    }

    [Fact]
    public async Task CompassReporting_Denies_CompassAdmin()
    {
        // Arrange — FR-019's testable core. Compass Admin holds no OTHER Compass role here.
        using var principal = CompassPrincipalBuilder.A().WithRoles(RolePolicy.CompassAdminRole).Build();

        // Act / Assert
        (await principal.SatisfiesAsync(RolePolicy.CompassReporting)).ShouldBeFalse(
            "Compass Admin is READ-ONLY in Compass (AC-44) and must be refused, not merely hidden");
    }

    public static TheoryData<string> EveryTimesheetAndOotoRole => new()
    {
        "EDJEr", "Manager", "TimesheetProcessor", "Accounting", "HR", "Ops",
        "PayrollProcessor", "Admin", "SuperAdmin", "OOTO Admin", "OOTO Reports",
    };

    [Theory]
    [MemberData(nameof(EveryTimesheetAndOotoRole))]
    public async Task CompassReporting_Denies_EveryTimesheetAndOotoRoleString(string role)
    {
        // Arrange — Compass inherits nothing, not even root (Principle IV). Adding a timesheet or
        // OOTO role string here would reverse an owner decision.
        using var principal = CompassPrincipalBuilder.A().WithRoles(role).Build();

        // Act / Assert
        (await principal.SatisfiesAsync(RolePolicy.CompassReporting)).ShouldBeFalse(
            $"{role} is a timesheet/OOTO role and must not satisfy Compass");
    }
}
