using System.Reflection;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Derived client status gates nothing — feature 004 US4, T105 (contract § 3, spec A-5).
/// </summary>
/// <remarks>
/// <para>
/// The non-gating property is load-bearing, not defensive. It is what makes the binary rule
/// safe. Case 1 of the derivation says a client with no assignments is Inactive — so a client is
/// Inactive at the moment it is created, and any path that filters, hides, or refuses on the
/// strength of Inactive makes a brand-new client impossible to use. AC-42 states it directly: "if
/// status ever gated assignment creation, a new client could never receive its first assignment."
/// </para>
/// <para>
/// Why this is a build gate rather than a review note. Filtering a client picker by status will
/// look like an obvious improvement to whoever writes it, and it will behave correctly in every case
/// they try — every client they test with already has assignments. It fails only for the brand-new
/// client, which is the one case a developer does not think to construct.
/// </para>
/// <para>
/// What this file owns and what it does not. Stream 2 asserts what it can from the derivation
/// itself: case 1 returns Inactive (<c>ClientStatusDerivationTests</c>), and the interface exposes no
/// filtering, eligibility, or editability entry point a consumer could reach for. The consumer-side
/// assertion — the named regression test <c>O6</c>, that a zero-assignment client appears in the
/// assignment picker — is Stream 3's, per Plan v7 and contract § 6. It is T053 and does not
/// exist yet, because the picker does not.
/// </para>
/// </remarks>
public class ClientStatusNonGatingTests
{
    /// <summary>
    /// The interface's entire surface, as shipped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pinned as a set rather than checked member by member, so adding a member fails too. That
    /// is the point: a <c>FilterActive(IQueryable&lt;Client&gt;)</c> or a
    /// <c>CanReceiveAssignment(Client)</c> would each be a perfectly reasonable-looking addition and
    /// each would hand a consumer the gate this contract forbids. Failing the build makes it a
    /// decision someone has to defend in a diff rather than one that arrives quietly.
    /// </para>
    /// <para>
    /// Three members added deliberately (feature 007, T008) — this IS the gate's own sanctioned
    /// path, not a weakening. <see cref="IClientStatusDerivation.IsFutureDated"/>,
    /// <see cref="IClientStatusDerivation.IsExpiringWithin"/> and
    /// <see cref="IClientStatusDerivation.HasNoFollowOn"/> each answer a question about a date, exactly
    /// as <c>IsCurrent</c> and <c>IsActive</c> already do — none of them filters a collection, decides
    /// assignment eligibility, or gates an edit. They are added here for the same reason FR-017
    /// confines them to <c>ClientStatusDerivation.cs</c> in the first place: a second SOW-expiry or
    /// rollout comparison written elsewhere is the exact divergence BR-11 and this pinned surface both
    /// exist to prevent.
    /// </para>
    /// </remarks>
    private static readonly string[] TheEntireSurface =
    [
        // Issue #274 replaced `ClientStatus From(Boolean)` with one naming method PER LEVEL. That was
        // not cosmetic: `From` was called in two incompatible senses at eight sites — six about a
        // client, two about an assignment — and adding a third CLIENT value to a shared method would
        // let a client-level caller lose Former silently, which is the divergence class this file
        // exists for. Two methods over two enums make it a compile error. Neither filters a
        // collection, decides eligibility, or gates an edit.
        "ClientStatus StatusOfClient(Boolean, Boolean)",
        "AssignmentStatus StatusOfAssignment(Boolean)",
        // The one new PREDICATE #274 needs, and the whole of what separates Inactive from Former. Same
        // shape as IsActive — a predicate over clients, never a set of them — so it hands a consumer
        // no gate: an Inactive client and a Former one are equally selectable and equally editable.
        "Expression`1 HasEverBeenAssigned()",
        "Expression`1 IsActive(DateOnly)",
        "Expression`1 IsCurrent(DateOnly)",
        "Expression`1 IsFutureDated(DateOnly)",
        // Added deliberately (feature 007, T009a, FR-032). HasStarted filters ASSIGNMENTS, not
        // clients: it returns no set of clients, decides no client eligibility, and gates no editing,
        // which is the property this pin protects. It exists because IsCurrent means "not ended"
        // rather than "active" — see spec Deviation 10.
        "Expression`1 HasStarted(DateOnly)",
        "Expression`1 IsExpiringWithin(DateOnly, Int32)",
        "Expression`1 HasNoFollowOn()",
        // Added for the Sales Dashboard's active-SOWs tile (issue #459). IsActiveSow filters SOWs, not
        // clients: it returns no set of clients, decides no client eligibility, and gates no editing —
        // the same property this pin protects for the other predicates.
        "Expression`1 IsActiveSow(DateOnly)",
        // Added deliberately (issue #457). A predicate over SOWs, not a set of them: it scopes the
        // expiring-SOWs population to SOWs whose assignment is open-ended and active. It filters no
        // clients, decides no eligibility, and gates no editing — the same non-gating shape as the
        // other predicates. It lives on this interface for FR-017's reason: a second assignment-end-
        // date comparison written elsewhere is the divergence BR-11 and this pinned surface prevent.
        "Expression`1 SowAssignmentIsOpenEndedAndActive(DateOnly)",
        "ClientStatus Of(Client, DateOnly)",
    ];

    [Fact]
    public void TheDerivation_ExposesExactlyTheShapesItShould_AndNothingThatCouldGate()
    {
        // Arrange / Act
        var surface = typeof(IClientStatusDerivation)
            .GetMethods()
            .Select(Describe)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        // Assert
        surface.ShouldBe(
            [.. TheEntireSurface.OrderBy(s => s, StringComparer.Ordinal)],
            "IClientStatusDerivation's surface changed. It answers what a client's status IS; it must "
                + "never offer a way to act on it. If the new member filters clients, decides "
                + "eligibility, or gates editing, it violates contract § 3 — and remember that a "
                + "brand-new client is Inactive, so any such gate makes a new client unusable. If it "
                + "genuinely does none of those things, add it to TheEntireSurface deliberately.");
    }

    [Fact]
    public void TheDerivation_NeverHandsBackASetOfClients_OnlyAPredicateOverThem()
    {
        // A member returning IQueryable<Client> or IEnumerable<Client> would BE the filtered set — a
        // consumer would reasonably read "the active clients" as "the clients you may assign". The
        // expression shapes force the caller to compose the predicate into a query it owns, which
        // keeps the decision about what to do with it at the call site where it is visible.
        var offenders = typeof(IClientStatusDerivation)
            .GetMethods()
            .Where(m => YieldsClients(m.ReturnType))
            .Select(Describe)
            .ToList();

        offenders.ShouldBeEmpty(
            "these members return a collection of clients: " + string.Join(", ", offenders)
                + ". Return a predicate the caller composes, never a pre-filtered set.");
    }

    [Fact]
    public void NoProductionMethodAnywhere_TakesADerivedStatusAsInput()
    {
        // Status is DISPLAY data (FR-036: authorization is role-based; FR-037: an Inactive client stays
        // fully editable). The moment a method accepts one as a parameter, something is branching on it
        // — which is the shape of every prohibition in contract § 3 at once.
        //
        // Covers AssignmentStatus as well as ClientStatus (issue #274). A fence that knew only about
        // the original enum would have a hole the width of the new one from the day it was added, and
        // an assignment-level gate is no more permitted than a client-level one.
        var methods = ProductionMethods();

        var offenders = methods
            .Where(m => m.GetParameters().Any(p => IsDerivedStatus(p.ParameterType)))
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
            .Distinct()
            .ToList();

        offenders.ShouldBeEmpty(
            "these production methods accept a derived status: " + string.Join(", ", offenders)
                + ". Status is an output, never an input to a selection, eligibility, or editability "
                + "decision (FR-036, FR-037, FR-038).");
    }

    [Fact]
    public void NoProductionTypeAnywhere_HoldsADerivedStatus()
    {
        // The other half of "never stored" (FR-035). CompassSchemaFromErdTests.Client_HasNoStoredStatusColumn
        // fences the database; this fences the object graph, where a cached field or a DTO member would
        // let a stale value outlive the assignments it summarises without any column appearing.
        //
        // Both enums, for the reason given above: AssignmentStatus is derived per read exactly as
        // ClientStatus is, so holding one is the same defect (issue #274).
        var offenders = ProductionTypes()
            // Each enum's own members are static fields of its own type — a declaration, not a holding.
            .Where(t => !IsDerivedStatus(t))
            .SelectMany(t => t
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Cast<MemberInfo>()
                .Concat(t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly)))
            .Where(IsDerivedStatusTyped)
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
            .ToList();

        offenders.ShouldBeEmpty(
            "these production members hold a derived status: " + string.Join(", ", offenders)
                + ". Status is derived on every read and never stored, cached, or carried (FR-035). "
                + "The read DTOs expose it as a string precisely so nothing can hold the enum.");
    }

    // ------------------------------------------------------------------ Non-vacuity

    [Fact]
    public void NonVacuity_TheScanInspectsRealTypesAndRealMethods()
    {
        // A reflection scan over an empty type set PASSES every assertion above.
        // docs/platform/adding-a-module.md records eight gates found silently fail-open this way.
        var types = ProductionTypes();
        var methods = ProductionMethods();

        types.Count.ShouldBeGreaterThan(100, "the production type scan found almost nothing");
        methods.Count.ShouldBeGreaterThan(100, "the production method scan found almost nothing");

        types.ShouldContain(
            typeof(ClientStatus),
            "ClientStatus itself is missing from the scan, so nothing here is being checked against it");
        types.ShouldContain(
            typeof(AssignmentStatus),
            "AssignmentStatus is missing from the scan, so the fences are not checking it either");
        types.ShouldContain(
            t => t.Name == nameof(LeadingEDJE.Leap.Api.Modules.Compass.Services.ClientStatusDerivation),
            "the derivation is missing from the scan");
    }

    [Fact]
    public void NonVacuity_TheChecksDetectTheViolationsTheyDescribe()
    {
        // The predicates are the gate. Asserted against known-bad shapes, so a refactor that stops
        // matching fails here rather than silently passing everything.
        YieldsClients(typeof(IQueryable<Client>)).ShouldBeTrue();
        YieldsClients(typeof(IEnumerable<Client>)).ShouldBeTrue();
        YieldsClients(typeof(List<Client>)).ShouldBeTrue();
        YieldsClients(typeof(Client)).ShouldBeFalse("one client is not a filtered set");
        YieldsClients(typeof(ClientStatus)).ShouldBeFalse();

        // Both enums, so extending the fences to AssignmentStatus is itself asserted rather than
        // assumed — a widened predicate that silently stopped matching the SECOND enum would leave
        // exactly the hole the widening was for (issue #274).
        IsDerivedStatus(typeof(ClientStatus)).ShouldBeTrue();
        IsDerivedStatus(typeof(AssignmentStatus)).ShouldBeTrue();
        IsDerivedStatus(typeof(ClientStatus?)).ShouldBeTrue("a nullable holding is still a holding");
        IsDerivedStatus(typeof(AssignmentStatus?)).ShouldBeTrue();
        IsDerivedStatus(typeof(string)).ShouldBeFalse("the DTOs carry the word as a string");

        typeof(Sample).GetMethod(nameof(Sample.GatesOnStatus))!
            .GetParameters()
            .ShouldContain(p => IsDerivedStatus(p.ParameterType));
        typeof(Sample).GetMethod(nameof(Sample.GatesOnAssignmentStatus))!
            .GetParameters()
            .ShouldContain(p => IsDerivedStatus(p.ParameterType));
        IsDerivedStatusTyped(typeof(Sample).GetProperty(nameof(Sample.Held))!).ShouldBeTrue();
        IsDerivedStatusTyped(typeof(Sample).GetProperty(nameof(Sample.HeldAssignment))!).ShouldBeTrue();
        IsDerivedStatusTyped(typeof(Sample).GetProperty(nameof(Sample.NotHeld))!).ShouldBeFalse();
    }

    // ------------------------------------------------------------------ Scanning

    private static string Describe(MethodInfo method) =>
        $"{method.ReturnType.Name} {method.Name}("
            + string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))
            + ")";

    private static bool YieldsClients(Type type)
    {
        if (type == typeof(Client))
        {
            return false;
        }

        return type.IsGenericType
            && type.GetGenericArguments().Any(a => a == typeof(Client))
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }

    /// <summary>Either derived-status enum — <c>ClientStatus</c> or <c>AssignmentStatus</c>.</summary>
    /// <remarks>
    /// One predicate rather than two checks at each site, so a THIRD derived-status enum (should one
    /// ever be justified) is added here once and every fence in this file picks it up. The failure
    /// mode being avoided is a new enum that no fence knows about — see issue #274, which added the
    /// second one.
    /// </remarks>
    private static bool IsDerivedStatus(Type type) =>
        Underlying(type) == typeof(ClientStatus) || Underlying(type) == typeof(AssignmentStatus);

    private static bool IsDerivedStatusTyped(MemberInfo member) => member switch
    {
        PropertyInfo property => IsDerivedStatus(property.PropertyType),
        FieldInfo field => IsDerivedStatus(field.FieldType),
        _ => false,
    };

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static List<Type> ProductionTypes() =>
        [.. typeof(ClientStatus).Assembly.GetTypes().Where(t => !IsCompilerGenerated(t))];

    private static List<MethodInfo> ProductionMethods() =>
        [.. ProductionTypes().SelectMany(t => t.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly))];

    /// <summary>
    /// Excludes closures, iterator state machines and records' synthesised members.
    /// </summary>
    /// <remarks>
    /// Not cosmetic: a lambda capturing a <c>ClientStatus</c> compiles to a display class with a
    /// field of that type, which the "never holds" assertion would report as a violation of a rule it
    /// is not breaking.
    /// </remarks>
    private static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false)
            || type.Name.Contains("<", StringComparison.Ordinal);

    /// <summary>Known-bad shapes, so the checks above are asserted rather than trusted.</summary>
    private sealed class Sample
    {
        public ClientStatus Held { get; set; }

        public AssignmentStatus HeldAssignment { get; set; }

        public string NotHeld { get; set; } = string.Empty;

        public static bool GatesOnStatus(ClientStatus status) => status == ClientStatus.Active;

        public static bool GatesOnAssignmentStatus(AssignmentStatus status) =>
            status == AssignmentStatus.Active;
    }
}
