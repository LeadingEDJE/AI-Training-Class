using System.Reflection;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// The caller-resolution port stays as narrow as it was specified to be: one operation, three
/// carried members, and nothing but BCL types.
/// </summary>
/// <remarks>
/// <para>
/// Why the shape is worth a gate. The port's value is what it CANNOT do. It takes
/// no email parameter, so no caller can resolve somebody else as "the caller"; and it carries three
/// members rather than a copy of the directory record, so Platform does not acquire a second
/// employee DTO of its own. Both properties are invisible in prose and one careless signature change
/// away, so they are asserted rather than described.
/// </para>
/// <para>
/// The module-naming check is deliberately NOT duplicated here.
/// <see cref="EmployeeDirectoryContractTests.NeitherPort_NamesATypeFromAnyModule"/> runs that
/// detector over this port too — <c>ICallerDirectory</c> is in its array — and that detector has
/// proven positive and negative controls (<c>TheModuleDetector_Fires…</c> and
/// <c>TheModuleDetector_StaysSilent…</c>). A second, control-less copy of the same scan here would
/// add a place for the rule to rot without adding any coverage.
/// </para>
/// </remarks>
public class CallerDirectoryContractTests
{
    [Fact]
    public void TheCallerPort_ExposesExactlyOneOperation()
    {
        // Act
        var methods = typeof(ICallerDirectory).GetMethods();

        // Assert
        methods.Select(m => m.Name).ShouldBe(["ResolveCallerAsync"]);
    }

    // The absent parameter IS the contract: a port that accepted an address would answer "tell me
    // about this person", which the module's own published contract already does. Only a port that
    // cannot be asked about anyone else can be trusted to answer "who is calling".
    [Fact]
    public void TheCallerPort_TakesNoIdentityParameter_OnlyACancellationToken()
    {
        // Act
        var parameters = typeof(ICallerDirectory)
            .GetMethod(nameof(ICallerDirectory.ResolveCallerAsync))!
            .GetParameters();

        // Assert
        parameters.Select(p => p.ParameterType).ShouldBe([typeof(CancellationToken)]);
    }

    [Fact]
    public void TheCallerRecord_CarriesExactlyThreeMembers()
    {
        // Act
        var properties = Properties<CallerDirectoryEntry>();

        // Assert
        properties.Select(p => p.Name).ShouldBe(
            ["DirectoryId", "DisplayName", "Email"],
            ignoreOrder: true);
    }

    // Named individually so the failure message says WHICH member crossed. All three are already
    // reachable by DirectoryId through the module's own published contract, so carrying one here
    // would be a second copy of the directory record living inside Platform.
    [Theory]
    [InlineData("Timezone")]
    [InlineData("IsActive")]
    [InlineData("CoachEmployeeId")]
    public void TheCallerRecord_DoesNotCarry(string forbiddenMember)
    {
        // Act
        var carried = Properties<CallerDirectoryEntry>().Select(p => p.Name).ToList();

        // Assert
        carried.ShouldNotContain(forbiddenMember);
    }

    // Self-containment. Every member type must be a BCL type, or the record drags a module type
    // across the boundary the port exists to establish.
    [Fact]
    public void EveryCarriedMemberType_ComesFromTheBaseClassLibrary()
    {
        // Arrange
        var bcl = typeof(object).Assembly;
        var properties = Properties<CallerDirectoryEntry>();

        // Act
        var offenders = properties
            .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType).Assembly != bcl)
            .Select(p => $"{p.Name} : {p.PropertyType.Name}")
            .ToList();

        // Assert — the non-vacuity half FIRST. ShouldBeEmpty over an accidentally-empty collection
        // passes, which is the failure mode this repository has hit repeatedly; if reflection ever
        // stops seeing the record's members, this gate must go red rather than green for free.
        properties.ShouldNotBeEmpty(
            "the scan inspected ZERO members and would have passed for free");

        offenders.ShouldBeEmpty(
            "the caller port must be declared over BCL types only. Offenders: "
                + string.Join(", ", offenders));
    }

    private static IReadOnlyList<PropertyInfo> Properties<T>() =>
        [.. typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)];
}
