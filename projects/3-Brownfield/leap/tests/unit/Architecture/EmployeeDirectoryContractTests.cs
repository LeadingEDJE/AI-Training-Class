using System.Reflection;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// The employee-directory seam stays as narrow as it was specified to be: one port, one operation,
/// six carried members, and nothing from any module.
/// </summary>
/// <remarks>
/// <para>
/// Narrowed after the Timesheet/Ooto module retirement: the notification-preference path
/// (<c>GetNotificationSettingsAsync</c>/<c>EmployeeNotificationSettings</c>), the by-EdjeId lookup
/// (<c>GetEmployeeByEdjeIdAsync</c>) and the Slack-id write port (<c>ISlackUserIdCache</c>) all served
/// only the real <c>NotificationService</c>, which retired with the module it read channel preferences
/// from (Timesheet's <c>EmployeeAttribute</c>). <c>ICallerDirectory</c> answers a different question —
/// who the caller IS rather than what a named employee is like — and keeps its own shape test in
/// <see cref="CallerDirectoryContractTests"/>; it still borrows <see cref="NeitherPort_NamesATypeFromAnyModule"/>
/// from here, because that is the detector with proven controls.
/// </para>
/// <para>
/// The member count is the point, not a formality. Fewer members breaks the audit actor name
/// resolution, which reads the first and last name. More members export data no Platform caller reads.
/// </para>
/// </remarks>
public class EmployeeDirectoryContractTests
{
    private static readonly Assembly Api = typeof(IEmployeeDirectory).Assembly;

    [Fact]
    public void TheReadPort_ExposesExactlyOneOperation()
    {
        // Act
        var methods = typeof(IEmployeeDirectory).GetMethods();

        // Assert
        methods.Select(m => m.Name).ShouldBe(["GetAllEmployeesAsync"]);
    }

    [Fact]
    public void TheEmployeeRecord_CarriesExactlySixMembers()
    {
        // Act
        var properties = Properties<EmployeeDirectoryEntry>();

        // Assert
        properties.Select(p => p.Name).ShouldBe(
            ["Id", "Name", "EdjeId", "Email", "FirstName", "LastName"],
            ignoreOrder: true);
    }

    // Named individually rather than by a blanket count so the failure message says WHICH
    // member crossed - and IsDeliveryTeam is the one that matters, because other in-flight work is
    // defining it right now.
    [Theory]
    [InlineData("IsDeliveryTeam")]
    [InlineData("Is1099Contractor")]
    [InlineData("CoachId")]
    public void TheSeam_DoesNotCarry(string forbiddenMember)
    {
        // Act
        var carried = Properties<EmployeeDirectoryEntry>()
            .Select(p => p.Name)
            .ToList();

        // Assert
        carried.ShouldNotContain(forbiddenMember);
    }

    // Self-containment. Every member type must be a BCL type, or the record drags a module
    // type across the boundary the seam exists to establish.
    [Fact]
    public void EveryCarriedMemberType_ComesFromTheBaseClassLibrary()
    {
        // Arrange
        var bcl = typeof(object).Assembly;

        // Act
        var offenders = Properties<EmployeeDirectoryEntry>()
            .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType).Assembly != bcl)
            .Select(p => $"{p.Name} : {p.PropertyType.Name}")
            .ToList();

        // Assert
        offenders.ShouldBeEmpty();
    }

    // From the other direction: no port may NAME a module type anywhere in its signatures - not as
    // a parameter, not as a return type, not nested inside a generic.
    // ICallerDirectory rides this detector rather than growing a control-less copy of it, and the
    // stake there is higher: PlatformPurityTests has no `.Contracts` exemption, so a Compass contract
    // type reaching that port fails the suite from two directions at once.
    [Fact]
    public void NeitherPort_NamesATypeFromAnyModule()
    {
        // Act
        var offenders = new[]
            {
                typeof(IEmployeeDirectory),
                typeof(ICallerDirectory),
            }
            .SelectMany(port => port.GetMethods())
            .SelectMany(TypesNamedBy)
            .Where(t => t.Assembly == Api)
            .Where(t => t.Namespace?.Contains(".Modules.", StringComparison.Ordinal) == true)
            .Select(t => t.FullName!)
            .Distinct()
            .ToList();

        // Assert
        offenders.ShouldBeEmpty();
    }

    // ⚠️ THE CONTROL for the test above, and it must run the SAME detector over a signature that
    // genuinely names a module type. An earlier version of this test merely asserted that some
    // module-namespaced type existed in the assembly — which would stay green even if the detector
    // were broken, making the "no offenders" result above vacuous. That is the exact failure mode
    // this feature spent four review rounds hunting, so it is not left as prose.
    [Theory]
    [InlineData(nameof(DetectorProbe.NamesAModuleTypeDirectly))]
    [InlineData(nameof(DetectorProbe.NamesAModuleTypeInsideAGeneric))]
    [InlineData(nameof(DetectorProbe.NamesAModuleTypeNestedTwoGenericsDeep))]
    [InlineData(nameof(DetectorProbe.NamesAModuleTypeNestedThreeGenericsDeep))]
    public void TheModuleDetector_Fires_OnASignatureThatNamesAModuleType(string probeMethod)
    {
        // Arrange
        var method = typeof(DetectorProbe).GetMethod(probeMethod)!;

        // Act — the identical filter used by NeitherPort_NamesATypeFromAnyModule
        var offenders = TypesNamedBy(method)
            .Where(t => t.Assembly == Api)
            .Where(t => t.Namespace?.Contains(".Modules.", StringComparison.Ordinal) == true)
            .ToList();

        // Assert
        offenders.ShouldNotBeEmpty();
    }

    [Fact]
    public void TheModuleDetector_StaysSilent_OnASignatureThatNamesOnlyPlatformAndBclTypes()
    {
        // Arrange — the negative half; without it the control above could pass by matching everything
        var method = typeof(DetectorProbe).GetMethod(nameof(DetectorProbe.NamesNoModuleType))!;

        // Act
        var offenders = TypesNamedBy(method)
            .Where(t => t.Assembly == Api)
            .Where(t => t.Namespace?.Contains(".Modules.", StringComparison.Ordinal) == true)
            .ToList();

        // Assert
        offenders.ShouldBeEmpty();
    }

    private static IReadOnlyList<PropertyInfo> Properties<T>() =>
        [.. typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)];

    // Recurses to ANY depth. A fixed two levels left `Task<IReadOnlyList<Dictionary<string, X>>>`
    // invisible, which would have made the port scan pass vacuously on a signature that really did
    // name a module type — see the three-generics-deep probe below.
    private static IEnumerable<Type> TypesNamedBy(MethodInfo method) =>
        method.GetParameters()
            .Select(p => p.ParameterType)
            .Append(method.ReturnType)
            .SelectMany(Flatten);

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var inner in Flatten(argument))
            {
                yield return inner;
            }
        }
    }

    /// <remarks>
    /// Fixture signatures for the detector controls. These are never called; only their shapes matter.
    /// They deliberately name a module type (Compass's own employee DTO) at several generic depths,
    /// which is why this test class is allowed to import a module namespace where the ports under
    /// test are not.
    /// </remarks>
    private abstract class DetectorProbe
    {
        public abstract Modules.Compass.Contracts.CompassEmployeeDto NamesAModuleTypeDirectly();

        public abstract Task<Modules.Compass.Contracts.CompassEmployeeDto> NamesAModuleTypeInsideAGeneric();

        public abstract Task<IReadOnlyList<Modules.Compass.Contracts.CompassEmployeeDto>> NamesAModuleTypeNestedTwoGenericsDeep();

        public abstract Task<IReadOnlyList<Dictionary<string, Modules.Compass.Contracts.CompassEmployeeDto>>> NamesAModuleTypeNestedThreeGenericsDeep();

        public abstract Task<IReadOnlyList<EmployeeDirectoryEntry>> NamesNoModuleType();
    }
}
