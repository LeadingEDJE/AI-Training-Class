using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Authorization;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The one EDJEr visibility rule, applied by every listing in Compass.
/// </summary>
/// <remarks>
/// <para>
/// AC-9 governs "an employee detail or any EDJEr listing", and there are three of them in this
/// stream: the Team Directory, an employee's assignment history, and a client's assignment history.
/// FR-023 requires the rule to exist once — retrofitting a visibility rule across three
/// surfaces after they ship is how one gets missed, and the failure is silent in the direction that
/// matters: a regular EDJEr seeing a departed colleague looks like working software.
/// </para>
/// <para>
/// This is an expression, not a post-load filter, and that is the point. FR-021 says a regular
/// EDJEr must not receive an inactive EDJEr — so the restriction belongs in the query, not in
/// a list comprehension after the rows have already crossed the wire.
/// </para>
/// </remarks>
public class EdjerVisibilityTests
{
    private static readonly Employee Active = new() { Id = 1, FirstName = "Ada", LastName = "Active", IsActive = true };
    private static readonly Employee Inactive = new() { Id = 2, FirstName = "Ivor", LastName = "Inactive", IsActive = false };

    [Fact]
    public void Baseline_SeesActiveEdjersOnly()
    {
        Visible(CompassTier.Baseline).ShouldBe([Active]);
    }

    [Theory]
    [InlineData(CompassTier.Elevated)]
    [InlineData(CompassTier.SuperAdmin)]
    public void EveryElevatedTier_SeesBoth(CompassTier tier)
    {
        Visible(tier).ShouldBe([Active, Inactive]);
    }

    [Fact]
    public void TheRuleIsAnExpression_SoItComposesIntoAQuery()
    {
        // If this ever becomes a Func<Employee, bool>, EF can no longer translate it and the filter
        // silently moves client-side — which means the inactive rows WERE fetched, defeating FR-021
        // while every visible behaviour stays identical. Asserting the type is what catches that.
        // Assignable-to, not of-type: an expression tree's runtime type is an internal subclass of
        // Expression<T>, so an exact-type assertion fails against correct code. This still catches
        // the change that matters — a return type of Func<Employee, bool> is not assignable here.
        var predicate = EdjerVisibility.For(CompassTier.Baseline);

        predicate.ShouldBeAssignableTo<System.Linq.Expressions.Expression<Func<Employee, bool>>>();
    }

    [Fact]
    public void TheBaselineRuleDependsOnIsActive_NotOnAnyOtherField()
    {
        // A guard against the rule quietly acquiring a second condition. AC-9 is about active status
        // and nothing else; narrowing it further would hide active colleagues from a regular EDJEr,
        // and widening it would disclose former ones.
        var employees = new[]
        {
            new Employee { Id = 3, IsActive = true, CoachEmployeeId = null, StateOfResidence = "OH" },
            new Employee { Id = 4, IsActive = true, CoachEmployeeId = 1, StateOfResidence = "DC" },
        };

        employees.AsQueryable().Where(EdjerVisibility.For(CompassTier.Baseline)).Count().ShouldBe(2);
    }

    private static List<Employee> Visible(CompassTier tier) =>
        new[] { Active, Inactive }.AsQueryable().Where(EdjerVisibility.For(tier)).ToList();
}
