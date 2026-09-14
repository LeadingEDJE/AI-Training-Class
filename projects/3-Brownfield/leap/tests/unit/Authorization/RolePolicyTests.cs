using LeadingEDJE.Leap.Api.Platform.Authorization;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Authorization;

public class RolePolicyTests
{
    [Fact]
    public void EDJEr_EqualsExpectedValue()
    {
        // Assert
        RolePolicy.EDJEr.ShouldBe("EDJEr");
    }

    [Fact]
    public void AllNineRoles_AreNonNullAndNonEmpty()
    {
        // Arrange
        var roles = new[]
        {
            RolePolicy.EDJEr,
            RolePolicy.Manager,
            RolePolicy.TimesheetProcessor,
            RolePolicy.Accounting,
            RolePolicy.HR,
            RolePolicy.Ops,
            RolePolicy.PayrollProcessor,
            RolePolicy.Admin,
            RolePolicy.SuperAdmin
        };

        // Assert
        roles.Length.ShouldBe(9);
        foreach (var role in roles)
        {
            role.ShouldNotBeNullOrEmpty();
        }
    }

    [Fact]
    public void CompoundPolicies_Exist()
    {
        // Assert
        RolePolicy.ManagerOrProcessor.ShouldNotBeNullOrEmpty();
        RolePolicy.HROrSuperAdmin.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void AllPolicyNames_AreUnique()
    {
        // Arrange
        var allPolicies = new[]
        {
            RolePolicy.EDJEr,
            RolePolicy.Manager,
            RolePolicy.TimesheetProcessor,
            RolePolicy.Accounting,
            RolePolicy.HR,
            RolePolicy.Ops,
            RolePolicy.PayrollProcessor,
            RolePolicy.Admin,
            RolePolicy.SuperAdmin,
            RolePolicy.ManagerOrProcessor,
            RolePolicy.HROrSuperAdmin
        };

        // Assert
        allPolicies.Distinct().Count().ShouldBe(allPolicies.Length);
    }
}
