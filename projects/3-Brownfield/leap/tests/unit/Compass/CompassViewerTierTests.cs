using LeadingEDJE.Leap.Api.Modules.Compass.Authorization;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The viewer tier every Compass read projection is built for.
/// </summary>
/// <remarks>
/// <para>
/// Every criterion in Stream 4 except AC-13 is a statement about what a particular viewer may see,
/// and BR-1 collects them into one visibility matrix — so getting this resolution wrong discloses a
/// colleague's contract terms, rate history, or the fact that they have left the company.
/// </para>
/// <para>
/// Two properties here are easy to get wrong and are asserted explicitly: Compass Admin is
/// elevated (AC-44 makes it read-only; BR-1 makes its visibility fully elevated —
/// capability and visibility are independent axes), and no Timesheet role contributes anything
/// (Principle IV: Compass inherits nothing, not even root).
/// </para>
/// </remarks>
public class CompassViewerTierTests
{
    [Fact]
    public void Resolve_AuthenticatedWithNoCompassRole_IsBaseline()
    {
        // The base EDJEr role is implicit (BR-2). Tier B is not an error state — AC-5 and AC-12 open
        // "Given any authenticated EDJEr", and CompassNav already treats both directories as baseline.
        CompassViewerTier.Resolve([]).ShouldBe(CompassTier.Baseline);
    }

    [Fact]
    public void Resolve_CompassSuperAdmin_IsSuperAdmin()
    {
        CompassViewerTier.Resolve([RolePolicy.CompassSuperAdminRole]).ShouldBe(CompassTier.SuperAdmin);
    }

    [Theory]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    public void Resolve_AnyElevatedCompassRole_IsElevated(string role)
    {
        // BR-1 treats all three identically for VISIBILITY. A check written against only one would
        // miss a policy wired to the wrong role, so all three are exercised.
        CompassViewerTier.Resolve([role]).ShouldBe(CompassTier.Elevated);
    }

    [Fact]
    public void Resolve_CompassAdmin_IsElevatedDespiteBeingReadOnly()
    {
        // AC-44 makes Compass Admin read-ONLY; BR-1 makes its visibility fully elevated. Conflating
        // "read-only role" with "restricted view" would hide inactive EDJErs and SOWs from the role
        // whose entire purpose is looking up any team member, current or former.
        CompassViewerTier.Resolve([RolePolicy.CompassAdminRole]).ShouldBe(CompassTier.Elevated);
    }

    [Fact]
    public void Resolve_SuperAdminAlongsideAnotherCompassRole_IsSuperAdmin()
    {
        // The tiers are a ladder evaluated top-down, mirroring AddCompassAuthorization where the
        // Compass root satisfies the other three Compass policies.
        CompassViewerTier
            .Resolve([RolePolicy.CompassOpsRole, RolePolicy.CompassSuperAdminRole])
            .ShouldBe(CompassTier.SuperAdmin);
    }

    [Theory]
    [InlineData(RolePolicy.SuperAdmin)]
    [InlineData(RolePolicy.Admin)]
    [InlineData(RolePolicy.Ops)]
    public void Resolve_TimesheetRolesAlone_AreBaseline(string timesheetRole)
    {
        // Principle IV, and the direction that fails OPEN. A timesheet SuperAdmin is NOT a Compass
        // anything; granting elevated Compass visibility on a timesheet role would reverse an owner
        // decision silently, and the local DevBypass identity carries exactly these nine strings.
        CompassViewerTier.Resolve([timesheetRole]).ShouldBe(CompassTier.Baseline);
    }

    [Fact]
    public void Resolve_TimesheetRolesMixedWithACompassRole_HonoursOnlyTheCompassOne()
    {
        // The realistic shape: /api/me returns one array carrying every module's roles for a person.
        CompassViewerTier
            .Resolve([RolePolicy.SuperAdmin, RolePolicy.CompassSalesRole, RolePolicy.EDJEr])
            .ShouldBe(CompassTier.Elevated);
    }

    [Fact]
    public void Resolve_UnknownRoleStrings_AreIgnored()
    {
        CompassViewerTier.Resolve(["Not A Role", "compass super admin", string.Empty])
            .ShouldBe(CompassTier.Baseline, "matching is exact — a lowercased variant is not the role");
    }

    [Fact]
    public void Resolve_NullPrivileges_IsBaseline()
    {
        CompassViewerTier.Resolve(null).ShouldBe(CompassTier.Baseline);
    }

    // ------------------------------------------------------------------ Derived predicates

    [Fact]
    public void SeesInactiveEdjers_IsFalseForBaselineAndTrueForEveryElevatedTier()
    {
        // AC-9 / BR-1, expressed once so all three listings consume the same answer (FR-023).
        CompassTier.Baseline.SeesInactiveEdjers().ShouldBeFalse();
        CompassTier.Elevated.SeesInactiveEdjers().ShouldBeTrue();
        CompassTier.SuperAdmin.SeesInactiveEdjers().ShouldBeTrue();
    }

    [Fact]
    public void SeesTimeTrackingSettings_IsSuperAdminOnly()
    {
        // AC-10, and AC-14's Client Details panel. The cell "elevated means sees everything"
        // intuition gets wrong.
        CompassTier.Baseline.SeesTimeTrackingSettings().ShouldBeFalse();
        CompassTier.Elevated.SeesTimeTrackingSettings().ShouldBeFalse();
        CompassTier.SuperAdmin.SeesTimeTrackingSettings().ShouldBeTrue();
    }

    [Fact]
    public void SeesOthersSowsAndNotes_IsFalseOnlyForBaseline()
    {
        // AC-11 and AC-16. Baseline's own-record exception is a per-record decision handled
        // elsewhere; this is the per-tier part.
        CompassTier.Baseline.SeesOthersSowsAndNotes().ShouldBeFalse();
        CompassTier.Elevated.SeesOthersSowsAndNotes().ShouldBeTrue();
        CompassTier.SuperAdmin.SeesOthersSowsAndNotes().ShouldBeTrue();
    }
}
