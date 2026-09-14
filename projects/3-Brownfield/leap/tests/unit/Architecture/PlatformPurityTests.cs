using System.Text.RegularExpressions;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Enforces the Platform→Module dependency direction: files under <c>api/Platform/</c> must not
/// name a module type — by import OR by qualification — unless the coupling is individually
/// allowlisted with a reason.
/// </summary>
/// <remarks>
/// <para>
/// This gate mirrors <see cref="DirectoryConsumerBoundaryTests"/> in structure — frozen allowlist,
/// stale-entry check, non-vacuity assertion, positive controls — and closes a gap that test could
/// never cover: Platform importing module types is a different violation from a module importing
/// directory types.
/// </para>
/// <para>
/// ADR-009 documents the ownership rule this gate enforces. The grandfathered exception is the
/// directory repositories and the cross-cutting services that consume Timesheet entities; both
/// categories are being reduced over time, and the stale-entry check forces the allowlist to
/// shrink as they migrate.
/// </para>
/// <para>
/// On the "138 Platform files" figure below. Three measurement notes here quote 138 as the
/// denominator they were taken over, and it has moved twice since. No replacement figure is quoted
/// on purpose — it would go stale the same way. Re-measure with
/// <c>git ls-files api/Platform/ | grep '\.cs$' | grep -v /Migrations/ | wc -l</c>, and do NOT
/// restate the three notes against the answer: each records one specific measurement (code lines
/// mentioning <c>Modules</c>, lines a deleted regex branch changed the verdict on, comment lines a
/// filter skipped) that was never re-run. Restating a denominator without re-running the
/// measurement turns a real record into a fabricated one.
/// </para>
/// </remarks>
public class PlatformPurityTests
{
    private const string PlatformDirectory = "api/Platform";

    /// <summary>
    /// A module namespace brought in by a <c>using</c> directive, in any of its legal spellings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>(?:^|;|*&#47;)</c> is what stops <c>using</c> matching mid-identifier: contract § C row 7
    /// requires an identifier that merely ENDS in <c>using</c>, followed by the namespace, to miss —
    /// see <see cref="TheDetector_DoesNotFireOnAnIdentifierThatMerelyEndsInUsing"/>.
    /// </para>
    /// <para>
    /// The <c>*&#47;</c> alternative is NOT decoration. Dropping the <c>/*</c> clause from
    /// <see cref="IsCode"/> makes the scan read
    /// <c>/* legacy */ using LeadingEDJE.Leap.Api.Modules.Timesheet;</c> — legal C#, a real import —
    /// but the anchor then rejected it anyway, because nothing before that <c>using</c> is either a
    /// line start or a <c>;</c>. Fixing the filter alone moved that line from "skipped" to "scanned
    /// and still missed"; the qualified pattern cannot rescue it either, since its own trailing
    /// <c>\.</c> requires a dot after <c>Timesheet</c> and the line has a <c>;</c> there.
    /// </para>
    /// <para>
    /// <c>\s*</c> around EVERY dot of the root-namespace prefix, and inside <c>global\s*::\s*</c>,
    /// because C# allows whitespace anywhere between the tokens of a qualified name.
    /// <c>using LeadingEDJE . Leap . Api . Modules . Timesheet;</c> and
    /// <c>using global :: LeadingEDJE.Leap.Api.Modules.Timesheet;</c> both compile, and both were
    /// missed while the prefix was spelled literally — see
    /// <see cref="TheDetector_FindsAReference_ThroughSpacingInTheRootNamespacePrefix"/>. Unlike
    /// <see cref="ModuleQualifiedReference"/>, this pattern has no bare-<c>Modules</c> fallback, so
    /// every one of those <c>\s*</c> is load-bearing and is pinned by its own control in
    /// <see cref="TheDetector_CoversEveryOptionalBranchOfItsPattern"/>.
    /// </para>
    /// </remarks>
    private static readonly Regex ModuleImport = new(
        @"(?m)(?:^|;|\*/)\s*(?:global\s+)?using\s+(?:(?:static|unsafe)\s+)?(?:[@\w]+\s*=\s*)?(?:global\s*::\s*)?LeadingEDJE\s*\.\s*Leap\s*\.\s*Api\s*\.\s*Modules\s*\.",
        RegexOptions.Compiled);

    /// <summary>
    /// A module type named by qualification, with NO import to give it away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>LeadingEDJE … Api</c> prefix is optional because it usually is not written: Platform
    /// files live under <c>LeadingEDJE.Leap.Api.Platform.*</c>, so name lookup walks out to
    /// <c>LeadingEDJE.Leap.Api</c> and the bare <c>Modules.Timesheet.Timesheet</c> binds.
    /// </para>
    /// <para>
    /// <c>(?&lt;![\w.])</c> is what keeps a PLATFORM namespace containing the word <c>Modules</c>
    /// out of the match — in <c>Platform.Modules.Registry.</c> the segment is preceded by a dot, so
    /// the lookbehind rejects it. It inspects exactly ONE character, so the SPACED spelling
    /// <c>Platform . Modules . Registry .</c> is not rejected and does over-fire. That is a
    /// fail-CLOSED false positive of the same family as the string-literal one below, it predates
    /// the whitespace widening rather than being caused by it, and it costs nothing today: across
    /// the 138 Platform files, 52 code lines mention <c>Modules</c> at all and 0 match
    /// <c>\.\s+Modules\s*\.</c>.
    /// </para>
    /// <para>
    /// <c>\w+</c> and NOT <c>(?:Timesheet|Ooto|Compass)</c>, per FR-014(c): a roster fails OPEN the
    /// day a fourth module lands, which is worse than no gate at all — see
    /// <see cref="TheDetector_FindsAReferenceToAModuleItHasNeverHeardOf"/>. It is also not
    /// <c>[A-Z]\w+</c>, which was the same defect one abstraction up: a naming CONVENTION rather
    /// than a name list, but failing open just as surely on <c>Modules.payroll.X</c> and on a
    /// single-character segment, both legal C#.
    /// </para>
    /// <para>
    /// <c>\s*</c> around EVERY dot — those after <c>Modules</c> and those in the optional prefix
    /// alike — because C# allows whitespace anywhere between the tokens of a qualified name.
    /// <c>Modules . Timesheet . Timesheet</c> and <c>LeadingEDJE.Leap . Api.Modules.Timesheet</c>
    /// both compile, verified with a real <c>dotnet build</c>. The prefix half was missed when the
    /// first half was fixed: because the lookbehind rejects a <c>Modules</c> preceded by a dot, that
    /// prefix is the ONLY route a fully-qualified reference has, so one space in it defeated this
    /// pattern and <see cref="ModuleImport"/> at once.
    /// </para>
    /// <para>
    /// There is deliberately NO <c>\s*</c> between the prefix's final <c>\.</c> and <c>Modules</c>,
    /// and adding one back would be an untestable fragment. Whenever it could consume whitespace,
    /// the character before <c>Modules</c> IS that whitespace, so the lookbehind admits the bare
    /// route, which then matches the identical tail — the two spellings cannot disagree. Measured,
    /// not assumed: 67,584 generated spacings produced 0 differences, against a positive control
    /// that separated the same inputs 8,121 ways, and re-adding the fragment is the one deliberate
    /// SURVIVOR of the 35-mutant sweep. Same reasoning, and the same treatment, as the
    /// <c>(?:global::)?</c> branch below.
    /// </para>
    /// <para>
    /// The trailing <c>\.</c> is LOAD-BEARING and must not be trimmed: it is the only thing telling
    /// a compiled reference apart from a namespace named as a STRING, and it is what spares both
    /// <c>LeapDbContext.ModuleSchemas</c> literals — see
    /// <see cref="TheDetector_DoesNotFireOnAModuleNamespaceWrittenAsAString"/>.
    /// </para>
    /// <para>
    /// There is no <c>(?:global::)?</c> branch, because it could never be the sole route to a match:
    /// it sits AFTER the lookbehind, and <c>:</c> is not in <c>[\w.]</c>, so a match simply restarts
    /// at the position past <c>global::</c>. Measured before deleting it — over the 138 Platform
    /// files (10,748 lines) the branch changed the verdict on 0 lines and 0 files, and all six
    /// synthetic <c>global::</c> shapes matched identically with and without it. The same holds for
    /// the SPACED <c>global :: </c>, for the same reason: whitespace is not in <c>[\w.]</c> either,
    /// so <c>private global :: LeadingEDJE.Leap.Api.Modules.Timesheet.Timesheet? _y;</c> was the one
    /// shape of the five-shape prefix defect this pattern already caught unaided.
    /// </para>
    /// </remarks>
    private static readonly Regex ModuleQualifiedReference = new(
        @"(?<![\w.])(?:LeadingEDJE\s*\.\s*Leap\s*\.\s*Api\s*\.)?Modules\s*\.\s*\w+\s*\.",
        RegexOptions.Compiled);

    /// <summary>
    /// The scan predicate: does this file text carry a Platform→module dependency?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per LINE rather than over whole file text, because <see cref="ModuleQualifiedReference"/>
    /// is only safe with commentary skipped: without that, a Platform file merely MENTIONING a
    /// module type in prose would need an allowlist entry, polluting the count this gate exists to
    /// make trustworthy (FR-014(d)). Both false positives were measured, not imagined.
    /// </para>
    /// <para>
    /// A line carrying a <c>cref</c> attribute is code, not commentary. A cref is bound by Roslyn
    /// and an unresolvable one is CS1574, so a fully-qualified one is a real dependency — and it is
    /// precisely the repair FR-010 forbids. Excepting it is what makes that rule mechanical
    /// for a cref written on ONE line, which is the whole of it: a cref whose attribute value
    /// wraps onto the next physical line still binds (a wrapped cref to a bogus type still raises
    /// CS1574) and still escapes, because the first line carries <c>cref=</c> but no namespace and
    /// the second is a <c>///</c> line with no <c>cref=</c>, so the filter skips it. Measured:
    /// one line flagged, the same cref split after <c>cref=</c> not flagged. Wrapping BEFORE
    /// <c>cref=</c> still fires, since that leaves attribute and value on one line together.
    /// </para>
    /// <para>
    /// Residual shapes no line-oriented text scan can see, all still review-only per FR-017:
    /// a reference split across a line break — of which the wrapped cref above is one case — a
    /// comment embedded INSIDE a qualified name, reflection or any string-keyed type load, and an
    /// alias declared elsewhere. <c>using LeadingEDJE./*c*/Leap.Api.Modules.Timesheet;</c> compiles
    /// and is not flagged; matching it would mean stripping comments before scanning, which is a
    /// parser, not a regex, and it is deliberately out of scope. The line-break, reflection and
    /// alias shapes were measured absent as of 2026-08-31 — zero lines under <c>api/Platform/</c>
    /// end in <c>Modules</c> or <c>Modules.</c>; zero
    /// <c>Type.GetType</c>/<c>Activator.CreateInstance</c>/<c>Assembly…GetType</c> call sites there;
    /// and zero <c>global using</c> directives anywhere under <c>api/</c>.
    /// </para>
    /// <para>
    /// Whitespace is NOT a residual, and is not fully closed either. Both patterns now
    /// tolerate it around every dot of a qualified name and inside <c>global ::</c>, which covers
    /// the shapes a single line can hold. What remains open is only whitespace containing a line
    /// break or a comment — the two residuals above — because those are properties of the line
    /// split, not of the spacing.
    /// </para>
    /// <para>
    /// In the other direction it over-fires on a module name written inside a string literal,
    /// and — since the filter stopped honouring <c>*</c> and <c>/*</c> — on any line of a
    /// <c>/* … */</c> block comment. That trade is deliberate: a mis-skip fails OPEN, a false
    /// positive fails CLOSED. It costs nothing today, because <c>api/Platform/</c> contains zero
    /// lines opening with <c>/*</c> and exactly one opening with <c>*</c>
    /// (<c>Auth/AccessDeniedPage.cs:103</c>, CSS in a raw string literal), which names no module.
    /// </para>
    /// </remarks>
    private static bool ReferencesAModule(string text) =>
        text.Split('\n')
            .Select(line => line.TrimStart())
            .Where(IsCode)
            .Any(line => ModuleImport.IsMatch(line) || ModuleQualifiedReference.IsMatch(line));

    /// <summary>
    /// A <c>cref</c> attribute, tolerating the whitespace XML allows around its <c>=</c>.
    /// </summary>
    /// <remarks>
    /// <c>\s*</c> because <c>cref = "…"</c> and <c>cref\t=\t"…"</c> bind exactly as <c>cref="…"</c>
    /// does; <c>\b</c> because <c>xcref=</c> is a word in prose, not an attribute. The literal
    /// <c>Contains("cref=")</c> this replaced was defeated by a single space.
    /// </remarks>
    private static readonly Regex CrefAttribute = new(@"\bcref\s*=", RegexOptions.Compiled);

    /// <summary>
    /// Is this trimmed line something the compiler reads? Line comments are commentary; everything
    /// else — including a <c>cref</c>-bearing doc comment — is treated as code.
    /// </summary>
    /// <remarks>
    /// Only the <c>//</c> clause, which covers <c>///</c> too. The former <c>*</c> and <c>/*</c>
    /// clauses skipped genuine code and prevented nothing — see
    /// <see cref="TheScan_DoesNotSkipALine_MerelyBecauseItOpensWithACommentToken"/> for the
    /// measurements.
    /// </remarks>
    private static bool IsCode(string line) =>
        !line.StartsWith("//", StringComparison.Ordinal) || CrefAttribute.IsMatch(line);

    /// <summary>
    /// Every <c>api/Platform/</c> file permitted to import a module namespace, with the reason.
    /// </summary>
    /// <remarks>
    /// Empty since the Timesheet/Ooto module removal. Every entry above named a Platform file's
    /// coupling to the Timesheet-owned <c>Person</c> entity or its directory repositories; <c>Person</c>
    /// is now a native Platform entity (<c>api/Platform/Domain/Person.cs</c>) and the directory repo
    /// interfaces/implementations were deleted outright rather than kept frozen. Platform has no module
    /// dependency left to grandfather. Leave this empty rather than deleting the mechanism — the next
    /// real coupling still needs somewhere to be recorded with its reason.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Allowlist =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // ---------------------------------------------------------------- main rule

    [Fact]
    public void NoPlatformFile_ImportsAModuleNamespace_UnlessAllowlisted()
    {
        // Arrange
        var files = PlatformSourceFiles();

        // Act
        var violations = files
            .Where(f => ReferencesAModule(f.Text))
            .Where(f => !Allowlist.ContainsKey(f.RelativePath))
            .Select(f => f.RelativePath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        // Assert
        files.Count.ShouldBeGreaterThan(
            0, "Platform file scan found zero files — path is wrong");

        violations.ShouldBeEmpty(
            "a Platform file names a module type (imported or qualified) without an allowlist "
                + "entry. "
                + "Platform must not depend on modules — move the dependency into the module, "
                + "or if this is a legitimate grandfathered coupling, add it to the allowlist "
                + "with a reason. Offenders: " + string.Join(", ", violations));
    }

    // ---------------------------------------------------------------- stale-entry check

    [Fact]
    public void TheAllowlist_ContainsNoStaleEntry()
    {
        // Arrange
        var files = PlatformSourceFiles();
        var importing = files
            .Where(f => ReferencesAModule(f.Text))
            .Select(f => f.RelativePath)
            .ToHashSet(StringComparer.Ordinal);

        // Act
        var stale = Allowlist.Keys
            .Where(p => !importing.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        // Assert
        stale.ShouldBeEmpty(
            "an allowlist entry no longer names a module type — delete it so the "
                + "exception list stays as small as the code requires. Stale entries: "
                + string.Join(", ", stale));
    }

    // ---------------------------------------------------------------- reason check

    [Fact]
    public void EveryAllowlistEntry_CarriesAReason()
    {
        // Arrange & Act
        var missing = Allowlist
            .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => kv.Key)
            .ToList();

        // Assert
        missing.ShouldBeEmpty(
            "every allowlisted coupling must state WHY it is permitted: "
                + string.Join(", ", missing));
    }

    // ---------------------------------------------------------------- positive controls

    [Fact]
    public void TheDetector_FindsAModuleImport_WhenOneExists()
    {
        // Arrange
        const string offending = "using LeadingEDJE.Leap.Api.Modules.Timesheet;";

        // Act & Assert
        ModuleImport.IsMatch(offending).ShouldBeTrue(
            "the module-import detector must fire on an import of a module namespace, "
                + "or the main rule can never fail");
    }

    [Fact]
    public void TheDetector_FindsAModuleImport_WhenASecondDirectiveSharesTheLine()
    {
        // Arrange — legal C#: two using directives on one physical line. The pre-hardening
        // detector caught this; line-anchoring the pattern would silently stop catching it,
        // which is a WEAKENED gate (constitution "Gate integrity") rather than a fixed one.
        const string offending = "using System; using LeadingEDJE.Leap.Api.Modules.Timesheet;";

        // Act & Assert
        ModuleImport.IsMatch(offending).ShouldBeTrue(
            "a module import is still an import when it does not start the line — anchoring "
                + "the pattern must not trade a real detection for the comment false-positive fix");
    }

    [Fact]
    public void TheDetector_CoversEveryOptionalBranchOfItsPattern()
    {
        // Arrange — one literal per optional group in the pattern. Mutation testing found these
        // branches had ZERO coverage: each could be deleted from the regex and nothing went red.
        // They all work; they were simply untested, which is the same as ungated.
        //
        // The last shape is the only MULTI-line one, and it is deliberate. ReferencesAModule splits
        // on '\n' before matching, so no line it passes in can contain one and (?m) cannot change a
        // verdict there — it survived every mutant run. It is kept rather than deleted because an
        // input DOES distinguish it, so deleting it would leave the pattern silently wrong for any
        // future caller that matches against whole-file text. This literal is that input.
        var shapes = new[]
        {
            "    using LeadingEDJE.Leap.Api.Modules.Timesheet;",                      // ^\s*  indentation
            "using T=LeadingEDJE.Leap.Api.Modules.Timesheet.Timesheet;",              // alias \s* (no spaces)
            "using @class = LeadingEDJE.Leap.Api.Modules.Timesheet.Timesheet;",       // [@\w]+ verbatim alias
            "using global::LeadingEDJE.Leap.Api.Modules.Timesheet;",                  // (?:global::)?
            "using unsafe P = LeadingEDJE.Leap.Api.Modules.Timesheet.Ptr;",           // unsafe alternative
            "/* legacy */ using LeadingEDJE.Leap.Api.Modules.Timesheet;",             // */ anchor alternative
            "class C { }\nusing LeadingEDJE.Leap.Api.Modules.Timesheet;",             // (?m)  ^ after a newline

            // One control per \s* in the root-namespace prefix, each isolating a single gap so a
            // mutation sweep names the exact fragment it killed. The prefix is MANDATORY in this
            // pattern — there is no bare-'Modules' fallback route here, as there is in
            // ModuleQualifiedReference — so all nine of these are killable.
            "using global ::LeadingEDJE.Leap.Api.Modules.Timesheet;",                 // global\s*::
            "using global:: LeadingEDJE.Leap.Api.Modules.Timesheet;",                 // ::\s*
            "using LeadingEDJE .Leap.Api.Modules.Timesheet;",                         // LeadingEDJE\s*\.
            "using LeadingEDJE. Leap.Api.Modules.Timesheet;",                         // \.\s*Leap
            "using LeadingEDJE.Leap .Api.Modules.Timesheet;",                         // Leap\s*\.
            "using LeadingEDJE.Leap. Api.Modules.Timesheet;",                         // \.\s*Api
            "using LeadingEDJE.Leap.Api .Modules.Timesheet;",                         // Api\s*\.
            "using LeadingEDJE.Leap.Api. Modules.Timesheet;",                         // \.\s*Modules
            "using LeadingEDJE.Leap.Api.Modules .Timesheet;",                         // Modules\s*\.
        };

        // Act & Assert
        foreach (var shape in shapes)
        {
            ModuleImport.IsMatch(shape).ShouldBeTrue(
                $"every optional branch of the detector must be exercised by a control: {shape}");
        }
    }

    /// <summary>
    /// The alias form is MANDATED for the CS0118 sibling-namespace
    /// collision. An allowlist entry that could be retired by rewriting its import into this shape
    /// would make the count a fiction, so the detector must see it.
    /// </summary>
    [Fact]
    public void TheDetector_FindsAModuleImport_WrittenAsAnAlias()
    {
        // Arrange
        const string offending =
            "using TimesheetEntity = LeadingEDJE.Leap.Api.Modules.Timesheet.Timesheet;";

        // Act & Assert
        ModuleImport.IsMatch(offending).ShouldBeTrue(
            "the detector must fire on the aliased import form — it is the shape the CS0118 "
                + "convention mandates, so it is the easiest way to hide a coupling");
    }

    [Fact]
    public void TheDetector_FindsAModuleImport_WrittenAsUsingStatic()
    {
        // Arrange
        const string offending = "using static LeadingEDJE.Leap.Api.Modules.Timesheet.Foo;";

        // Act & Assert
        ModuleImport.IsMatch(offending).ShouldBeTrue(
            "the detector must fire on 'using static' — it imports module members just as "
                + "surely as a plain namespace import");
    }

    /// <summary>
    /// A <c>global using</c> in a Platform file is the worst case: it injects the module namespace
    /// into EVERY file in the assembly, so any Platform file can then use module types with no
    /// import line of its own.
    /// </summary>
    [Fact]
    public void TheDetector_FindsAModuleImport_WrittenAsAGlobalUsingAlias()
    {
        // Arrange
        const string offending =
            "global using TimesheetEntity = LeadingEDJE.Leap.Api.Modules.Timesheet.Timesheet;";

        // Act & Assert
        ModuleImport.IsMatch(offending).ShouldBeTrue(
            "the detector must fire on a global using — it is strictly worse than a file-local "
                + "import, not an exemption from the rule");
    }

    /// <summary>
    /// Contract § C row 7: an identifier that merely ENDS in <c>using</c>, followed by the module
    /// namespace, is not an import. This is the control the anchor in <see cref="ModuleImport"/>
    /// exists for — delete <c>(?:^|;|*&#47;)</c> and <c>using</c> matches anywhere on the line.
    /// </summary>
    /// <remarks>
    /// The anchor had no control literal of its own until now, so widening it to admit a
    /// block-comment prefix could have silently taken this guard with it.
    /// </remarks>
    [Fact]
    public void TheDetector_DoesNotFireOnAnIdentifierThatMerelyEndsInUsing()
    {
        // Arrange
        var benign = new[]
        {
            "Xusing LeadingEDJE.Leap.Api.Modules.Timesheet;",
            "        Xreusing LeadingEDJE.Leap.Api.Modules.Timesheet;",
        };

        // Act & Assert
        foreach (var line in benign)
        {
            ReferencesAModule(line).ShouldBeFalse(
                "the anchor is what stops 'using' matching mid-identifier: " + line);
        }
    }

    // ------------------------------------------- positive controls: no import at all

    /// <summary>
    /// A qualified reference with NO <c>using</c> anywhere. Platform files live under
    /// <c>LeadingEDJE.Leap.Api.Platform.*</c>, so C# name lookup walks out to
    /// <c>LeadingEDJE.Leap.Api</c> and <c>Modules.Timesheet.Timesheet</c> binds in FEWER keystrokes
    /// than the honest import — the cheapest evasion available, not an exotic one.
    /// </summary>
    /// <remarks>
    /// And <see cref="TheAllowlist_ContainsNoStaleEntry"/> actively rewards taking it: converting a
    /// file's imports to this shape COMPELS deleting its allowlist entry, so the count falls while
    /// the Roslyn-level dependency is entirely intact. 13 such references already exist under
    /// <c>api/Platform/</c> — 2 in <c>LeapDbContext.cs</c>, 11 in <c>DevelopmentSeeder.cs</c>.
    /// </remarks>
    [Fact]
    public void TheDetector_FindsAQualifiedModuleReference_WithNoImportAtAll()
    {
        // Arrange — every shape contracts/gate-contract.md § D listed as a known limit, plus the
        // short form that section originally missed.
        var offending = new[]
        {
            "    public DbSet<Modules.Timesheet.Timesheet> Timesheets => Set<Modules.Timesheet.Timesheet>();",
            "        new Modules.Timesheet.Timesheet { Id = 1, TotalHours = 40.0m },",
            "    private global::LeadingEDJE.Leap.Api.Modules.Timesheet.Timesheet? _t;",
            "        _ = new LeadingEDJE.Leap.Api.Modules.Timesheet.TimeCategory();",
        };

        // Act & Assert
        foreach (var line in offending)
        {
            ReferencesAModule(line).ShouldBeTrue(
                "a qualified module reference is a real Platform→module dependency whether or not "
                    + "an import accompanies it: " + line);
        }
    }

    /// <summary>
    /// A <c>cref</c> is not prose — it is compiler-bound, and an unresolvable one is CS1574. FR-010
    /// forbids repairing <c>SlackApprovalChannel</c>'s cref by fully-qualifying it, because that
    /// compiles, keeps a real Platform→module reference, and still drops the allowlist entry.
    /// </summary>
    /// <remarks>
    /// This control is what makes FR-010 MECHANICAL instead of review-only, and it is the reason
    /// the comment filter treats a line carrying <c>cref=</c> as code rather than commentary.
    /// </remarks>
    [Fact]
    public void TheDetector_FindsAFullyQualifiedCref_EvenInsideADocComment()
    {
        // Arrange
        const string offending =
            @"    /// <see cref=""LeadingEDJE.Leap.Api.Modules.Timesheet.Services.Slack.Processor""/>";

        // Act & Assert
        ReferencesAModule(offending).ShouldBeTrue(
            "a fully-qualified cref is a compiler-verified reference to a module type, so it is a "
                + "dependency the gate must see — FR-010's forbidden repair, caught mechanically");
    }

    /// <summary>
    /// XML permits whitespace around an attribute's <c>=</c>, and Roslyn binds <c>cref = "…"</c>
    /// exactly as it binds <c>cref="…"</c> — an unresolvable one is CS1574 either way, confirmed by
    /// a real <c>dotnet build</c>. A literal <c>Contains("cref=")</c> test therefore lets ONE SPACE
    /// retire the FR-010 exemption.
    /// </summary>
    /// <remarks>
    /// That is not a cosmetic miss. The exemption is the only mechanical enforcement FR-010 has, and
    /// a file that evades it is not flagged at all — which then COMPELS deleting its allowlist entry
    /// via <see cref="TheAllowlist_ContainsNoStaleEntry"/>, shrinking the count while the
    /// compiler-bound dependency stays exactly where it was.
    /// </remarks>
    [Fact]
    public void TheDetector_FindsACref_WhateverWhitespaceSurroundsItsEquals()
    {
        // Arrange — one compiler-bound reference, spelled three ways XML treats identically.
        var offending = new[]
        {
            @"    /// <see cref=""LeadingEDJE.Leap.Api.Modules.Timesheet.Services.Slack.Processor""/>",
            @"    /// <see cref = ""LeadingEDJE.Leap.Api.Modules.Timesheet.Services.Slack.Processor""/>",
            "    /// <see cref\t=\t\"LeadingEDJE.Leap.Api.Modules.Timesheet.Services.Slack.Processor\"/>",
        };

        // Act & Assert
        foreach (var line in offending)
        {
            ReferencesAModule(line).ShouldBeTrue(
                "whitespace around the '=' does not stop Roslyn binding the cref, so it must not "
                    + "stop the gate seeing it: " + line);
        }
    }

    /// <summary>
    /// The exemption must recognise the ATTRIBUTE, not the four letters. <c>xcref=</c> is prose;
    /// reading it as code would drag genuine commentary back into the count FR-014(d) exists to keep
    /// honest — the exact false positive the comment filter was added to prevent.
    /// </summary>
    [Fact]
    public void TheDetector_DoesNotTreatALongerWordEndingInCref_AsACref()
    {
        // Arrange
        const string commentary =
            @"    /// see xcref=""LeadingEDJE.Leap.Api.Modules.Timesheet.Services.Slack.Processor""";

        // Act & Assert
        ReferencesAModule(commentary).ShouldBeFalse(
            "only a real cref attribute is compiler-bound — 'xcref' is a word in prose, and reading "
                + "it as code re-opens the commentary false positive: " + commentary);
    }

    /// <summary>
    /// A line is not commentary because it OPENS with a comment token. All three shapes here are
    /// real, compiling code that the <c>*</c> and <c>/*</c> clauses of the filter skipped outright.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The multiplication continuation is not exotic. This repository wraps binary operators to the
    /// START of the continuation line in 124 places across 46 files under <c>api/</c>
    /// (measured: <c>rg '^\s+(\*|\+|&amp;&amp;|\|\||\?\?)\s' api/</c>). Every one of those lines was
    /// invisible to this gate.
    /// </para>
    /// <para>
    /// The clauses bought nothing in return. Under <c>api/Platform/</c> there are ZERO lines
    /// opening with <c>/*</c>, and the <c>*</c> clause has exactly ONE target — which it
    /// mis-classifies: <c>api/Platform/Auth/AccessDeniedPage.cs:103</c> is
    /// <c>* { box-sizing: border-box; }</c>, CSS inside a raw string literal, not a comment. Over all
    /// 138 Platform files, the number of comment lines the clauses skipped that would otherwise have
    /// matched is 0. Dropping them trades a fail-OPEN mis-skip for a fail-CLOSED false
    /// positive, which is the direction the constitution's gate-integrity rule requires.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheScan_DoesNotSkipALine_MerelyBecauseItOpensWithACommentToken()
    {
        // Arrange
        var offending = new[]
        {
            "/* legacy */ using LeadingEDJE.Leap.Api.Modules.Timesheet;",
            "/* legacy */ var t = new Modules.Timesheet.Timesheet();",
            "    * Modules.Timesheet.Rates.Factor;",
        };

        // Act & Assert
        foreach (var line in offending)
        {
            ReferencesAModule(line).ShouldBeTrue(
                "this is compiling code, not commentary — skipping it is a fail-OPEN hole: " + line);
        }
    }

    /// <summary>
    /// FR-014(c): match the SHAPE of a module-qualified reference, never a roster of module names.
    /// A pattern spelling <c>Timesheet|Ooto|Compass</c> fails OPEN the day a fourth module lands,
    /// and a gate that fails open is worse than no gate at all.
    /// </summary>
    [Fact]
    public void TheDetector_FindsAReferenceToAModuleItHasNeverHeardOf()
    {
        // Arrange — a module that does not exist. The roster pattern is the form this control
        // exists to reject; asserting it MISSES first is what stops this test passing vacuously
        // whichever pattern happens to ship.
        const string offending = "        var run = new Modules.Payroll.PayrollRun();";
        var hardcodedRoster = new Regex(
            @"(?<![\w.])(?:LeadingEDJE\.Leap\.Api\.)?Modules\s*\.\s*(?:Timesheet|Ooto|Compass)\s*\.");

        // Act & Assert
        hardcodedRoster.IsMatch(offending).ShouldBeFalse(
            "control integrity: the roster form must MISS a fourth module, or this test proves "
                + "nothing about which pattern shipped");

        ReferencesAModule(offending).ShouldBeTrue(
            "the detector must match the shape of a module-qualified reference, not the list of "
                + "module names that happen to exist today");
    }

    /// <summary>
    /// Shapes that COMPILE and evaded the qualified-reference pattern: whitespace around the dots,
    /// a lowercase namespace segment, and a single-character one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[A-Z]\w+</c> encoded a NAMING CONVENTION, and a convention fails open exactly the way
    /// FR-014(c) says a roster does — it is the same defect one abstraction up. A module folder
    /// named in any other case, or with a one-letter segment, walked straight through. Matching the
    /// shape instead costs nothing.
    /// </para>
    /// <para>
    /// <c>private Modules . Timesheet . Timesheet? _spaced;</c> compiles — verified with a real
    /// <c>dotnet build</c>, not assumed. The class doc used to claim the only residuals were
    /// line-split references, reflection and aliases declared elsewhere; this shape contradicted it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDetector_FindsAQualifiedReference_ThroughSpacingAndCasing()
    {
        // Arrange
        var offending = new[]
        {
            "    private Modules . Timesheet . Timesheet? _spaced;",
            "        var run = new Modules.payroll.PayrollRun();",
            "        var y = new Modules.X.Y();",
        };

        // Act & Assert
        foreach (var line in offending)
        {
            ReferencesAModule(line).ShouldBeTrue(
                "spacing and casing are not a dependency's business — this is a real "
                    + "Platform→module reference: " + line);
        }
    }

    /// <summary>
    /// The same whitespace tolerance, applied to the ROOT-NAMESPACE PREFIX instead of to the
    /// segments after <c>Modules</c> — <c>LeadingEDJE . Leap . Api .</c>, and <c>global ::</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One space in that prefix defeated BOTH detectors at once. <see cref="ModuleQualifiedReference"/>'s
    /// lookbehind rejects a <c>Modules</c> preceded by a dot, so for a FULLY-QUALIFIED reference that
    /// literal prefix is the only route to a match — break it and there is no fallback.
    /// </para>
    /// <para>
    /// These shapes COMPILE. Four probe files under <c>api/Platform/</c> written this way, each
    /// reaching a real module type, built 0 warnings / 0 errors and left the gate green, while the
    /// same reference spelled ordinarily failed it by name.
    /// </para>
    /// <para>
    /// It is the fail-OPEN shape FR-014 exists to prevent: an evading file is not flagged, which then
    /// COMPELS deleting its allowlist entry via <see cref="TheAllowlist_ContainsNoStaleEntry"/> — the
    /// count falls while the Roslyn-level dependency stays exactly where it was. The commit that
    /// added <c>\s*</c> around the dots AFTER <c>Modules</c> left this prefix unspaced in both
    /// patterns, so that fix landed on half the name.
    /// </para>
    /// <para>
    /// The benign half is not decoration: widening a pattern for whitespace is the easiest way to
    /// widen WHAT it matches, so the same fact pins both directions.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDetector_FindsAReference_ThroughSpacingInTheRootNamespacePrefix()
    {
        // Arrange
        var offending = new[]
        {
            "using LeadingEDJE . Leap . Api . Modules . Timesheet;",
            "using LeadingEDJE.Leap . Api.Modules.Timesheet;",
            "using LeadingEDJE.Leap.Api . Modules.Timesheet;",
            "    private LeadingEDJE.Leap . Api.Modules.Timesheet.Timesheet? _x;",
            "using global :: LeadingEDJE.Leap.Api.Modules.Timesheet;",

            // The one shape already caught before this fix, kept as its regression guard: the
            // qualified pattern rescues it on its own, because ':' is not in [\w.] and the match
            // simply restarts past 'global :: '.
            "    private global :: LeadingEDJE.Leap.Api.Modules.Timesheet.Timesheet? _y;",
        };

        var benign = new[]
        {
            // Spaced, and still PLATFORM. Widening the prefix must not make Platform.Modules reachable.
            "using LeadingEDJE . Leap . Api . Platform . Modules . Registry;",

            // Spaced, and still a STRING. The trailing \. must survive the widening.
            @"        (""LeadingEDJE . Leap . Api . Modules . Timesheet"", ""timesheet""),",
        };

        // Act & Assert
        foreach (var line in offending)
        {
            ReferencesAModule(line).ShouldBeTrue(
                "whitespace inside the root-namespace prefix is legal C# and changes nothing about "
                    + "the dependency it declares: " + line);
        }

        foreach (var line in benign)
        {
            ReferencesAModule(line).ShouldBeFalse(
                "tolerating whitespace must not widen what the pattern matches: " + line);
        }
    }

    /// <summary>
    /// A module namespace written as a STRING is data, not a dependency. The trailing <c>\.</c> in
    /// <see cref="ModuleQualifiedReference"/> is the only thing that tells the two apart.
    /// </summary>
    /// <remarks>
    /// Both literals are copied verbatim from <c>LeapDbContext.ModuleSchemas</c>, which maps a CLR
    /// namespace prefix to a Postgres schema. Drop the trailing dot from the pattern and both
    /// match — a distinction this feature has gotten wrong twice, pinned by nothing until now.
    /// </remarks>
    [Fact]
    public void TheDetector_DoesNotFireOnAModuleNamespaceWrittenAsAString()
    {
        // Arrange
        var benign = new[]
        {
            @"        (""LeadingEDJE.Leap.Api.Modules.Timesheet"", ""timesheet""),",
            @"        (""LeadingEDJE.Leap.Api.Modules.Ooto"", ""ooto""),",
        };

        // Act & Assert
        foreach (var line in benign)
        {
            ReferencesAModule(line).ShouldBeFalse(
                "a namespace named as a string is configuration, not a compiled reference — the "
                    + "trailing dot is what separates them: " + line);
        }
    }

    /// <summary>
    /// Every optional fragment of <see cref="ModuleQualifiedReference"/> and every clause of
    /// <see cref="IsCode"/>, each pinned by at least one control that FLIPS when the fragment is
    /// deleted. Sibling of <see cref="TheDetector_CoversEveryOptionalBranchOfItsPattern"/>, which
    /// does the same job for <see cref="ModuleImport"/>.
    /// </summary>
    /// <remarks>
    /// Mutation testing found SEVEN survivors among the fragments the qualified-reference commit
    /// added: each could be deleted with the whole suite still green. That check was performed for
    /// <see cref="ModuleImport"/> one commit earlier and simply not repeated for the new pattern or
    /// the filter. A fragment nothing distinguishes is either dead code or an ungated behaviour, and
    /// both are worth knowing which.
    /// <para>
    /// Re-run across BOTH patterns after the whitespace widening: 35 mutants, 34 killed, and the
    /// single survivor is the deliberately-omitted <c>\s*</c> documented on
    /// <see cref="ModuleQualifiedReference"/> — re-adding it changes no verdict, which is why it is
    /// not in the pattern. Anything new here must be killable, or it does not belong.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheQualifiedDetector_CoversEveryOptionalBranchOfItsPattern()
    {
        // Arrange — (line, must the scan fire?, the fragment this control pins).
        var controls = new (string Line, bool Fires, string Fragment)[]
        {
            ("    var registry = Platform.Modules.Registry.ModuleRegistry.Empty;",
             false, @"(?<![\w.]) — a PLATFORM namespace ending in 'Modules' is not a module"),

            ("        _ = new LeadingEDJE.Leap.Api.Modules.Timesheet.TimeCategory();",
             true, @"(?:LeadingEDJE\.Leap\.Api\.)? PRESENT — the fully-qualified spelling"),

            ("        new Modules.Timesheet.Timesheet { Id = 1 },",
             true, @"(?:LeadingEDJE\.Leap\.Api\.)? OPTIONAL — the bare spelling, which binds anyway"),

            (@"        (""LeadingEDJE.Leap.Api.Modules.Timesheet"", ""timesheet""),",
             false, @"the trailing \. — ModuleSchemas names a namespace as a STRING"),

            ("    private Modules . Timesheet . Timesheet? _spaced;",
             true, @"\s* around both dots — spaced dots compile"),

            ("        var run = new Modules.payroll.PayrollRun();",
             true, @"\w+ rather than [A-Z]\w+ — a lowercase segment is legal C#"),

            ("        var y = new Modules.X.Y();",
             true, @"\w+ rather than [A-Z]\w+ — a single-character segment is legal C#"),

            ("    // TODO: stop reaching into Modules.Timesheet.Timesheet here",
             false, "IsCode's // clause — commentary is not a dependency"),

            (@"    /// <see cref=""LeadingEDJE.Leap.Api.Modules.Timesheet.Foo""/>",
             true, "IsCode's cref exemption — a cref is compiler-bound (FR-010)"),

            (@"    /// <see cref = ""LeadingEDJE.Leap.Api.Modules.Timesheet.Foo""/>",
             true, @"\s* in CrefAttribute — XML allows whitespace around '='"),

            (@"    /// see xcref=""LeadingEDJE.Leap.Api.Modules.Timesheet.Foo""",
             false, @"\b in CrefAttribute — 'xcref' is prose, not an attribute"),

            // One control per \s* inside the OPTIONAL prefix. Each must keep the dot hard against
            // 'Modules', or the bare route rescues the mutant: delete one of these and the prefix
            // fails, the lookbehind then sees the '.' before 'Modules' and rejects — which is
            // exactly what makes the fragment killable. None of them is an import, so ModuleImport
            // cannot rescue them either.
            ("    private LeadingEDJE .Leap.Api.Modules.Timesheet.Timesheet? _a;",
             true, @"LeadingEDJE\s*\. in the optional prefix"),

            ("    private LeadingEDJE. Leap.Api.Modules.Timesheet.Timesheet? _b;",
             true, @"\.\s*Leap in the optional prefix"),

            ("    private LeadingEDJE.Leap .Api.Modules.Timesheet.Timesheet? _c;",
             true, @"Leap\s*\. in the optional prefix"),

            ("    private LeadingEDJE.Leap. Api.Modules.Timesheet.Timesheet? _d;",
             true, @"\.\s*Api in the optional prefix"),

            ("    private LeadingEDJE.Leap.Api .Modules.Timesheet.Timesheet? _e;",
             true, @"Api\s*\. in the optional prefix"),

            (@"        (""LeadingEDJE . Leap . Api . Modules . Timesheet"", ""timesheet""),",
             false, @"the trailing \. SURVIVES the whitespace widening — still a STRING, not a reference"),
        };

        // Act & Assert — a coverage fact over zero controls passes vacuously, which is the failure
        // that hides all the others.
        controls.Length.ShouldBeGreaterThan(
            0, "this fact must actually iterate something");

        foreach (var (line, fires, fragment) in controls)
        {
            ReferencesAModule(line).ShouldBe(
                fires,
                $"this control pins {fragment} — deleting that fragment must turn this assertion "
                    + $"red: {line}");
        }
    }

    // ---------------------------------------------------------------- the comment filter

    /// <summary>
    /// FR-014(d). Matching qualified references is only safe if commentary is skipped: without it a
    /// Platform file that merely MENTIONS a module type in prose is flagged and needs an allowlist
    /// entry, polluting the very count this gate exists to make trustworthy.
    /// </summary>
    [Fact]
    public void TheScan_SkipsCommentary_ButNotTheSameTextAsCode()
    {
        // Arrange — each pair is one payload written twice. The code half is what keeps the
        // commentary half honest: a negative control whose payload never matched anything would
        // pass with the filter deleted, which is the vacuous-guard failure this repo keeps hitting.
        var pairs = new[]
        {
            (Commentary: "    /// Drained by Modules.Timesheet.Processor in the module.",
             Code: "    Drained by Modules.Timesheet.Processor in the module."),
            (Commentary: "    // TODO: stop reaching into Modules.Timesheet.Timesheet here",
             Code: "    var here = new Modules.Timesheet.Timesheet();"),
            // Was "     * Modules.Timesheet.Timesheet is what this used to construct." — a block
            // comment's continuation line. The filter no longer honours a leading '*', and rightly:
            // that prefix belongs to real code far more often than to a comment in this repo (124
            // operator-leading continuation lines under api/, versus one '*' line under
            // api/Platform, which is CSS). The repo's actual comment style is '///'.
            (Commentary: "    /// Modules.Timesheet.Timesheet is what this used to construct.",
             Code: "    Modules.Timesheet.Timesheet is what this used to construct."),
        };

        // Act & Assert
        foreach (var (commentary, code) in pairs)
        {
            ReferencesAModule(commentary).ShouldBeFalse(
                "a mention in commentary is not a dependency: " + commentary);

            ReferencesAModule(code).ShouldBeTrue(
                "the same payload outside a comment MUST fire, or the line above passes for the "
                    + "wrong reason: " + code);
        }
    }

    // ---------------------------------------------------------------- negative controls

    [Fact]
    public void TheDetector_DoesNotFireOnPlatformImports()
    {
        // Arrange
        var benign = new[]
        {
            "using LeadingEDJE.Leap.Api.Platform.Data;",
            "using LeadingEDJE.Leap.Api.Platform.Interfaces;",
            "using Microsoft.Extensions.DependencyInjection;",

            // A PLATFORM namespace whose own last segment is the word "Modules". The alias branch
            // is the only fragment that consumes text before the namespace, so it is the only one
            // that can newly over-match — these three are its guard.
            "using LeadingEDJE.Leap.Api.Platform.Modules;",
            "using LeadingEDJE.Leap.Api.Platform.Modules.Registry;",
            "using ModuleRegistry = LeadingEDJE.Leap.Api.Platform.Modules.Registry.ModuleRegistry;",

            // A commented-out import is not an import.
            "// using LeadingEDJE.Leap.Api.Modules.Timesheet;",

            // The same Platform-namespace guard as CODE rather than as an import — the qualified
            // pattern's lookbehind is what keeps 'Platform.Modules.' from reading as 'Modules.'.
            "    var registry = Platform.Modules.Registry.ModuleRegistry.Empty;",
        };

        // Act & Assert — through the real scan predicate, so both patterns are covered.
        foreach (var line in benign)
        {
            ReferencesAModule(line).ShouldBeFalse(
                $"the detector must not fire on a non-module import: {line}");
        }
    }

    // ---------------------------------------------------------------- helpers

    private sealed record SourceFile(string RelativePath, string Text);

    private static List<SourceFile> PlatformSourceFiles()
    {
        var root = RepositoryRoot();
        var platformRoot = Path.Combine(root, PlatformDirectory);
        if (!Directory.Exists(platformRoot))
        {
            throw new DirectoryNotFoundException(
                $"found nothing to inspect: '{PlatformDirectory}' does not exist under '{root}'");
        }

        var files = Directory
            .EnumerateFiles(platformRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => new SourceFile(
                Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(path)))
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                "found nothing to inspect: no C# files found under Platform");
        }

        return files;
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
                "found nothing to inspect: could not locate the repository root (no leap.slnx above "
                    + AppContext.BaseDirectory + ")");
    }
}
