using LeadingEDJE.Leap.Api.Modules.Compass;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The residence vocabulary FR-014 restricts an EDJEr to: the 50 states plus DC, and nothing else.
/// </summary>
/// <remarks>
/// This list has two consumers — the database <c>CHECK</c> generated in <c>EmployeeConfiguration</c> and
/// the validation in <c>CompassEmployeeService</c>. It was a private array in the configuration until the
/// service needed the same values; two copies of 51 codes can drift, and the symptom would be a 500 on a
/// value the service believed it had accepted.
/// </remarks>
public class UsStateCodesTests
{
    [Fact]
    public void All_ContainsExactlyFiftyOneCodes()
    {
        // Arrange & Act & Assert — 50 states plus DC. A count that drifts means a state was lost or a
        // territory crept in, and the CHECK constraint would drift with it.
        UsStateCodes.All.Length.ShouldBe(51);
    }

    [Fact]
    public void All_HasNoDuplicates()
    {
        // Arrange & Act & Assert — a duplicate would pass the count check by masking a missing state.
        UsStateCodes.All.Distinct(StringComparer.Ordinal).Count().ShouldBe(UsStateCodes.All.Length);
    }

    [Fact]
    public void All_IsUpperCaseTwoLetterCodes()
    {
        // Arrange & Act & Assert — the column is char(2) and the CHECK compares literals, so a
        // lower-case or long entry would silently make that state unusable.
        UsStateCodes.All.ShouldAllBe(code => code.Length == 2);
        UsStateCodes.All.ShouldAllBe(code => code == code.ToUpperInvariant());
    }

    [Theory]
    [InlineData("OH")]
    [InlineData("DC")]
    [InlineData("AK")]
    [InlineData("HI")]
    [InlineData("WY")]
    public void IsValid_AcceptsAnAcceptedCode(string code) =>
        UsStateCodes.IsValid(code).ShouldBeTrue();

    [Theory]
    [InlineData("oh")]
    [InlineData("Dc")]
    public void IsValid_IsCaseInsensitive_SoAFormMaySubmitEitherSpelling(string code) =>
        // The service upper-cases before storing, so accepting the lower-case spelling here is what makes
        // the two consistent.
        UsStateCodes.IsValid(code).ShouldBeTrue();

    [Theory]
    [InlineData("PR", "Puerto Rico — a territory, deliberately excluded per AC-NFR-6")]
    [InlineData("GU", "Guam — likewise")]
    [InlineData("VI", "US Virgin Islands — likewise")]
    [InlineData("AS", "American Samoa — likewise")]
    [InlineData("MP", "Northern Mariana Islands — likewise")]
    public void IsValid_RejectsAUSTerritory(string code, string because) =>
        UsStateCodes.IsValid(code).ShouldBeFalse(because);

    [Theory]
    [InlineData("ZZ")]
    [InlineData("")]
    [InlineData("OHIO")]
    [InlineData(" OH")]
    [InlineData("O")]
    [InlineData(null)]
    public void IsValid_RejectsAnythingElse(string? code) =>
        // Note " OH" is rejected: IsValid does not trim, deliberately. Callers normalise first, so that
        // trimming happens in one place rather than being re-implemented per validator.
        UsStateCodes.IsValid(code).ShouldBeFalse();
}
