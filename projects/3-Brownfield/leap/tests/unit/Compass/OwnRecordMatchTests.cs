using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// How Compass decides that a viewer is looking at their own record (AC-11, FR-018).
/// </summary>
/// <remarks>
/// <para>
/// The only cell of the BR-1 matrix whose value depends on which record is being viewed rather
/// than only on who is viewing. A regular EDJEr sees SOWs on their own record and on no other, so a
/// wrong answer here discloses a colleague's contract terms.
/// </para>
/// <para>
/// Owner direction (spec Q1, 2026-08-12): match on email, fail closed. No correlation column
/// exists between the authenticated identity and <c>compass.employee</c>, and Principle II forbids
/// adding one speculatively. BR-9 makes the email unique across all EDJErs, active and inactive, so
/// it is a sound key.
/// </para>
/// <para>
/// Normalization is <c>lower(btrim(...))</c>, deliberately identical to the functional unique index
/// Stream 2's <c>#59</c> adds for BR-9. A different normalization here would mean two records the
/// database considers duplicates could disagree with the record this resolves to.
/// </para>
/// </remarks>
public class OwnRecordMatchTests
{
    private const string ViewerEmail = "test@leadingedje.com";

    [Fact]
    public void Matches_TheSameEmail()
    {
        Owned(ViewerEmail, Employee(ViewerEmail)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("TEST@LEADINGEDJE.COM")]
    [InlineData("Test@LeadingEdje.com")]
    public void Matches_RegardlessOfCase(string storedEmail)
    {
        // Case-insensitive by owner direction. Google delivers the address; its casing is not a
        // stable property, and a case-sensitive match would silently deny someone their own SOWs.
        Owned(ViewerEmail, Employee(storedEmail)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("  test@leadingedje.com")]
    [InlineData("test@leadingedje.com  ")]
    [InlineData(" test@leadingedje.com ")]
    public void Matches_IgnoringSurroundingWhitespace(string storedEmail)
    {
        Owned(ViewerEmail, Employee(storedEmail)).ShouldBeTrue();
    }

    [Fact]
    public void Matches_WhenTheViewersOwnEmailCarriesWhitespaceOrCase()
    {
        // Normalization applies to BOTH sides. Applying it to only the stored value is the subtle
        // half-fix that passes every test written from the database's point of view.
        Owned("  TEST@leadingedje.com ", Employee(ViewerEmail)).ShouldBeTrue();
    }

    [Fact]
    public void DoesNotMatch_ADifferentEmail()
    {
        Owned(ViewerEmail, Employee("someone.else@leadingedje.com")).ShouldBeFalse();
    }

    [Fact]
    public void DoesNotMatch_ASubstringOrPrefix()
    {
        // Full-string equality, never prefix or substring — the same rule the sign-in pipeline
        // applies to group names, and for the same reason: one real address can contain another.
        Owned("test@leadingedje.com", Employee("test@leadingedje.com.au")).ShouldBeFalse();
        Owned("test@leadingedje.com", Employee("nottest@leadingedje.com")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DoesNotMatch_WhenTheViewerHasNoUsableEmail_FailsClosed(string? viewerEmail)
    {
        // FR-018a. An unauthenticated or malformed identity owns NOTHING — it must never match an
        // employee whose stored email is itself blank, which is the shape a naive equality check
        // would produce.
        Owned(viewerEmail, Employee(ViewerEmail)).ShouldBeFalse();
        Owned(viewerEmail, Employee(string.Empty)).ShouldBeFalse();
        Owned(viewerEmail, Employee("   ")).ShouldBeFalse();
    }

    [Fact]
    public void DoesNotMatch_AnEmployeeWithABlankEmail_EvenForABlankViewer()
    {
        // Restated as its own case because it is the fail-open one: two empty strings are equal, and
        // an implementation that normalizes then compares would hand a session with no email every
        // emailless employee's SOWs.
        Owned(string.Empty, Employee(string.Empty)).ShouldBeFalse();
    }

    [Fact]
    public void MatchesExactlyOneRecord_AcrossARealisticSet()
    {
        // BR-9 guarantees uniqueness in the database; this asserts the predicate does not widen it.
        var employees = new[]
        {
            Employee("ada.lovelace@leadingedje.com"),
            Employee(ViewerEmail),
            Employee("TEST@LEADINGEDJE.COM.AU"),
            Employee("grace.hopper@leadingedje.com"),
        };

        employees.AsQueryable().Count(OwnRecordMatch.IsOwnedBy(ViewerEmail)).ShouldBe(1);
    }

    [Fact]
    public void TheRuleIsAnExpression_SoItComposesIntoAQuery()
    {
        // R-5: evaluated in the database, composing into the query already loading the record.
        // Matching in memory after loading inverts the ordering — the record would be fetched before
        // entitlement is known.
        OwnRecordMatch.IsOwnedBy(ViewerEmail)
            .ShouldBeAssignableTo<System.Linq.Expressions.Expression<Func<Employee, bool>>>();
    }

    private static bool Owned(string? viewerEmail, Employee employee) =>
        new[] { employee }.AsQueryable().Any(OwnRecordMatch.IsOwnedBy(viewerEmail));

    private static Employee Employee(string email) =>
        new() { Id = 1, FirstName = "Test", LastName = "User", Email = email };
}
