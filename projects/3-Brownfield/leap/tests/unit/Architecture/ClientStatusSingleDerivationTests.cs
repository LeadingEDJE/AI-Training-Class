using System.Text.RegularExpressions;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Fences the single shared derivation of client status and assignment currency (AC-42, BR-11).
/// </summary>
/// <remarks>
/// <para>
/// A requirement without a mechanism is a hope. BR-11 requires status to be "computed by a
/// single shared implementation used by every consumer", and AC-42 states the reason: "independent
/// implementations would diverge on boundary cases such as an end date falling exactly today." Four
/// consumers exist or are planned — the Client Directory, the client view, the dashboard beach tile,
/// and the reports — and each could plausibly write <c>a.EndDate &gt;= today</c> itself. The two that
/// disagree would produce two screens contradicting each other about the same client, on the one case
/// nobody tests by hand.
/// </para>
/// <para>
/// So this fails the build when any production file other than <c>ClientStatusDerivation.cs</c>
/// compares an assignment's <c>EndDate</c> against a date, or constructs a <c>ClientStatus</c> value
/// it computed itself.
/// </para>
/// <para>
/// Why a source scan. The rule is about how a comparison is *written*, which reflection cannot
/// see — a method body containing <c>EndDate &gt;= today</c> has no signature to inspect. The scan
/// resolves the repository root by walking up from the test assembly and FAILS LOUDLY if it cannot
/// find it: a path-based check that resolves no files PASSES, which is the fail-open shape
/// <c>docs/platform/adding-a-module.md</c> records finding eight times across Phases 48-49.
/// </para>
/// </remarks>
public class ClientStatusSingleDerivationTests
{
    /// <summary>The one file permitted to compare an end date. Not a list, and not configurable.</summary>
    private const string TheDerivation = "api/Modules/Compass/Services/ClientStatusDerivation.cs";

    private const string CompassModuleDirectory = "api/Modules/Compass";

    /// <summary>
    /// A comparison of an <c>EndDate</c>-family field against a business date — never against
    /// a sibling date field on the same row — in either operand order (research R-1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Refined 2026-08-13 (R-1). The original expression matched <c>EndDate</c> against
    /// anything, including another date field: it tripped on <c>request.EndDate &lt;
    /// request.StartDate</c> (FR-008) and, via substring, on <c>sow.SowEndDate &gt;=
    /// sow.SowStartDate</c> (FR-020) — neither is a currency derivation, because neither compares
    /// against today. Worse, it was operand-order dependent: <c>SowEndDate &gt;= SowStartDate</c>
    /// tripped while <c>SowStartDate &gt; SowEndDate</c> did not, so the identical rule passed or
    /// failed on spelling. The docstring's invariant — no file independently decides assignment
    /// currency or client status — is about a comparison against today, not a comparison of two
    /// date fields on the same row. This expression enforces exactly that: a match requires one side to
    /// be an <c>EndDate</c>-family field (<c>EndDate</c> or <c>SowEndDate</c>) and the OTHER side to NOT
    /// be a <c>StartDate</c>/<c>EndDate</c>-family field. After the refinement, a currency comparison
    /// trips in either operand order for both <c>EndDate</c> and <c>SowEndDate</c>, and a sibling-field
    /// date-order comparison never trips in either operand order.
    /// </para>
    /// <para>
    /// Deliberately broad on the non-date-field side: the defect is comparing an end date against a
    /// business date at all outside the derivation, whatever that business date is called. Equality
    /// against <c>null</c> is excluded because "has this assignment ever been ended" is a different
    /// question from "is it current", and forbidding it would push callers toward worse workarounds.
    /// </para>
    /// <para>
    /// The <c>(?&lt;!=)</c> lookbehind is load-bearing. A C# lambda arrow <c>=&gt;</c> contains
    /// a <c>&gt;</c>, so without it every <c>x =&gt; x.EndDate</c> reads as a comparison — which
    /// flagged <c>ClientAssignmentConfiguration</c>'s perfectly innocent column mapping the first time
    /// this gate ran. A gate whose first act is a false positive gets suppressed, so this case is
    /// pinned in <see cref="NonVacuity_ThePatternsMatchWhatTheyClaimTo"/> rather than merely fixed.
    /// </para>
    /// <para>
    /// The operator alternation is wrapped in an atomic group, <c>(?&gt;...)</c>, and that is load-
    /// bearing too. Without it, when the trailing lookahead/lookbehind correctly rejects a sibling-
    /// field comparison like <c>SowEndDate &gt;= SowStartDate</c>, the engine backtracks the ALTERNATION
    /// itself — it re-matches the two-character <c>&gt;=</c> operator as the bare one-character
    /// <c>&gt;</c> alternative, which leaves a stray <c>=</c> immediately after the "operator". That
    /// stray character breaks the date-field pattern the assertion is checking for, so the assertion
    /// passes on garbage and the whole comparison wrongly trips. The atomic group commits to the first
    /// operator alternative that matches and forbids the engine from retrying a shorter one once the
    /// trailing assertion fails — found empirically: the un-atomic version passed 14 of 15 cases in
    /// <see cref="NonVacuity_ThePatternsMatchWhatTheyClaimTo"/> and silently mismatched exactly this one.
    /// </para>
    /// </remarks>
    private static readonly Regex EndDateComparison = new(
        @"EndDate\s*(?>(?:<=|>=|<|>))(?!\s*\w*\.?\w*(?:Start|End)Date\b)"
            + @"|(?<!=)(?<!(?:Start|End)Date\s*)(?>(?:<=|>=|<|>))\s*\w*\.?\w*EndDate\b",
        RegexOptions.Compiled);

    /// <summary>
    /// A file that actually USES the derivation — injects it, or names one of its members.
    /// </summary>
    /// <remarks>
    /// Matched against comment-stripped source, so the many doc comments that merely cite
    /// <c>IClientStatusDerivation</c> by name to explain a rule (four read DTOs and
    /// <c>CompassSowMigrationService</c> do) are correctly not consumers.
    /// </remarks>
    private static readonly Regex DerivationUse = new(
        @"IClientStatusDerivation",
        RegexOptions.Compiled);

    /// <summary>
    /// Every file that consumes the derivation, and therefore every surface whose agreement is
    /// asserted by <c>ClientStatusCrossSurfaceAgreementTests</c>. Ordinal-sorted by path.
    /// </summary>
    private static readonly string[] KnownConsumers =
    [
        "api/Modules/Compass/Data/Repositories/CompassAssignmentRepository.cs",
        "api/Modules/Compass/Data/Repositories/CompassClientRepository.cs",
        "api/Modules/Compass/Data/Repositories/CompassDashboardRepository.cs",
        "api/Modules/Compass/Data/Repositories/CompassDirectoryRepository.cs",
        "api/Modules/Compass/Data/Repositories/CompassReadRepository.cs",
        "api/Modules/Compass/Data/Repositories/CompassReportRepository.cs",
        "api/Modules/Compass/Services/CompassAssignmentService.cs",
        "api/Modules/Compass/Services/CompassClientService.cs",
    ];

    /// <summary>A derived-status value produced by something other than the derivation.</summary>
    /// <remarks>
    /// <para>
    /// This alternation must list every member of both enums, and it FAILS OPEN if it does not.
    /// A value the pattern does not name is simply not found, so the rule passes and reports nothing —
    /// the same fail-open shape <c>docs/platform/adding-a-module.md</c> records finding eight times
    /// across Phases 48-49, except here the trigger is adding an enum member rather than moving a
    /// folder. Issue #274 added <c>ClientStatus.Former</c> and the whole of
    /// <c>AssignmentStatus</c>; both are named here, and
    /// <see cref="NonVacuity_ThePatternsMatchWhatTheyClaimTo"/> asserts each spelling individually so
    /// a future member cannot be half-added.
    /// </para>
    /// <para>
    /// <c>Former</c> is deliberately NOT matched against <c>AssignmentStatus</c> in practice — that
    /// enum has no such member, so the combined alternation is broader than either type. Broader is
    /// correct for a fence: naming a value that cannot exist costs nothing, while missing one that can
    /// is the failure above.
    /// </para>
    /// </remarks>
    private static readonly Regex ClientStatusConstruction = new(
        @"(?:ClientStatus|AssignmentStatus)\s*\.\s*(?:Active|Inactive|Former)",
        RegexOptions.Compiled);

    [Fact]
    public void NoProductionFileOutsideTheDerivation_ComparesAnAssignmentEndDate()
    {
        // Arrange
        var files = ProductionSourceFiles(CompassModuleDirectory);

        // Act
        var offenders = files
            .Where(f => !IsTheDerivation(f.RelativePath))
            .Where(f => EndDateComparison.IsMatch(StripComments(f.Contents)))
            .Select(f => f.RelativePath)
            .ToList();

        // Assert
        offenders.ShouldBeEmpty(
            "these files compare an assignment's EndDate outside the single derivation: "
                + string.Join(", ", offenders)
                + $". BR-11 requires ONE implementation. Compose {nameof(IClientStatusDerivation)}"
                + ".IsCurrent(today) or .IsActive(today) instead — they are expressions, so they compose "
                + "into a query. A second copy diverges on the exactly-today case, which is the case "
                + "AC-42 names.");
    }

    [Fact]
    public void NoProductionFileOutsideTheDerivation_ConstructsAClientStatus()
    {
        // Arrange
        var files = ProductionSourceFiles(CompassModuleDirectory);

        // Act — the enum's own declaration is not a construction site.
        var offenders = files
            .Where(f => !IsTheDerivation(f.RelativePath))
            // Each enum's own declaration is not a construction site.
            .Where(f => !f.RelativePath.EndsWith("/ClientStatus.cs", StringComparison.Ordinal))
            .Where(f => !f.RelativePath.EndsWith("/AssignmentStatus.cs", StringComparison.Ordinal))
            .Where(f => ClientStatusConstruction.IsMatch(StripComments(f.Contents)))
            .Select(f => f.RelativePath)
            .ToList();

        // Assert
        offenders.ShouldBeEmpty(
            "these files produce a ClientStatus value themselves: " + string.Join(", ", offenders)
                + ". Obtain it from IClientStatusDerivation.Of(client, today) — a value computed "
                + "locally is a second derivation wearing a different shape.");
    }

    // ------------------------------------------------------------------ Consumer completeness

    /// <summary>
    /// Every file that consumes the derivation is covered by the cross-surface agreement test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This closes the half the two rules above structurally cannot see. They are source scans:
    /// they prove no rival derivation was written. They cannot prove that a consumer which
    /// dutifully calls <c>IClientStatusDerivation</c> feeds it the same business date as its siblings,
    /// or projects the answer through without dropping it. Measured, not assumed — mutating
    /// <c>CompassReadRepository</c>'s client-directory pass to
    /// <c>IsActive(today.AddDays(1))</c> leaves both rules above passing 5/5 while two shipped surfaces
    /// visibly disagree about the same client.
    /// </para>
    /// <para>
    /// So the agreement itself is asserted by
    /// <c>tests/integration/Compass/ClientStatusCrossSurfaceAgreementTests.cs</c> (QA checkpoint CP1,
    /// <c>#90</c> bullet 4), against real PostgreSQL, because these predicates run in the database. This
    /// test's only job is to notice when a NEW consumer appears that that file does not read — otherwise
    /// the agreement test silently covers a shrinking fraction of the surfaces, and nothing says so.
    /// </para>
    /// <para>
    /// Adding a consumer is expected; leaving it unverified is not. When this fails, add the new
    /// surface to <c>ReadEveryStatusBearingSurfaceAsync</c> (bumping its <c>ExpectedSurfaceCount</c>) if
    /// it publishes a status a viewer can read, then add the file here.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryClientStatusConsumer_IsAccountedFor_ByTheCrossSurfaceAgreementTest()
    {
        // Arrange — the interface and the derivation declare it; the extension merely registers it.
        // None of the three is a consumer, and none can disagree with anything.
        var notConsumers = new[]
        {
            TheDerivation,
            "api/Modules/Compass/Interfaces/IClientStatusDerivation.cs",
            "api/Modules/Compass/CompassServiceCollectionExtensions.cs",
        };

        // Act
        var consumers = ProductionSourceFiles(CompassModuleDirectory)
            .Where(f => !notConsumers.Contains(f.RelativePath, StringComparer.Ordinal))
            .Where(f => DerivationUse.IsMatch(StripComments(f.Contents)))
            .Select(f => f.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        // Assert — non-vacuity first. An empty result would pass a ShouldBe against an empty expectation
        // and would also pass if the regex silently stopped matching.
        consumers.Count.ShouldBeGreaterThan(
            0,
            "no consumer of IClientStatusDerivation was found at all — the scan or the regex is broken, "
                + "not the module");

        consumers.ShouldBe(
            KnownConsumers,
            ignoreOrder: true,
            customMessage: "the set of client-status consumers changed. Every one of them must be read by "
                + "tests/integration/Compass/ClientStatusCrossSurfaceAgreementTests.cs, or CP1's "
                + "\"agrees across every consuming surface\" claim (AC-42, BR-11) covers less than it "
                + "says. Add the new surface to that test, then add the file to KnownConsumers here.");
    }

    // ------------------------------------------------------------------ Non-vacuity

    [Fact]
    public void NonVacuity_TheScanInspectsRealFilesAndTheDerivationExists()
    {
        // A scan matching zero files PASSES both assertions above. This is what stops that.
        var files = ProductionSourceFiles(CompassModuleDirectory);

        files.Count.ShouldBeGreaterThan(
            0, $"the single-derivation check found NOTHING to inspect under {CompassModuleDirectory}");
        files.ShouldContain(
            f => IsTheDerivation(f.RelativePath),
            $"the derivation itself is missing from the scan at {TheDerivation}. Either it moved — in "
                + "which case this constant is stale and the rule now fences nothing — or the scan is "
                + "broken.");
    }

    [Fact]
    public void NonVacuity_ThePatternsMatchWhatTheyClaimTo()
    {
        // The regexes are the whole gate. If they stop matching, both rules pass on any input and the
        // failure is completely silent — so they are asserted against known-bad and known-good text
        // rather than trusted.
        EndDateComparison.IsMatch("a.EndDate >= today").ShouldBeTrue();
        EndDateComparison.IsMatch("assignment.EndDate < cutoff").ShouldBeTrue();
        EndDateComparison.IsMatch("today <= x.EndDate").ShouldBeTrue();
        EndDateComparison.IsMatch("EndDate == null").ShouldBeFalse("null checks are a different question");
        EndDateComparison.IsMatch("public DateOnly? EndDate { get; set; }").ShouldBeFalse();

        // The lambda arrow. This exact line is real — ClientAssignmentConfiguration maps the column
        // this way, and it was flagged the first time this gate ran. A `=>` contains a `>`.
        EndDateComparison
            .IsMatch(@"builder.Property(x => x.EndDate).HasColumnName(""end_date"");")
            .ShouldBeFalse("a lambda arrow is not a comparison");
        EndDateComparison
            .IsMatch("assignments.Select(a => a.EndDate)")
            .ShouldBeFalse("a lambda arrow is not a comparison");

        // R-1: the previously-missed positives — currency comparisons that escaped the unrefined
        // regex because of operand order or because they matched only via SowEndDate substring.
        EndDateComparison.IsMatch("today <= a.EndDate").ShouldBeTrue();
        EndDateComparison.IsMatch("today > a.EndDate").ShouldBeTrue();
        EndDateComparison.IsMatch("sow.SowEndDate >= today").ShouldBeTrue(
            "SowEndDate compared against a business date is a currency comparison too");
        EndDateComparison.IsMatch("today <= sow.SowEndDate").ShouldBeTrue(
            "the flipped operand order must not escape the gate");

        // R-1: the collateral cases the unrefined regex wrongly tripped on — date-ORDER validation
        // against a sibling field on the same row, which cannot disagree with the derivation because
        // no business date is involved.
        EndDateComparison.IsMatch("request.EndDate < request.StartDate").ShouldBeFalse(
            "FR-008's date-order check compares two fields on the same row, not a business date");
        EndDateComparison.IsMatch("sow.SowEndDate >= sow.SowStartDate").ShouldBeFalse(
            "FR-020's date-order check is a sibling-field comparison, not a currency derivation");

        // R-1: the operand-order leak — flipping the operands must not change the verdict. Both
        // spellings of the same sibling comparison must agree (neither trips).
        EndDateComparison.IsMatch("sow.SowStartDate > sow.SowEndDate").ShouldBeFalse(
            "the flipped spelling of the same date-order check must not trip either — no leak");

        // R-4's overlap pre-check compares two SOWs' sibling dates directly, never against today.
        EndDateComparison.IsMatch("existing.SowStartDate <= candidate.SowEndDate").ShouldBeFalse(
            "the overlap pre-check compares sibling periods against each other, not against today");

        ClientStatusConstruction.IsMatch("return ClientStatus.Active;").ShouldBeTrue();
        ClientStatusConstruction.IsMatch("ClientStatus status = ClientStatus.Inactive;").ShouldBeTrue();
        ClientStatusConstruction.IsMatch("ClientStatus Of(Client client, DateOnly today)").ShouldBeFalse();

        // Issue #274's additions, asserted one spelling at a time. This is the assertion that makes
        // the fence's fail-open mode visible: without Former in the alternation, every check above
        // still passes and a rival `ClientStatus.Former` anywhere in the module goes unreported.
        ClientStatusConstruction.IsMatch("return ClientStatus.Former;").ShouldBeTrue(
            "a Former constructed outside the derivation is exactly what this fence is for");
        ClientStatusConstruction.IsMatch("AssignmentStatus.Active").ShouldBeTrue();
        ClientStatusConstruction.IsMatch("AssignmentStatus.Inactive").ShouldBeTrue();
        ClientStatusConstruction.IsMatch("AssignmentStatus StatusOfAssignment(bool isCurrent)")
            .ShouldBeFalse("a signature naming the type is not a construction");
    }

    [Fact]
    public void NonVacuity_MissingDirectory_ThrowsRatherThanInspectingNothing()
    {
        var thrown = Should.Throw<DirectoryNotFoundException>(
            () => ProductionSourceFiles("api/Modules/ThisModuleDoesNotExist"));

        thrown.Message.ShouldContain(
            "found nothing to inspect",
            Case.Insensitive,
            "the failure must say the check found nothing, so the fail-open shape is unmistakable");
    }

    // ------------------------------------------------------------------ Scanning

    private static bool IsTheDerivation(string relativePath) =>
        string.Equals(relativePath, TheDerivation, StringComparison.Ordinal);

    /// <summary>
    /// Removes comments and XML docs before matching.
    /// </summary>
    /// <remarks>
    /// Without this, every doc comment explaining the rule — including the ones in the derivation's
    /// own interface, which quote <c>EndDate &gt;= today</c> verbatim — would be reported as a
    /// violation, and the gate would be suppressed within a day.
    /// </remarks>
    private static string StripComments(string source)
    {
        var withoutBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//.*?$", string.Empty, RegexOptions.Multiline);
    }

    private static List<SourceFile> ProductionSourceFiles(string relativeDirectory)
    {
        var root = RepositoryRoot();
        var absolute = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(absolute))
        {
            throw new DirectoryNotFoundException(
                $"The single-derivation check found nothing to inspect: '{relativeDirectory}' does not "
                    + $"exist under repository root '{root}'. A path-based check that resolves no files "
                    + "PASSES, which is the fail-open shape this check exists to avoid — fix the path "
                    + "rather than letting the check run empty.");
        }

        return Directory
            .EnumerateFiles(absolute, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .Select(p => new SourceFile(
                Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(p)))
            .ToList();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "leap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "The single-derivation check found nothing to inspect: no repository root containing "
                    + $"leap.slnx above '{AppContext.BaseDirectory}'.");
    }

    private sealed record SourceFile(string RelativePath, string Contents);
}
