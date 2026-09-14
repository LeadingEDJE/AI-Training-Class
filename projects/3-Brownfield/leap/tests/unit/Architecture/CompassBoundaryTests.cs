using System.Reflection;
using System.Text.RegularExpressions;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Data;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Enforces the Compass module boundary that <c>docs/platform/adding-a-module.md</c> §6 documents in
/// prose: a module reaches another module's data through an interface, never by injecting the shared
/// data context, and nothing outside a module references its internals except the composition root.
/// </summary>
/// <remarks>
/// <para>
/// Why this is worth a test: five developers begin building Compass on a boundary that is otherwise
/// maintained by discipline alone. Within a week of parallel work an undocumented rule is a suggestion.
/// This makes a crossing a FAILING BUILD, with a message naming the offending file or type.
/// </para>
/// <para>
/// Why the check is scoped to Compass and not to every module. The directory entities
/// (<c>Person</c>, <c>Employee</c>, …) deliberately stay in the Timesheet module by owner decision, so
/// two arrangements legitimately reach across module lines today: the out-of-office module's
/// <c>OotoDirectoryService</c> reads employees straight out of <c>Modules.Timesheet</c>, and the platform
/// layer references that module because every concrete repository landed under <c>Platform/</c> during
/// the restructure. Both are accepted, documented in §12 of the playbook, and explicitly not to be
/// refactored. A general "no module reaches another module" check would therefore be RED on arrival —
/// and a gate that is red the day it is written gets suppressed, which is worse than no gate.
/// <see cref="AcceptedCrossModuleReads_AreCountedAndNotFlagged"/> is the positive control that defends
/// this scoping with a measurement rather than with an argument.
/// </para>
/// <para>
/// Why a source scan complements reflection. Reflection over declared signatures catches the
/// injection cases that matter — a context handed to a service, a foreign entity on a public surface —
/// but it cannot see a type used only inside a method body, and it cannot see a <c>using</c> that has
/// not yet produced a signature. The file scan closes that gap. It resolves the repository root by
/// walking up from the test assembly and FAILS LOUDLY if it cannot find it, because a path-based check
/// that resolves no files passes — the fail-open shape this repository has produced repeatedly.
/// </para>
/// </remarks>
public class CompassBoundaryTests
{
    private const string CompassNamespace = "LeadingEDJE.Leap.Api.Modules.Compass";

    /// <summary>
    /// The PUBLISHED contract namespace — the one segment of Compass a consumer outside the module may
    /// name (spec 013, #451). Everything else under <see cref="CompassNamespace"/> stays internal.
    /// </summary>
    private const string CompassContractsNamespace = CompassNamespace + ".Contracts";

    // The token a source file uses when it reaches into another module, whether via a `using` or via
    // full qualification. `Modules.Compass` / `Modules.Timesheet` cannot be produced by a role string
    // or a comment mentioning the word "Compass", so the scan does not fire on prose.
    // CompassReferenceToken is `internal` rather than `private` so
    // CompassContractsExemptionParityTests can assert this detector and
    // DirectoryConsumerBoundaryTests' agree. The three detectors anchor on three different strings
    // and cannot share one literal, so nothing else stops one being loosened alone (spec 013, #451).
    internal const string CompassReferenceToken = "Modules.Compass";
    private const string TimesheetReferenceToken = "Modules.Timesheet";

    /// <summary>
    /// C#'s <c>identifier_part_character</c> set (ECMA-334 §6.4.3). Kept in step with
    /// <c>DirectoryConsumerBoundaryTests</c>' copy by <c>CompassContractsExemptionParityTests</c>.
    /// </summary>
    /// <remarks>
    /// <c>\b</c> is NOT used: its word set admits only U+200C/U+200D, so any other format character
    /// would split <c>Contracts</c> from a following segment and silently widen the exemption below.
    /// </remarks>
    private const string CSharpIdentifierPart = @"[\p{L}\p{Nl}\p{Nd}\p{Mn}\p{Mc}\p{Pc}\p{Cf}]";

    /// <summary>
    /// A reference to a Compass module internal, anchored on the short
    /// <see cref="CompassReferenceToken"/>, exempting the published contract segment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This was a <c>String.Contains</c> call until spec 013 (#451). A negative lookahead is a
    /// regex construct and cannot be applied to <c>String.Contains</c>, so admitting the published
    /// contract required the conversion. The tempting alternative was tested and is broken:
    /// stripping <c>Modules.Compass.Contracts</c> out of the text and then calling <c>Contains</c>
    /// turns <c>…Modules.Compass.ContractsInternal</c> into <c>…Api.Internal</c>, which no longer
    /// contains the anchor — a real violation would stop being flagged.
    /// </para>
    /// <para>
    /// The anchor stays the SHORT token, deliberately. It is broader than
    /// <c>DirectoryConsumerBoundaryTests</c>' full-namespace anchor and catches shapes that one misses;
    /// narrowing it to match would silently weaken this gate.
    /// </para>
    /// </remarks>
    private static readonly Regex CompassInternalReference = new(
        $@"Modules\.Compass(?!\.Contracts(?!{CSharpIdentifierPart}))",
        RegexOptions.Compiled);

    /// <summary>
    /// Does this file text reach into a Compass module internal? The single decision rule one's
    /// source-level variant makes, extracted so it can be asserted directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Extracted because rule one's file sweep matches ZERO non-exempt files. Measured: of 417
    /// production files under <c>api/</c> outside Compass, only <c>api/Program.cs</c> names the
    /// namespace, and it is excused. A detector that matches nothing cannot fail, so the only way to
    /// verify its behaviour is to call it with synthetic text — which requires it to be callable
    /// (spec 013, #451).
    /// </para>
    /// </remarks>
    internal static bool TextReferencesCompass(string text)
        => CompassInternalReference.IsMatch(text);

    // Any SIBLING module, not just Timesheet. Scoping rule three to the Timesheet token alone would
    // let a Compass file reach into `Modules.Ooto` unnoticed, which is the same boundary crossing
    // wearing a different name.
    private static readonly System.Text.RegularExpressions.Regex SiblingModuleReference =
        new(@"Modules\.(?!Compass\b)(?<module>[A-Z][A-Za-z0-9_]*)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // The composition root is the ONE sanctioned entry point into a module from outside it: it wires the
    // registration extensions and maps the endpoints. Everything else must go through the module's own
    // interfaces.
    private const string CompositionRootFile = "api/Program.cs";
    private const string CompositionRootTypeName = "Program";

    // Relative to the repository root. Changing either of these to a path that does not exist must FAIL
    // the run, not silently inspect nothing — see NonVacuity_* below.
    private const string CompassModuleDirectory = "api/Modules/Compass";
    private const string ApiDirectory = "api";

    /// <summary>
    /// The ONLY Compass files permitted to reference the Timesheet module, each named with the reason.
    /// </summary>
    /// <remarks>
    /// These three form one vertical slice — interface, implementation, projection — and they cross for
    /// a single reason: the directory entities <c>Employee</c> and <c>Person</c> live in the Timesheet
    /// module by owner decision and Compass reads them there. The exception is bounded by naming the
    /// files rather than granted generally, so a FOURTH Compass file quietly acquiring a cross-module
    /// reference is a failure. When the directory relocation eventually happens, these three entries are
    /// deleted and the rule becomes absolute.
    /// </remarks>
    /// <summary>
    /// Files inside Compass permitted to reference a sibling module, each with its reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EMPTY since feature 003, and that is the finished state. It previously held three
    /// entries — the directory interface, repository, and service — because the boundary resolved
    /// against the Timesheet-owned <c>Employee</c> and <c>Person</c> in <c>public.employees</c>.
    /// Re-pointing it at <c>compass.employee</c> removed every one of Compass's cross-module reads,
    /// and rule three's stale-entry assertion is what forced them to be deleted rather than left
    /// behind as permanent permission.
    /// </para>
    /// <para>
    /// Do not add an entry to "make a build pass." Compass owns the directory now; a Compass
    /// file reaching into a sibling module is a design error, not an exception to be documented. If
    /// you believe you need one, the answer is almost certainly a method on the directory contract.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> AllowedTimesheetReads =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // ------------------------------------------------------------------ Rule one

    /// <summary>
    /// Rule one — nothing outside the Compass module reaches into it, except the composition root.
    /// </summary>
    [Fact]
    public void RuleOne_NoProductionTypeOutsideCompass_ReferencesACompassType()
    {
        // Arrange
        var outsideTypes = ProductionTypes()
            .Where(t => !IsInCompass(t))
            .Where(t => !IsCompositionRoot(t))
            .ToList();

        // Act
        var violations = outsideTypes
            .Select(t => new
            {
                Type = t,
                // IsCompassInternal, NOT IsInCompass — the published contract is referenceable.
                // The `outsideTypes` filter above deliberately still uses IsInCompass; see that
                // predicate's remarks for why conflating the two inverts this gate.
                Referenced = DeclaredSignatureTypes(t)
                    .Where(IsCompassInternal)
                    .Select(r => r.FullName ?? r.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
            })
            .Where(x => x.Referenced.Count > 0)
            .ToList();

        // Assert
        outsideTypes.Count.ShouldBeGreaterThan(
            0,
            "the check inspected ZERO production types outside the Compass module, so it can only pass "
                + "vacuously — the assembly or the namespace filter is wrong");

        violations.ShouldBeEmpty(
            "only the composition root may reference Compass internals from outside the module; "
                + "everything else goes through the module's own interfaces. Offenders: "
                + string.Join(
                    "; ",
                    violations.Select(v => $"{v.Type.FullName} -> {string.Join(", ", v.Referenced)}")));
    }

    /// <summary>
    /// Rule one, source-level — the same rule over files, so a <c>using</c> or a body-only reference is
    /// caught as well as a declared signature.
    /// </summary>
    [Fact]
    public void RuleOne_NoProductionFileOutsideCompass_ReferencesTheCompassNamespace()
    {
        // Arrange
        var outsideFiles = ProductionSourceFiles(ApiDirectory)
            .Where(f => !f.RelativePath.StartsWith(CompassModuleDirectory + "/", StringComparison.Ordinal))
            .ToList();

        // Act
        var violations = outsideFiles
            .Where(f => f.RelativePath != CompositionRootFile)
            .Where(f => !IsEfGeneratedMigrationArtifact(f.RelativePath))
            .Where(f => TextReferencesCompass(f.Text))
            .Select(f => f.RelativePath)
            .ToList();

        // Assert
        outsideFiles.Count.ShouldBeGreaterThan(
            0,
            "the check inspected ZERO production files outside the Compass module — the repository root "
                + "or the API directory is wrong, and a check that reads nothing passes for free");

        violations.ShouldBeEmpty(
            $"only {CompositionRootFile} — the registration seam — may name the Compass namespace from "
                + "outside the module. Offenders: " + string.Join(", ", violations));
    }

    // ------------------------------------------------------------------ Rule two

    /// <summary>
    /// Rule two — the Compass endpoint and service layers touch no data context. They depend on the
    /// module's own interfaces. This is the habit the boundary exists to prevent, and the playbook
    /// already asserts it informally; here it is mechanical.
    /// </summary>
    [Fact]
    public void RuleTwo_CompassEndpointsAndServices_ReferenceNoDataContext()
    {
        // Arrange
        var layerTypes = ProductionTypes()
            .Where(IsInCompass)
            .Where(t => IsInCompassLayer(t, "Endpoints") || IsInCompassLayer(t, "Services"))
            .ToList();

        // Act — declared signatures first.
        var signatureViolations = layerTypes
            .Select(t => new
            {
                Type = t,
                Contexts = DeclaredSignatureTypes(t)
                    .Where(IsDataContext)
                    .Select(c => c.FullName ?? c.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
            })
            .Where(x => x.Contexts.Count > 0)
            .ToList();

        // Act — then the source, which also sees a context used only inside a method body.
        var layerFiles = ProductionSourceFiles(CompassModuleDirectory)
            .Where(f => f.RelativePath.Contains("/Endpoints/", StringComparison.Ordinal)
                || f.RelativePath.Contains("/Services/", StringComparison.Ordinal))
            .ToList();

        var sourceViolations = layerFiles
            .Where(f => f.Text.Contains("DbContext", StringComparison.Ordinal))
            .Select(f => f.RelativePath)
            .ToList();

        // Assert
        layerTypes.Count.ShouldBeGreaterThan(
            0,
            "the check inspected ZERO Compass endpoint or service types — the namespace layout changed "
                + "and this rule is now checking nothing");
        layerFiles.Count.ShouldBeGreaterThan(
            0,
            "the check inspected ZERO Compass endpoint or service FILES — the module folder layout "
                + "changed and this rule is now reading nothing");

        signatureViolations.ShouldBeEmpty(
            "a Compass endpoint or service must depend on the module's interfaces, never on a data "
                + "context. Offenders: "
                + string.Join(
                    "; ",
                    signatureViolations.Select(v =>
                        $"{v.Type.FullName} -> {string.Join(", ", v.Contexts)}")));

        sourceViolations.ShouldBeEmpty(
            "a Compass endpoint or service file names a data context; data access belongs in the "
                + "repository layer. Offenders: " + string.Join(", ", sourceViolations));
    }

    // ------------------------------------------------------------------ Rule three

    /// <summary>
    /// Rule three — the module's cross-boundary reads are EXACTLY the allowlisted ones. A fourth file
    /// acquiring a cross-module reference fails; an allowlist entry whose file stops crossing also
    /// fails, so the list cannot rot into permanent permission.
    /// </summary>
    [Fact]
    public void RuleThree_CompassCrossModuleReads_AreExactlyTheAllowlistedFiles()
    {
        // Arrange
        var moduleFiles = ProductionSourceFiles(CompassModuleDirectory);

        // Act — any SIBLING module, not just Timesheet: reaching into Ooto is the same crossing.
        var crossingWithModule = moduleFiles
            .Select(f => new
            {
                f.RelativePath,
                Modules = SiblingModuleReference.Matches(f.Text)
                    .Select(m => m.Groups["module"].Value)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(m => m, StringComparer.Ordinal)
                    .ToList(),
            })
            .Where(x => x.Modules.Count > 0)
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToList();

        var crossing = crossingWithModule.Select(x => x.RelativePath).ToList();

        var unexpected = crossingWithModule
            .Where(x => !AllowedTimesheetReads.ContainsKey(x.RelativePath))
            .Select(x => $"{x.RelativePath} -> Modules.{string.Join("/", x.Modules)}")
            .ToList();
        var staleAllowlistEntries = AllowedTimesheetReads.Keys
            .Where(p => !crossing.Contains(p, StringComparer.Ordinal))
            .ToList();

        // Assert
        moduleFiles.Count.ShouldBeGreaterThan(
            0,
            $"the check inspected ZERO files under {CompassModuleDirectory} — it found nothing to "
                + "inspect and would have passed for free");

        unexpected.ShouldBeEmpty(
            "a Compass file reaches into a SIBLING MODULE without being on the named allowlist. The "
                + "allowlist is bounded on purpose: the directory entities live in the Timesheet "
                + "module by owner decision, so exactly the interface/repository/projection slice may "
                + "cross, and only into Timesheet. Any other module is not accepted at all. Route the "
                + "read through ICompassDirectoryRepository, or — if this genuinely is the directory "
                + "read — add the file to AllowedTimesheetReads WITH a written reason. Offenders: "
                + string.Join(", ", unexpected));

        staleAllowlistEntries.ShouldBeEmpty(
            "an allowlist entry no longer crosses the boundary; delete it so the exception stays as "
                + "small as the code requires. Stale entries: "
                + string.Join(", ", staleAllowlistEntries));
    }

    // ------------------------------------------------------------------ Rule four (non-vacuity)

    /// <summary>
    /// Rule four — the check cannot pass vacuously. Every rule above carries its own non-zero guard;
    /// this test states the invariant once, in one place, so the guard is visible rather than buried.
    /// </summary>
    [Fact]
    public void NonVacuity_TheCheckInspectsRealFilesAndRealTypes()
    {
        // Arrange / Act
        var moduleFiles = ProductionSourceFiles(CompassModuleDirectory);
        var apiFiles = ProductionSourceFiles(ApiDirectory);
        var productionTypes = ProductionTypes().ToList();
        var compassTypes = productionTypes.Where(IsInCompass).ToList();

        // Assert
        moduleFiles.Count.ShouldBeGreaterThan(
            0, "the Compass boundary check found NOTHING to inspect under " + CompassModuleDirectory);
        apiFiles.Count.ShouldBeGreaterThan(
            0, "the Compass boundary check found NOTHING to inspect under " + ApiDirectory);
        productionTypes.Count.ShouldBeGreaterThan(
            0, "the Compass boundary check loaded NO production types");
        compassTypes.Count.ShouldBeGreaterThan(
            0, "the Compass boundary check found NO types in the Compass namespace");
    }

    /// <summary>
    /// Rule four — repository-root resolution fails LOUDLY. A path-based check that cannot find the
    /// repository must not degrade into inspecting an empty set.
    /// </summary>
    [Fact]
    public void NonVacuity_MissingDirectory_ThrowsRatherThanInspectingNothing()
    {
        // Arrange / Act / Assert — a directory that does not exist is an error, never an empty result.
        var thrown = Should.Throw<DirectoryNotFoundException>(
            () => ProductionSourceFiles("api/Modules/ThisModuleDoesNotExist"));

        thrown.Message.ShouldContain(
            "found nothing to inspect",
            Case.Insensitive,
            "the failure must say the check found nothing, so the fail-open shape is unmistakable");
    }

    // ------------------------------- the published-contract exemption (#451)

    /// <summary>
    /// The published contract namespace is referenceable from outside the module (spec 013, #451).
    /// </summary>
    /// <remarks>
    /// <para>
    /// These controls must agree, case for case, with
    /// <c>DirectoryConsumerBoundaryTests</c>' equivalents. The two detectors anchor on DIFFERENT
    /// strings — this one on the short <see cref="CompassReferenceToken"/>, the other on the full
    /// namespace — so they cannot share one literal, and nothing but
    /// <c>CompassContractsExemptionParityTests</c> stops one being loosened alone.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSourceDetector_DoesNotFireOnThePublishedContractNamespace()
    {
        // Arrange — the same allow-shapes the sibling detector's controls use.
        var allowed = new[]
        {
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;",
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;\r\n\r\n"
                + "namespace LeadingEDJE.Leap.Api.Modules.Ooto;\r\n\r\n"
                + "internal sealed class Consumer(IDirectory directory);\r\n",
            "private readonly LeadingEDJE.Leap.Api.Modules.Compass.Contracts.IDirectory _directory;",
            "using Dir = LeadingEDJE.Leap.Api.Modules.Compass.Contracts.IDirectory;",
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;\r\n"
                + "// see LeadingEDJE.Leap.Api.Modules.Compass.Contracts.CompassEmployeeDto\r\n",
        };

        // Act & Assert
        foreach (var text in allowed)
        {
            TextReferencesCompass(text).ShouldBeFalse(
                "the published contract namespace must be referenceable from outside Compass, or a "
                    + $"consumer cannot obey Principle V. Fired on: {text}");
        }
    }

    /// <summary>Every legal terminator after <c>Contracts</c> keeps the exemption.</summary>
    [Fact]
    public void TheSourceDetector_DoesNotFireOnThePublishedContract_AfterAnyLegalTerminator()
    {
        // Arrange
        const string Reference = "LeadingEDJE.Leap.Api.Modules.Compass.Contracts";
        var terminators = new[] { ";", ".", ")", ",", ">", "=", "{", " ", string.Empty, "\r\n" };

        // Act & Assert
        foreach (var terminator in terminators)
        {
            TextReferencesCompass(Reference + terminator).ShouldBeFalse(
                $"the exemption must survive every legal terminator. Fired on: '{terminator}'");
        }
    }

    /// <summary>
    /// Widening for the published contract must not admit an internal, a longer word, or a
    /// format-character split.
    /// </summary>
    [Fact]
    public void TheSourceDetector_StillFiresOnEveryCompassInternalNamespace()
    {
        // Arrange
        var offending = new[]
        {
            "using LeadingEDJE.Leap.Api.Modules.Compass.Services;",
            "using LeadingEDJE.Leap.Api.Modules.Compass.Data;",
            "using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;",
            "using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;",
            "using LeadingEDJE.Leap.Api.Modules.Compass.Endpoints;",
            "using LeadingEDJE.Leap.Api.Modules.Compass;",
            "new LeadingEDJE.Leap.Api.Modules.Compass.Employee();",
            "global::LeadingEDJE.Leap.Api.Modules.Compass.Services.CompassDirectoryService x;",
            "using Svc = LeadingEDJE.Leap.Api.Modules.Compass.Services.CompassDirectoryService;",
            "List<LeadingEDJE.Leap.Api.Modules.Compass.Employee> employees;",
            "Type.GetType(\"LeadingEDJE.Leap.Api.Modules.Compass.Services.CompassDirectoryService\");",
            "/// <see cref=\"LeadingEDJE.Leap.Api.Modules.Compass.Services.CompassDirectoryService\"/>",
            "using LeadingEDJE.Leap.Api.Modules.Compass.ContractsInternal;",
            "using LeadingEDJE.Leap.Api.Modules.Compass.ContractsFoo;",
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts­Internal;",
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;\r\n"
                + "using LeadingEDJE.Leap.Api.Modules.Compass.Services;\r\n",
        };

        // Act & Assert
        foreach (var text in offending)
        {
            TextReferencesCompass(text).ShouldBeTrue(
                $"the exemption is the exact `Contracts` segment and nothing more. Missed: {text}");
        }
    }

    /// <summary>
    /// <c>IDirectory</c>'s own signature closure lives in the published contract namespace, and
    /// nowhere else (spec 013 FR-001/FR-002, #451).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted against the CLOSURE, never against the word "published" — that distinction is
    /// load-bearing. The committed <c>compass-v1.openapi.json</c> declares 16 schemas and only
    /// these 8 move; <c>CompassClientRequest</c>, <c>CompassEdjerRequest</c>, <c>CompassSowRequest</c>,
    /// the four lookup/category requests and <c>SowType</c> are equally *published* and deliberately
    /// stay put. A test worded "no published type remains in <c>Dtos/</c>" is red before AND after and
    /// can never go green.
    /// </para>
    /// <para>
    /// Types are located by simple name rather than by <c>typeof</c> so this assertion compiles — and
    /// therefore fails honestly — before the move as well as after it.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePublishedContractClosure_LivesInTheContractsNamespace()
    {
        // Arrange — IDirectory's 11 method signatures reach exactly these types, measured, not chosen.
        // The three most recent additions introduce no new type: the two email-keyed employee reads
        // return CompassEmployeeDto, and GetClientsAsync returns CompassClientDto — both listed here.
        var closure = new[]
        {
            "IDirectory",
            "CompassEmployeeDto",
            "CompassEmployeeCoachDto",
            "CompassEmployeeTimeTrackingDto",
            "CompassClientDto",
            "CompassAssignmentDto",
            "CompassInvoiceFrequencyDto",
            "CompassBillableCategoryDto",
            "CompassDirectorySowDto",
        };
        var types = ProductionTypes().ToList();

        // Act & Assert
        types.Count.ShouldBeGreaterThan(
            0, "reflected over ZERO production types — this assertion would pass for free");

        foreach (var name in closure)
        {
            var matches = types.Where(t => string.Equals(t.Name, name, StringComparison.Ordinal)).ToList();

            matches.Count.ShouldBe(
                1,
                $"expected exactly one production type named {name}; found {matches.Count}. A second "
                    + "type of the same name would make this assertion ambiguous rather than wrong");

            matches[0].Namespace.ShouldBe(
                CompassContractsNamespace,
                $"{name} is part of IDirectory's signature closure, so a consumer outside Compass must "
                    + "be able to name it. It has to live in the published contract namespace, not in "
                    + "Compass internals (FR-001/FR-002)");
        }
    }

    /// <summary>
    /// The reflection detector's exemption: a published contract type on a declared surface is
    /// allowed; a Compass internal is not (spec 013, #451).
    /// </summary>
    /// <remarks>
    /// <para>
    /// These exercise <see cref="IsCompassInternal"/> directly rather than through a planted file,
    /// because reflection sees the API assembly only — a synthetic type in this test project is
    /// invisible to rule one, which is exactly why detector 3 could not be fixed test-first ahead of
    /// the move.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReflectionDetector_TreatsPublishedContractTypesAsReferenceable()
    {
        // Arrange — the published closure, located by name so the test does not hard-code a namespace
        // it is itself asserting.
        var published = new[]
        {
            "IDirectory", "CompassEmployeeDto", "CompassClientDto", "CompassAssignmentDto",
            "CompassInvoiceFrequencyDto", "CompassBillableCategoryDto", "CompassDirectorySowDto",
        };
        var types = ProductionTypes().ToList();

        // Act & Assert
        types.Count.ShouldBeGreaterThan(0, "reflected over ZERO types — would pass for free");

        foreach (var name in published)
        {
            var type = types.Single(t => string.Equals(t.Name, name, StringComparison.Ordinal));

            IsCompassInternal(type).ShouldBeFalse(
                $"{name} is part of the published contract, so a consumer outside Compass must be "
                    + "able to hold it on a declared surface — that is the whole purpose of the seam");
        }
    }

    /// <summary>
    /// Compass internals stay unreferenceable from outside the module (Contract B, C6/C7).
    /// </summary>
    /// <remarks>
    /// <c>Employee</c> covers C6 (a Compass entity); <c>ICompassDirectoryRepository</c> covers C7 (a
    /// repository interface that stays in <c>Interfaces/</c>). <see cref="IsCompassInternal"/> does
    /// not distinguish a ctor parameter from a property from a method-return type — that unwrapping
    /// happens once, in <see cref="DeclaredSignatureTypes"/>, before this predicate ever runs — so
    /// asserting the predicate directly on the named type covers every declared-surface shape the
    /// contract lists for it.
    /// </remarks>
    [Fact]
    public void TheReflectionDetector_StillTreatsCompassInternalsAsForbidden()
    {
        // Arrange — a service, a repository interface, an entity, and a lookup that all STAY internal.
        var internals = new[]
        {
            "CompassDirectoryService",
            "ICompassDirectoryRepository",
            "CompassEdjerDto",
            "ClientAssignment",
            "Employee",
        };
        var types = ProductionTypes().ToList();

        // Act & Assert
        foreach (var name in internals)
        {
            var matches = types
                .Where(t => string.Equals(t.Name, name, StringComparison.Ordinal) && IsInCompass(t))
                .ToList();

            matches.ShouldNotBeEmpty(
                $"expected a Compass type named {name} to exist; if it was renamed or moved, this "
                    + "control is measuring nothing and must be updated rather than deleted");

            IsCompassInternal(matches[0]).ShouldBeTrue(
                $"{name} is a Compass internal. Widening the gate for the published contract must not "
                    + "make internals referenceable from outside the module");
        }
    }

    /// <summary>
    /// A Compass entity is still flagged even when an unrelated <c>LeapDbContext</c> reference sits on
    /// the same declared surface (Contract B, C5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// What this closes. Rule one's reflection variant collects EVERY declared-surface type
    /// (<see cref="DeclaredSignatureTypes"/>: base type, interfaces, ctor parameters, fields,
    /// properties, method parameters and returns) into one flat set before filtering it with
    /// <see cref="IsCompassInternal"/>. An unrelated member — <c>LeapDbContext</c>, which has nothing
    /// to do with the Compass boundary — sitting beside a Compass entity on that same surface must not
    /// suppress the flag on the entity, the reflection form of B16's "a published import can never
    /// launder the rest of the file."
    /// </para>
    /// <para>
    /// <c>LeapDbContext</c> is deliberately NOT itself expected to be flagged: it is not a Compass
    /// type, so <see cref="IsInCompass"/> already excludes it before <see cref="IsCompassInternal"/>
    /// runs — this test asserts that exclusion holds too, not just the entity's inclusion.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReflectionDetector_FlagsACompassEntity_AlongsideAnUnrelatedLeapDbContextReference()
    {
        // Arrange
        var types = ProductionTypes().ToList();
        types.Count.ShouldBeGreaterThan(0, "reflected over ZERO types — would pass for free");

        var employee = types.Single(t =>
            string.Equals(t.Name, "Employee", StringComparison.Ordinal) && IsInCompass(t));
        var declaredSurface = new[] { typeof(LeapDbContext), employee };

        // Act
        var flagged = declaredSurface.Where(IsCompassInternal).ToList();

        // Assert
        flagged.Count.ShouldBe(
            1,
            "a Compass entity must still be flagged when an unrelated LeapDbContext reference sits "
                + "on the same declared surface as it (C5). Flagged: "
                + string.Join(", ", flagged.Select(t => t.FullName)));
        flagged[0].ShouldBe(
            employee,
            "the unrelated LeapDbContext reference must not itself be flagged — it is not a Compass "
                + "type at all, and IsInCompass already excludes it before IsCompassInternal runs");
    }

    /// <summary>
    /// The published contract types are STILL classified as inside Compass for the purpose of choosing
    /// which types rule one scans.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the trap, asserted. The obvious way to implement the exemption is to edit
    /// <see cref="IsInCompass"/> — but rule one calls it twice, and the first call decides which types
    /// are outside the module. Exempting <c>Contracts</c> there would push the 7 contract types
    /// into the scanned set and invert the gate. If someone later "simplifies" the two predicates into
    /// one, this fails.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePublishedContractTypes_AreStillInsideCompass_ForScopeSelection()
    {
        // Arrange
        var types = ProductionTypes().ToList();
        var contractTypes = types.Where(IsPublishedContract).ToList();

        // Act & Assert
        contractTypes.Count.ShouldBeGreaterThan(
            0,
            "found ZERO published contract types — either the move regressed or this control is "
                + "measuring nothing");

        foreach (var type in contractTypes)
        {
            IsInCompass(type).ShouldBeTrue(
                $"{type.Name} must still count as INSIDE the Compass module. IsInCompass selects which "
                    + "types rule one treats as outside the module; if the Contracts exemption leaks "
                    + "into it, the contract types become scan targets and the gate inverts");
        }
    }

    /// <summary>
    /// A namespace that merely BEGINS with <c>Contracts</c> is not the published contract (Contract B,
    /// C8 — the reflection form of B13/B14).
    /// </summary>
    [Fact]
    public void ThePublishedContractPredicate_RejectsALongerFirstSegment()
    {
        // Arrange — the reflection form of the text detectors' same-word-prefix trap. Asserted via
        // IsPublishedContractNamespace (the real predicate's string-only core) against a hypothetical
        // namespace, because no such namespace exists in production (and must not).
        const string Naive = CompassContractsNamespace;
        var hostile = CompassContractsNamespace + "Internal";

        // Act & Assert
        hostile.StartsWith(Naive, StringComparison.Ordinal).ShouldBeTrue(
            "sanity: the naive prefix form DOES match the hostile namespace — which is why "
                + nameof(IsPublishedContract) + " appends a dot before comparing");

        IsPublishedContractNamespace(hostile).ShouldBeFalse(
            "…Compass.ContractsInternal is a Compass internal, not the published contract. The "
                + "dot-suffixed comparison is what keeps it forbidden");
    }

    /// <summary>
    /// The published contract folder is a FLAT directory on disk — no subdirectory (Contract C, D1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only defence against the text exemption becoming a side door. Both text detectors
    /// exempt any reference whose next segment is exactly <c>Contracts</c>, including anything nested
    /// beneath it — no regex can separate a nested namespace from member access, since
    /// <c>…Contracts.IDirectory.Foo()</c> must stay allowed (<see cref="ThePublishedContractNamespace_HasNoSubNamespace"/>
    /// asserts the same invariant over reflected TYPES; this asserts it over the actual FOLDER, which
    /// is what a reviewer or a future PR actually touches).
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePublishedContractFolder_ContainsNoSubdirectory()
    {
        // Arrange
        var root = RepositoryRoot();
        var contractsDirectory = Path.Combine(root, "api", "Modules", "Compass", "Contracts");

        if (!Directory.Exists(contractsDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The Compass boundary check found nothing to inspect: '{contractsDirectory}' does not "
                    + "exist. A path-based check that resolves no directory PASSES, which is the "
                    + "fail-open shape this check exists to avoid.");
        }

        // Act
        var subdirectories = Directory.GetDirectories(contractsDirectory);

        // Assert
        subdirectories.ShouldBeEmpty(
            "api/Modules/Compass/Contracts/ must stay flat. A subdirectory beneath it would be "
                + "referenceable from outside Compass without any gate objecting to it, because both "
                + "text detectors exempt anything whose next segment is `Contracts`. Offenders: "
                + string.Join(", ", subdirectories));
    }

    /// <summary>
    /// The published contract namespace is a LEAF — no sub-namespace beneath it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only defence against the text detectors' exemption becoming a side door.
    /// Both exempt any reference whose next segment is exactly <c>Contracts</c>, and that necessarily
    /// includes anything nested beneath it, because no regex can distinguish a nested namespace from
    /// member access (<c>…Contracts.IDirectory.GetEmployeeAsync</c> must stay allowed). Structure is
    /// the only thing that can close it. Measured today: zero sub-namespaces, so this passes on
    /// arrival and turns a future nesting into a build failure rather than a silent widening.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePublishedContractNamespace_HasNoSubNamespace()
    {
        // Arrange
        var types = ProductionTypes().ToList();
        var prefix = CompassContractsNamespace + ".";

        // Act
        var nested = types
            .Where(t => t.Namespace is not null
                && t.Namespace.StartsWith(prefix, StringComparison.Ordinal))
            .Select(t => t.Namespace!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // Assert
        types.Count.ShouldBeGreaterThan(
            0, "reflected over ZERO production types — this assertion would pass for free");

        nested.ShouldBeEmpty(
            "the published contract namespace must stay flat. Both text detectors exempt anything "
                + "whose next segment is `Contracts`, INCLUDING nested namespaces, so a sub-namespace "
                + "here would be referenceable from outside Compass without any gate objecting. Put "
                + "the type in Compass internals instead. Offenders: " + string.Join(", ", nested));
    }

    // ------------------------------------------------------------------ Positive control

    /// <summary>
    /// Positive control — the ACCEPTED platform→Timesheet arrangement is present, counted, and NOT
    /// flagged by any rule above.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The platform layer references the Timesheet module because the concrete repositories landed
    /// under <c>Platform/</c> during the restructure. It is accepted by owner decision: the directory
    /// entities deliberately stay where they are, and the eventual relocation is a LATER timesheet
    /// change — not this check's business. A boundary check that failed on it would have been noise.
    /// </para>
    /// <para>
    /// The out-of-office module USED to cross here too. Issue #428 retired the last of it —
    /// <c>OotoService.ResolveLegacyRowKeyAsync</c> is gone with the legacy
    /// <c>ooto.out_of_office_events.employee_id</c> — so no OOTO file names the Timesheet module any
    /// more, and this control is scoped to the platform crossing alone.
    /// </para>
    /// <para>
    /// The count assertion is what stops this control rotting into a tautology. If the platform read
    /// were ever refactored away, "the check does not flag it" would become trivially true; asserting
    /// the count is non-zero makes that situation fail loudly instead, at which point the check's scope
    /// should be widened.
    /// </para>
    /// </remarks>
    [Fact]
    public void AcceptedCrossModuleReads_AreCountedAndNotFlagged()
    {
        // Arrange — #428 retired the OOTO crossing, so the platform layer is the only accepted one left.
        var platformReads = ProductionSourceFiles("api/Platform")
            .Where(f => f.Text.Contains(TimesheetReferenceToken, StringComparison.Ordinal))
            .Select(f => f.RelativePath)
            .ToList();

        // Assert — the accepted arrangement still exists, so this control is still proving something.
        platformReads.Count.ShouldBeGreaterThan(
            0,
            "the platform layer no longer references the Timesheet module. That is a real change: widen "
                + "the check's scope rather than deleting the control");

        // The out-of-office module must NOT cross into Timesheet — #428 retired that read entirely, and
        // a new one is a regression this control catches. The module itself is gone now (not merely
        // decoupled), so there is no directory to scan; guard the lookup rather than let the
        // fail-open protection in ProductionSourceFiles reject the whole test on a directory that is
        // correctly absent.
        var ootoModuleDirectory = Path.Combine(
            RepositoryRoot(), "api/Modules/Ooto".Replace('/', Path.DirectorySeparatorChar));
        var ootoReads = Directory.Exists(ootoModuleDirectory)
            ? ProductionSourceFiles("api/Modules/Ooto")
                .Where(f => f.Text.Contains(TimesheetReferenceToken, StringComparison.Ordinal))
                .Select(f => f.RelativePath)
                .ToList()
            : [];
        ootoReads.ShouldBeEmpty(
            "an out-of-office file names the Timesheet module. #428 retired OotoService's legacy "
                + "directory read; a fresh crossing is a regression. Offenders: "
                + string.Join(", ", ootoReads));

        // Act — run the rules' own violation collectors over the accepted files. The EF-generated
        // migration artifacts are exempted on the same grounds as in rule one: one context means one
        // model snapshot, and dotnet ef writes every entity's qualified type name into it.
        var flaggedByRuleOne = platformReads
            .Where(p => p != CompositionRootFile)
            .Where(p => !IsEfGeneratedMigrationArtifact(p))
            // TextReferencesCompass, NOT a raw Contains(CompassReferenceToken). The raw token matches
            // the PUBLISHED contract namespace too, which Principle V requires a consumer to be able
            // to name -- and which TheSourceDetector_DoesNotFireOnThePublishedContractNamespace
            // blesses in this very file, using an OOTO consumer of IDirectory as its example.
            //
            // This control predates the spec 013 / #451 Contracts exemption and had never met a real
            // consumer of it, so it kept passing on the strength of there being none. Feature 018 and
            // issue #430 both made OotoDirectoryService that consumer -- once for the timezone read
            // and once for clients and assignments -- and the control fired on legitimate code.
            // Aligning it with the detector rule one actually uses is a correction, not a relaxation:
            // every
            // Compass-INTERNAL reference is still flagged, as
            // TheSourceDetector_StillFiresOnEveryCompassInternalNamespace proves.
            .Where(p => TextReferencesCompass(ReadRepositoryFile(p)))
            .ToList();

        var flaggedByRuleThree = platformReads
            .Where(p => p.StartsWith(CompassModuleDirectory + "/", StringComparison.Ordinal))
            .ToList();

        // Assert — none of the accepted reads is a violation of any rule this class enforces.
        flaggedByRuleOne.ShouldBeEmpty(
            "an accepted cross-module read must not reach into Compass. Offenders: "
                + string.Join(", ", flaggedByRuleOne));
        flaggedByRuleThree.ShouldBeEmpty(
            "an accepted read outside the Compass module must never be evaluated by the Compass "
                + "allowlist. Offenders: " + string.Join(", ", flaggedByRuleThree));
    }

    // ------------------------------------------------------------------ Helpers

    // The composition root is `public partial class Program` in the GLOBAL namespace, plus anything the
    // compiler nests inside it (lambda closure classes, iterator state machines). Excluding by simple
    // name alone would also excuse any unrelated type that happened to be called `Program`, and would
    // NOT excuse Program's own generated closures — so walk out to the outermost declaring type and
    // require the global namespace.
    private static bool IsCompositionRoot(Type type)
    {
        var outermost = type;
        while (outermost.DeclaringType is not null)
        {
            outermost = outermost.DeclaringType;
        }

        return outermost.Namespace is null
            && string.Equals(outermost.Name, CompositionRootTypeName, StringComparison.Ordinal);
    }

    private static bool IsInCompass(Type type)
        => type.Namespace is not null
            && type.Namespace.StartsWith(CompassNamespace, StringComparison.Ordinal);

    /// <summary>
    /// A Compass type a consumer outside the module may NOT hold: inside Compass, but not part of the
    /// published contract (spec 013, #451).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Do NOT fold this exemption into <see cref="IsInCompass"/>. That predicate is called at
    /// TWO sites with TWO meanings: rule one uses it to decide which types are outside the module
    /// (and therefore worth scanning), and again to decide which references are forbidden.
    /// Exempting <c>Contracts</c> inside <c>IsInCompass</c> would change the first meaning too —
    /// reclassifying the 9 contract types as living outside Compass, subjecting them to rule one, and
    /// inverting the gate instead of relaxing it. The exemption belongs at the reference site alone.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <c>internal</c> rather than <c>private</c> so <c>CompassContractsExemptionParityTests</c> can
    /// assert detector 3's verdict against detectors 1 and 2's on the cases expressible in both forms
    /// (Contract D, E2) — the actual predicate, not a fourth re-derivation of its logic.
    /// </remarks>
    internal static bool IsCompassInternal(Type type)
        => IsInCompass(type) && !IsPublishedContract(type);

    /// <summary>
    /// Is this type part of the published contract — the one Compass namespace an outside consumer may
    /// name?
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>+ "."</c> is load-bearing. A bare
    /// <c>StartsWith(CompassContractsNamespace)</c> would also exempt a hypothetical
    /// <c>…Compass.ContractsInternal</c> — the same-word-prefix trap the text detectors guard with a
    /// C# identifier-part class. Verified: the naive form returns true for that namespace.
    /// </para>
    /// </remarks>
    private static bool IsPublishedContract(Type type) => IsPublishedContractNamespace(type.Namespace);

    /// <summary>
    /// The string-only core of <see cref="IsPublishedContract"/>, extracted so
    /// <c>CompassContractsExemptionParityTests</c> can assert D3's verdict on a hypothetical namespace
    /// that no real production type carries — <c>…Compass.ContractsInternal</c> (Contract D, E2;
    /// mirrors detector 3's B13/B14 ↔ C8 case, which has no reflectable <c>Type</c> to test against).
    /// </summary>
    internal static bool IsPublishedContractNamespace(string? ns)
        => ns is not null
            && (string.Equals(ns, CompassContractsNamespace, StringComparison.Ordinal)
                || ns.StartsWith(CompassContractsNamespace + ".", StringComparison.Ordinal));

    private static bool IsInCompassLayer(Type type, string layer)
        => type.Namespace is not null
            && (type.Namespace.EndsWith("." + layer, StringComparison.Ordinal)
                || type.Namespace.Contains("." + layer + ".", StringComparison.Ordinal));

    private static bool IsDataContext(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Name.EndsWith("DbContext", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Type> ProductionTypes()
    {
        var assembly = typeof(CompassEmployeeDto).Assembly;
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }

        return types.Where(t => t is not null).Select(t => t!);
    }

    // Declared signature surface: base type, interfaces, constructor parameters, fields, properties,
    // method parameters and return types — with generic arguments unwrapped. This is what catches an
    // injected context or a foreign entity on a public surface. It does NOT see a type used only inside
    // a method body, which is why every rule also has a source-level counterpart.
    private static IEnumerable<Type> DeclaredSignatureTypes(Type type)
    {
        const BindingFlags Declared = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        var seed = new List<Type?> { type.BaseType };
        seed.AddRange(type.GetInterfaces());

        foreach (var ctor in type.GetConstructors(Declared))
        {
            seed.AddRange(ctor.GetParameters().Select(p => p.ParameterType));
        }

        foreach (var field in type.GetFields(Declared))
        {
            seed.Add(field.FieldType);
        }

        foreach (var property in type.GetProperties(Declared))
        {
            seed.Add(property.PropertyType);
        }

        foreach (var method in type.GetMethods(Declared))
        {
            seed.Add(method.ReturnType);
            seed.AddRange(method.GetParameters().Select(p => p.ParameterType));
        }

        return seed.Where(t => t is not null).SelectMany(t => Unwrap(t!)).Distinct();
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;

        if (type.HasElementType)
        {
            var element = type.GetElementType();
            if (element is not null)
            {
                foreach (var inner in Unwrap(element))
                {
                    yield return inner;
                }
            }
        }

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var inner in Unwrap(argument))
            {
                yield return inner;
            }
        }
    }

    /// <summary>
    /// True for the two EF Core-generated migration artifacts that necessarily name every mapped
    /// entity's CLR type: the per-migration <c>*.Designer.cs</c> and <c>LeapDbContextModelSnapshot.cs</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are exempt from rule one because the reference is INHERENT to the one-context decision,
    /// not a design leak. There is one <c>LeapDbContext</c> and therefore one model snapshot, and
    /// <c>dotnet ef</c> writes the fully-qualified name of every entity type into it — including
    /// Compass's. The only ways to avoid it would be a second <c>DbContext</c> (banned outright) or
    /// hand-editing generated files after every migration (worse than the rule it would satisfy).
    /// </para>
    /// <para>
    /// The exemption is deliberately NARROW — two filename patterns under the migrations folder, and
    /// only for the source-text rule. It does not extend to the migration body itself
    /// (<c>&lt;timestamp&gt;_Name.cs</c>), which is hand-editable and contains only table-name strings;
    /// and it does not weaken the reflection-based <c>RuleOne_NoProductionType…</c>, which inspects
    /// declared signatures and passes on its own. If a hand-written Platform file ever names the
    /// Compass namespace, it is still a violation.
    /// </para>
    /// </remarks>
    private static bool IsEfGeneratedMigrationArtifact(string relativePath) =>
        relativePath.StartsWith("api/Platform/Data/Migrations/", StringComparison.Ordinal)
        && (
            relativePath.EndsWith(".Designer.cs", StringComparison.Ordinal)
            || relativePath.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal)
        );

    private sealed record SourceFile(string RelativePath, string Text);

    private static List<SourceFile> ProductionSourceFiles(string relativeDirectory)
    {
        var root = RepositoryRoot();
        var absolute = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(absolute))
        {
            throw new DirectoryNotFoundException(
                $"The Compass boundary check found nothing to inspect: '{relativeDirectory}' does not "
                    + $"exist under repository root '{root}'. A path-based check that resolves no files "
                    + "PASSES, which is the fail-open shape this check exists to avoid — fix the path "
                    + "rather than letting the check run empty.");
        }

        var files = Directory
            .EnumerateFiles(absolute, "*.cs", SearchOption.AllDirectories)
            // Build output is not production source, and it contains generated copies that would make
            // the scan report the same violation several times over.
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .Select(p => new SourceFile(
                Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(p)))
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            throw new DirectoryNotFoundException(
                $"The Compass boundary check found nothing to inspect: '{relativeDirectory}' contains "
                    + "no C# source files. An empty inspection set passes silently — treat it as a "
                    + "broken check, not as a clean result.");
        }

        return files;
    }

    private static string ReadRepositoryFile(string relativePath)
        => File.ReadAllText(
            Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    // Walks up from the test assembly until the solution file appears. Throws rather than returning a
    // best guess: a wrong root would make every file scan empty, and an empty scan passes.
    private static string RepositoryRoot()
    {
        const string SolutionFile = "leap.slnx";

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"The Compass boundary check found nothing to inspect: could not locate '{SolutionFile}' "
                + $"walking up from '{AppContext.BaseDirectory}'. Without a repository root every file "
                + "scan is empty and the check passes for free.");
    }
}
