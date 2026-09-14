using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// Contract tests pinning the EXACT public surface of the two lookup DTOs, and the entity properties
/// they project.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on <see cref="CompassEmployeeDtoTests"/>, and for the same reason: it makes "add one more
/// field while I'm here" a failing build. The API contract states the shape as <c>{ id, typeName,
/// isActive }</c> and Principle II forbids speculative fields, so the member set is the requirement,
/// not an implementation detail.
/// </para>
/// <para>
/// Unlike the boundary DTO's test, this one guards a shape the acceptance criteria fix rather than a
/// phase's scope promise — AC-25 and AC-26 describe exactly a name and an active flag. Extending it
/// needs a criterion asking for the new field.
/// </para>
/// </remarks>
public class CompassLookupDtoTests
{
    private static readonly string[] AgreedMembers = ["Id", "TypeName", "IsActive"];

    private static string[] MembersOf(Type type) =>
        [.. type.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)];

    [Fact]
    public void EmployeeTypeDto_ExposesExactlyTheAgreedMembers()
    {
        // Arrange & Act
        var actual = MembersOf(typeof(EmployeeTypeDto));

        // Assert
        actual.ShouldBe(
            [.. AgreedMembers.OrderBy(n => n, StringComparer.Ordinal)],
            "the API contract fixes the lookup shape at { id, typeName, isActive }"
        );
    }

    [Fact]
    public void InvoiceFrequencyTypeDto_ExposesExactlyTheAgreedMembers()
    {
        // Arrange & Act
        var actual = MembersOf(typeof(InvoiceFrequencyTypeDto));

        // Assert
        actual.ShouldBe([.. AgreedMembers.OrderBy(n => n, StringComparer.Ordinal)]);
    }

    [Fact]
    public void LookupDtos_AreSeparateTypes_SoTheTwoContractsCanDiverge()
    {
        // Arrange & Act & Assert — structurally identical today, deliberately not unified: they are
        // two published contracts and appear as distinct OpenAPI schemas.
        typeof(EmployeeTypeDto).ShouldNotBe(typeof(InvoiceFrequencyTypeDto));
    }

    [Theory]
    [InlineData(typeof(EmployeeType))]
    [InlineData(typeof(InvoiceFrequencyType))]
    public void BothLookupEntities_ImplementTheSharedLookupContract(Type entityType)
    {
        // Arrange & Act & Assert — the repository is written once against ICompassLookup. If an
        // entity stopped implementing it the generic repository would not close over it, so this
        // states the dependency rather than leaving it to a compile error elsewhere.
        //
        // NOT ShouldBeAssignableTo: the subject here is a Type OBJECT, so that overload would ask
        // whether System.Type implements the interface — which is false for every input and would fail
        // the test for the wrong reason.
        typeof(ICompassLookup)
            .IsAssignableFrom(entityType)
            .ShouldBeTrue($"{entityType.Name} must implement ICompassLookup");
    }

    [Fact]
    public void TheLookupContract_ExposesOnlyWhatTheAdminSurfaceAdministers()
    {
        // Arrange & Act — the interface exists to let one repository serve both lookups. Widening it
        // would push entity-specific concerns into shared code.
        var actual = MembersOf(typeof(ICompassLookup));

        // Assert
        actual.ShouldBe([.. AgreedMembers.OrderBy(n => n, StringComparer.Ordinal)]);
    }
}
