using System.Text.RegularExpressions;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// The CONSUMER-side directory boundary gate: no module outside Compass may read Compass's own
/// entities, and no module outside Compass may reach directory data through the shared data context.
/// </summary>
/// <remarks>
/// <para>
/// This runs the opposite direction to <c>CompassBoundaryTests</c>, which is why it has to exist
/// separately. That suite checks that Compass does not reach into its siblings. Its own
/// comments say so. Constitution Principle V's first bullet is the other direction — a consuming
/// module must not read Directory tables — and nothing checked it. That is exactly how Known Gap
/// <c>#142</c> survived undetected, and the Constitution says in as many words: do not cite
/// <c>CompassBoundaryTests</c> as enforcement of Principle V against a consumer.
/// </para>
/// <para>
/// Verified by construction, not by inspection. A gate that has never failed has never run. A
/// path regex matching zero files does not fail — it PASSES — and eight gates in this repository have
/// been found silently fail-open for that reason. This feature already produced a ninth: the
/// cross-schema foreign-key query in <c>CatalogAssertions</c> looked correct, and detected nothing
/// until a real violation was planted against a live database. So this file carries its own
/// non-vacuity assertions AND a positive control that proves the detector fires.
/// </para>
/// <para>
/// Why OOTO is an exception rather than a violation to fix. Owner decision D-7 (2026-08-10):
/// <c>compass.employee</c> cannot serve OOTO. Of the four conditions § 1b lists, THREE are now
/// CONSUMED, not merely satisfied. The first: <c>compass.employee</c> carries an IANA <c>Timezone</c> (#420),
/// both boundary transports publish it (#422), and OOTO reads it from there (#424, feature 018) —
/// the first attribute ever to leave the frozen tables. The second: employment status is derived
/// from the correlated record's <c>EmployeeType</c> (#429), so no query in this module reads
/// <c>employees.EmploymentStatus</c>. The third: the platform resolves an authenticated CALLER to a
/// Compass employee through <c>ICallerDirectory</c>, and both OOTO services identify the caller
/// through it (#427, feature 019), so <c>Person.EdjeId</c> is gone from this module.
/// What still blocks is counted in <c>severance-record.md</c> § 1b and nowhere else.
/// Adding the remaining fields to Compass would be the speculative modelling Principle II forbids.
/// Issue #428 dropped the last of it: <c>ooto.out_of_office_events.employee_id</c> and its cascading
/// FK are gone, ownership moved onto the Compass id, and <c>OotoService.ResolveLegacyRowKeyAsync</c>
/// was retired — so NO OOTO file reads the legacy directory tables any more. <c>#142</c> stays open
/// for Timesheet's own reads (enumerated below); this gate's job is to stop the problem GROWING.
/// </para>
/// <para>
/// The OOTO reads have SHRUNK to zero, which is the intended direction. Issue #430 moved
/// <c>OotoDirectoryService</c>'s <c>clients</c> and <c>assignments</c> reads to Compass; #428 then
/// moved its EMPLOYEE reads and retired <c>OotoService</c>'s legacy row-key read, so
/// <see cref="TheAllowlist_ContainsNoStaleEntry"/> forced both OOTO entries off this list.
/// </para>
/// </remarks>
public class DirectoryConsumerBoundaryTests
{
    private const string ApiDirectory = "api";
    private const string CompassModuleDirectory = "api/Modules/Compass";

    /// <summary>
    /// C#'s <c>identifier_part_character</c> set (ECMA-334 §6.4.3) — letter, combining mark, decimal
    /// digit, connector, or format character.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used instead of <c>\b</c>, deliberately. <c>\b</c>'s word set is <c>\w</c> plus only
    /// U+200C/U+200D, so every other format character slips through — and Cf characters are legal in a
    /// C# identifier. Verified by compiling it: <c>namespace Foo.Contracts&#x00AD;Internal</c> builds
    /// clean and is a namespace distinct from <c>Foo.Contracts</c>. With <c>\b</c> the exemption below
    /// would stop flagging it, and today's detector flags it — so <c>\b</c> would WEAKEN an existing
    /// gate, which Principle V forbids.
    /// </para>
    /// </remarks>
    private const string CSharpIdentifierPart = @"[\p{L}\p{Nl}\p{Nd}\p{Mn}\p{Mc}\p{Pc}\p{Cf}]";

    /// <summary>
    /// A reference to a Compass module internal from a file outside that module. Matches the
    /// import form and the fully-qualified form, and exempts the published contract namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exemption exists because Principle V otherwise contradicts itself (spec 013, #451):
    /// its first bullet tells a consuming module to call <c>IDirectory</c>, and this gate fails any
    /// outside file that names the Compass namespace — which is the only route to <c>IDirectory</c>.
    /// Homing the contract in <c>…Compass.Contracts</c> and exempting exactly that segment lets both
    /// rules hold. Every internal namespace stays flagged.
    /// </para>
    /// <para>
    /// What the exemption actually covers, stated honestly: any reference whose next segment is
    /// exactly <c>Contracts</c>, including anything nested beneath it. No regex can separate a
    /// nested namespace from member access — <c>…Contracts.IDirectory.GetEmployeeAsync</c> must stay
    /// allowed — so the defence against a nested subtree is structural, not textual:
    /// <c>CompassBoundaryTests</c> asserts <c>api/Modules/Compass/Contracts/</c> contains no
    /// subdirectory.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than <c>private</c> so <c>CompassContractsExemptionParityTests</c>
    /// can assert this detector and <c>CompassBoundaryTests</c>' two agree. The three detectors
    /// anchor on three different strings and cannot share one literal, so nothing else stops one being
    /// loosened alone.
    /// </para>
    /// </remarks>
    internal static readonly Regex CompassModuleReference = new(
        $@"LeadingEDJE\.Leap\.Api\.Modules\.Compass(?!\.Contracts(?!{CSharpIdentifierPart}))",
        RegexOptions.Compiled);

    /// <summary>
    /// The frozen directory entities, singular CLR type name mapped to the plural <c>DbSet</c>
    /// property name <c>LeapDbContext</c> exposes for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Single source of truth for both regexes below, so the property-name list and the type-name
    /// list cannot drift out of sync — <c>Person</c> -&gt; <c>People</c> is irregular, and pluralizing
    /// it by hand a third time somewhere is exactly how the two lists would eventually disagree.
    /// </para>
    /// <para>
    /// Namespace-blind, by accepted scope, not oversight. A type name here matches
    /// <c>Set&lt;T&gt;()</c> or a DbSet property regardless of which module's <c>T</c> it is. Today
    /// that is safe: the only entities anywhere in the codebase sharing these simple names are the
    /// frozen Timesheet-module directory types this gate protects, and Compass's own same-named
    /// entities (<c>Employee</c>, <c>Client</c>, ...) are already out of scope because
    /// <see cref="SourceFilesOutsideCompass"/> excludes the whole module. A THIRD module minting its
    /// own <c>Client</c> or <c>Assignment</c> type would need this gate to become namespace-aware
    /// (full semantic analysis, not a text regex) — deliberately not built ahead of that need.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> FrozenDirectoryEntityPropertyNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Employee"] = "Employees",
            ["Client"] = "Clients",
            ["Assignment"] = "Assignments",
            ["Person"] = "People",
            ["JobTitle"] = "JobTitles",
            ["TitleCategory"] = "TitleCategories",
            ["EmploymentRecord"] = "EmploymentRecords",
        };

    /// <summary>
    /// A read of the legacy directory data through the shared context — <c>db.Employees</c>,
    /// <c>context.Clients</c>, <c>db.Assignments</c>, <c>context.People</c> and their kin.
    /// </summary>
    private static readonly Regex SharedContextDirectoryRead = new(
        $@"\b(db|context|dbContext|_context|_db)\.({string.Join('|', FrozenDirectoryEntityPropertyNames.Values)})\b",
        RegexOptions.Compiled);

    /// <summary>
    /// A frozen directory entity reached through the GENERIC <c>Set&lt;T&gt;()</c> form rather than the
    /// named DbSet property <see cref="SharedContextDirectoryRead"/> matches (FR-003).
    /// </summary>
    /// <remarks>
    /// This is the bypass rule two's property-name regex cannot see: <c>context.Set&lt;Employee&gt;()</c>
    /// reaches the exact same table as <c>context.Employees</c> without matching either word. Compass's
    /// own repositories legitimately use this form against <c>compass.*</c> entities, which is why this
    /// regex alone is not the gate — <see cref="LeapDbContextInjection"/> narrows it to consumers that
    /// hold the context directly, and <see cref="SourceFilesOutsideCompass"/> already excludes Compass.
    /// </remarks>
    private static readonly Regex GenericDirectoryEntitySetAccess = new(
        $@"\bSet<({string.Join('|', FrozenDirectoryEntityPropertyNames.Keys)})>\(\)",
        RegexOptions.Compiled);

    /// <summary>
    /// A constructor parameter (primary or classic) typed <c>LeapDbContext</c> — the shape FR-003
    /// forbids for a directory consumer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately narrow to the constructor-parameter shape rather than "the type name appears
    /// anywhere" — <c>LeapDbContext</c> is also named in comments, cref docs, and DI registration
    /// (<c>Program.cs</c>), none of which is an injection. Every DI consumer in this codebase captures
    /// the parameter directly (no separate <c>private readonly LeapDbContext _context;</c> field with a
    /// same-named different-typed parameter), so this single shape covers the real injection sites.
    /// </para>
    /// <para>
    /// Deliberately NOT "any LeapDbContext injection outside Compass." <c>AuditService</c>,
    /// <c>SystemSettingService</c>, <c>UserRoleService</c>, the notification and seeding services, and
    /// others all inject it legitimately for reasons that have nothing to do with directory data — a
    /// bare injection check would need an allowlist entry for each of them, diluting this gate's actual
    /// concern (FR-003: directory data specifically) with unrelated platform persistence. This regex is
    /// therefore only ever evaluated together with a directory-entity-access match below, never alone.
    /// </para>
    /// </remarks>
    private static readonly Regex LeapDbContextInjection = new(
        @"\bLeapDbContext\??\s+\w+\s*[,)]",
        RegexOptions.Compiled);

    /// <summary>
    /// Files permitted to read legacy directory data through the shared context, each with its reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Individually named on purpose. A path exclusion covering <c>api/Modules/Ooto/</c> would
    /// be shorter and would also hide a future Compass violation living in that directory. Naming the
    /// three files means a fourth OOTO consumer acquiring a directory read fails this gate.
    /// </para>
    /// <para>
    /// The unblocking conditions close one at a time, by three different routes.
    /// Timezone closed by adding to the Compass MODEL (`#424`); employment status by
    /// redesigning this CONSUMER against the published <c>CompassEmployeeDto.EmployeeType</c>
    /// (`#429`), modelling nothing new; caller identity by inverting a DEPENDENCY behind Platform's
    /// <c>ICallerDirectory</c> (`#427`, ADR-011), adding no column and no entity. Prefer the last
    /// two shapes: neither needs a Compass data-model decision.
    /// </para>
    /// <para>
    /// Nothing unfreezes until every condition closes. A shrinking reason string is the list doing
    /// its job — it is not permission to remove an entry whose file still crosses. Note the
    /// stale-entry rule below is scoped to the FILE, so it cannot notice a reason that has gone out
    /// of date; that half is on whoever migrates the next attribute.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> AllowedLegacyDirectoryReads =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Platform-owned, and NOT part of the Directory-boundary question. Person is now a
            // native Platform entity (api/Platform/Domain/Person.cs) rather than a Compass/Timesheet
            // directory table, so a Platform file reading `people` is reading its own data, not
            // consuming Compass's directory. Listed so the gate stays exact rather than being
            // loosened to accommodate them.
            ["api/Platform/Services/PersonProvisioningService.cs"] =
                "the sign-in pipeline provisions and reads `people`, a native Platform entity",
            ["api/Platform/Services/AuditEntityDescriptionResolver.cs"] =
                "resolves human-readable audit descriptions for `people` rows, a native Platform "
                    + "entity",
            ["api/Platform/Data/Repositories/PersonRepository.cs"] =
                "Platform's own repository over its own `people` entity",
            ["api/Platform/Services/PersonEmployeeDirectory.cs"] =
                "IEmployeeDirectory backed directly by Platform's own `people` entity, replacing the "
                    + "module-owned implementation the Timesheet/Ooto removal deleted",

            // Every other entry here named a file in api/Modules/Timesheet/ or a repository for a
            // Compass/Timesheet directory table (Employee, Client, Assignment, JobTitle,
            // EmploymentRecord). All were deleted with the Timesheet/Ooto module removal, and the
            // stale-entry check below would force them out one at a time anyway — removed together
            // here since the whole module went at once.
        };

    // ---------------------------------------------------------------- rule one

    [Fact]
    public void NoConsumerOutsideCompass_ReferencesACompassEntity()
    {
        // Arrange
        var files = SourceFilesOutsideCompass();

        // Act
        var violations = files
            .Where(f => CompassModuleReference.IsMatch(f.Text))
            .Where(f => !IsCompositionRoot(f.RelativePath))
            .Select(f => f.RelativePath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        // Assert
        files.Count.ShouldBeGreaterThan(
            0, "the gate inspected ZERO files outside Compass and would have passed for free");

        violations.ShouldBeEmpty(
            "a module outside Compass reaches into the Compass module. Constitution Principle V "
                + "requires directory data to be consumed through IDirectory, never by touching "
                + "Compass internals. Route the read through the contract. Offenders: "
                + string.Join(", ", violations));
    }

    // ---------------------------------------------------------------- rule two

    [Fact]
    public void LegacyDirectoryReads_AreExactlyTheAllowlistedFiles()
    {
        // Arrange
        var files = SourceFilesOutsideCompass();

        // Act
        var reading = files
            .Where(f => SharedContextDirectoryRead.IsMatch(f.Text))
            .Select(f => f.RelativePath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var unexpected = reading
            .Where(p => !AllowedLegacyDirectoryReads.ContainsKey(p))
            .ToList();

        // Assert
        files.Count.ShouldBeGreaterThan(
            0, "the gate inspected ZERO files and would have passed for free");

        unexpected.ShouldBeEmpty(
            "a NEW file reads legacy directory data through the shared data context. The surviving "
                + "reads are frozen at a named set (D-7) and the set is not open for extension — the "
                + "whole point is that this problem stops growing while OOTO waits on Compass's data "
                + "model. Add a method to IDirectory instead. Offenders: "
                + string.Join(", ", unexpected));
    }

    // ---------------------------------------------------------------- rule three

    /// <summary>
    /// No type outside Compass injects <c>LeapDbContext</c> to reach a frozen directory entity (FR-003).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gap this closes. Rule two catches USAGE of the named DbSet property
    /// (<c>db.Employees</c>); none of the seven existing assertions in this file checked the
    /// DEPENDENCY itself. 001's T200 was written for exactly this ("no consuming module injects the
    /// Directory data context") and was never built. FR-003 states the property directly: a consuming
    /// module depends on <c>IDirectory</c>, not on the database.
    /// </para>
    /// <para>
    /// Reuses <see cref="AllowedLegacyDirectoryReads"/> rather than a second dictionary. Every
    /// file that reaches a frozen directory entity necessarily holds a <c>LeapDbContext</c> to do it,
    /// so today's injecting-and-reaching set is exactly rule two's already-allowlisted set — this
    /// assertion adds no new entries, only new COVERAGE: a future file using
    /// <c>context.Set&lt;Employee&gt;()</c> instead of <c>context.Employees</c> would satisfy rule two's
    /// narrower regex by accident but fails here, where the failure message names the actual
    /// remediation (depend on <c>IDirectory</c>) rather than "add to the allowlist."
    /// </para>
    /// </remarks>
    [Fact]
    public void NoConsumerOutsideCompass_InjectsLeapDbContextToReachDirectoryData()
    {
        // Arrange
        var files = SourceFilesOutsideCompass();

        // Act — injects the context AND reaches a frozen entity, by either detection shape.
        var reaching = files
            .Where(f => LeapDbContextInjection.IsMatch(f.Text))
            .Where(f => SharedContextDirectoryRead.IsMatch(f.Text)
                || GenericDirectoryEntitySetAccess.IsMatch(f.Text))
            .Select(f => f.RelativePath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var unexpected = reaching
            .Where(p => !AllowedLegacyDirectoryReads.ContainsKey(p))
            .ToList();

        // Assert
        files.Count.ShouldBeGreaterThan(
            0, "the gate inspected ZERO files and would have passed for free");

        unexpected.ShouldBeEmpty(
            "a NEW file injects LeapDbContext directly to reach directory data. Constitution Principle "
                + "V and FR-003 require a consuming module to depend on IDirectory, not on the database "
                + "— add a method to IDirectory instead of injecting the context. Offenders: "
                + string.Join(", ", unexpected));
    }

    [Fact]
    public void TheAllowlist_ContainsNoStaleEntry()
    {
        // Arrange — mirrors CompassBoundaryTests rule three: an exception whose file stops crossing
        // must FAIL, so the list shrinks as OOTO migrates rather than rotting into permanent
        // permission. Without this, the allowlist outlives the reason it was written.
        //
        // Scoped to SharedContextDirectoryRead only, matching rule two — not the broader
        // NoConsumerOutsideCompass_InjectsLeapDbContextToReachDirectoryData check above. Every current
        // allowlist entry reaches directory data through the named-property form these two rules share,
        // so this is not a gap today; it would only matter for a hypothetical future entry justified
        // solely by the generic Set<T>() form, which does not exist yet.
        var files = SourceFilesOutsideCompass();
        var reading = files
            .Where(f => SharedContextDirectoryRead.IsMatch(f.Text))
            .Select(f => f.RelativePath)
            .ToList();

        // Act
        var stale = AllowedLegacyDirectoryReads.Keys
            .Where(p => !reading.Contains(p, StringComparer.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        // Assert
        stale.ShouldBeEmpty(
            "an allowlist entry no longer reads legacy directory data — delete it so the exception "
                + "stays as small as the code requires. Stale entries: " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryAllowlistEntry_CarriesAReason()
    {
        // Arrange & Act — an undocumented exception is indistinguishable from an oversight.
        var missing = AllowedLegacyDirectoryReads
            .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => kv.Key)
            .ToList();

        // Assert
        missing.ShouldBeEmpty(
            "every allowlisted directory read must state WHY it is permitted: " + string.Join(", ", missing));
    }

    // ------------------------------------------------------- positive controls

    [Fact]
    public void TheDetector_FindsACompassReference_WhenOneExists()
    {
        // Arrange — a synthetic file body, so the control needs no real violation on disk.
        const string offending = "using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;";

        // Act & Assert
        CompassModuleReference.IsMatch(offending).ShouldBeTrue(
            "the Compass-reference detector must fire on an import of the Compass module, or rule "
                + "one can never fail");
    }

    // ------------------------------------- the published-contract exemption (#451)

    /// <summary>
    /// The published contract namespace is referenceable from outside the module — that is the entire
    /// point of homing it separately (spec 013, #451).
    /// </summary>
    /// <remarks>
    /// <para>
    /// These controls are the ONLY verification of the exemption. Measured at `51eac2fb`: of 417
    /// production files under <c>api/</c> outside Compass, exactly one names the Compass namespace —
    /// <c>api/Program.cs</c>, which <see cref="IsCompositionRoot"/> already excuses. So rule one's file
    /// sweep matches ZERO non-exempt files, and a wrong refinement would pass CI in silence. A gate
    /// that matches nothing cannot fail; only a synthetic control can.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDetector_DoesNotFireOnThePublishedContractNamespace()
    {
        // Arrange — every shape a legitimate consumer of the published contract can take.
        var allowed = new[]
        {
            // A1 — the ordinary import.
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;",

            // A2 — the same import inside a real file body.
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;\r\n\r\n"
                + "namespace LeadingEDJE.Leap.Api.Modules.Ooto;\r\n\r\n"
                + "internal sealed class Consumer(IDirectory directory);\r\n",

            // A3 — inline fully-qualified, with no using directive at all.
            "private readonly LeadingEDJE.Leap.Api.Modules.Compass.Contracts.IDirectory _directory;",

            // A4 — a using ALIAS to a published type.
            "using Dir = LeadingEDJE.Leap.Api.Modules.Compass.Contracts.IDirectory;",

            // A5 — two legitimate references in one file must not compound into a violation.
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;\r\n"
                + "// see LeadingEDJE.Leap.Api.Modules.Compass.Contracts.CompassEmployeeDto\r\n",
        };

        // Act & Assert
        foreach (var text in allowed)
        {
            CompassModuleReference.IsMatch(text).ShouldBeFalse(
                "the published contract namespace must be referenceable from outside Compass — "
                    + "Principle V tells a consumer to call IDirectory, so the gate that enforces "
                    + $"Principle V cannot reject naming it. Fired on: {text}");
        }
    }

    /// <summary>
    /// Every character C# can legally place after the <c>Contracts</c> segment keeps the exemption.
    /// </summary>
    /// <remarks>
    /// A guard that only works before a semicolon would reject a constructor parameter, a generic
    /// argument or an end-of-file reference — all legal, all a consumer's normal usage.
    /// </remarks>
    [Fact]
    public void TheDetector_DoesNotFireOnThePublishedContract_AfterAnyLegalTerminator()
    {
        // Arrange — A6: `;` `.` `)` `,` `>` `=` `{`, a space, end-of-string, and a newline.
        const string Reference = "LeadingEDJE.Leap.Api.Modules.Compass.Contracts";
        var terminators = new[] { ";", ".", ")", ",", ">", "=", "{", " ", string.Empty, "\r\n" };

        // Act & Assert
        foreach (var terminator in terminators)
        {
            CompassModuleReference.IsMatch(Reference + terminator).ShouldBeFalse(
                "the exemption must survive every legal terminator, or ordinary consumer code is "
                    + $"rejected. Fired on terminator: '{terminator}'");
        }
    }

    /// <summary>
    /// A namespace whose next segment merely BEGINS with <c>Contracts</c> is still an internal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trap this closes: a naive exemption written as "followed by <c>.Contracts</c>" also exempts
    /// <c>.ContractsInternal</c>. The guard is a C# identifier-part class rather than <c>\b</c>, because
    /// <c>\b</c>'s word set admits only U+200C/U+200D and lets any other format character split the
    /// segment — <c>Contracts­Internal</c> compiles as a distinct namespace and is flagged today.
    /// Losing that would weaken an existing gate, which Principle V forbids.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDetector_StillFiresOnANamespaceThatMerelyStartsWithContracts()
    {
        // Arrange
        var offending = new[]
        {
            "using LeadingEDJE.Leap.Api.Modules.Compass.ContractsInternal;",
            "using LeadingEDJE.Leap.Api.Modules.Compass.ContractsFoo;",
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts­Internal;",
        };

        // Act & Assert
        foreach (var text in offending)
        {
            CompassModuleReference.IsMatch(text).ShouldBeTrue(
                $"only the exact `Contracts` segment is exempt, never a longer word: {text}");
        }
    }

    /// <summary>
    /// Every Compass INTERNAL namespace stays forbidden, and a published import cannot launder a file.
    /// </summary>
    [Fact]
    public void TheDetector_StillFiresOnEveryCompassInternalNamespace()
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

            // A published import must NEVER launder an internal one in the same file.
            "using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;\r\n"
                + "using LeadingEDJE.Leap.Api.Modules.Compass.Services;\r\n",
        };

        // Act & Assert
        foreach (var text in offending)
        {
            CompassModuleReference.IsMatch(text).ShouldBeTrue(
                "widening the gate to admit the published contract must not admit any internal "
                    + $"namespace. Did not fire on: {text}");
        }
    }

    [Fact]
    public void TheDetector_FindsASharedContextDirectoryRead_WhenOneExists()
    {
        // Arrange — the exact shape OotoDirectoryService uses, plus two spellings of the field.
        // If this stops matching, rule two silently permits everything.
        var offending = new[]
        {
            "var caller = await db.Employees.AsNoTracking().FirstOrDefaultAsync();",
            "var query = context.Clients.AsNoTracking();",
            "await _context.Assignments.AnyAsync();",
            "dbContext.People.Add(person);",
        };

        // Act & Assert
        foreach (var line in offending)
        {
            SharedContextDirectoryRead.IsMatch(line).ShouldBeTrue(
                $"the shared-context directory-read detector must fire on: {line}");
        }
    }

    [Fact]
    public void TheDetector_DoesNotFireOnUnrelatedCode()
    {
        // Arrange — guards against a detector so broad that rule two fails on everything, which
        // would get it disabled rather than fixed.
        var benign = new[]
        {
            "var entries = await db.TimeEntries.ToListAsync();",
            "// Employees are read through IDirectory.",
            "public DbSet<Employee> Employees => Set<Employee>();",
        };

        // Act & Assert
        foreach (var line in benign)
        {
            SharedContextDirectoryRead.IsMatch(line).ShouldBeFalse(
                $"the detector must not fire on unrelated code: {line}");
        }
    }

    [Fact]
    public void TheDetector_FindsAGenericDirectoryEntitySetAccess_WhenOneExists()
    {
        // Arrange — the Set<T>() bypass rule two's named-property regex would miss.
        var offending = new[]
        {
            "await context.Set<Employee>().AnyAsync();",
            "context.Set<Client>().AddRange(seedClients);",
        };

        // Act & Assert
        foreach (var line in offending)
        {
            GenericDirectoryEntitySetAccess.IsMatch(line).ShouldBeTrue(
                $"the generic Set<T>() directory-entity detector must fire on: {line}");
        }
    }

    [Fact]
    public void TheDetector_DoesNotFireOnAnUnrelatedSetAccess()
    {
        // Arrange — a Compass entity or an unrelated entity, neither of which is a frozen directory
        // type this gate protects.
        var benign = new[]
        {
            "await context.Set<TimeEntry>().AnyAsync();",
            "context.Set<CompassClient>().AsNoTracking();",
        };

        // Act & Assert
        foreach (var line in benign)
        {
            GenericDirectoryEntitySetAccess.IsMatch(line).ShouldBeFalse(
                $"the generic Set<T>() detector must not fire on an unrelated entity: {line}");
        }
    }

    [Fact]
    public void TheDetector_FindsALeapDbContextInjection_WhenOneExists()
    {
        // Arrange — the primary-constructor form (most of this codebase), the classic multi-line
        // constructor-parameter form (NotificationService's shape), and the NULLABLE form of each —
        // a bare "?" right after the type name must not defeat the detector.
        var offending = new[]
        {
            "public class OotoDirectoryService(LeapDbContext db) : IOotoDirectoryService",
            "    LeapDbContext dbContext,",
            "public class OotoDirectoryService(LeapDbContext? db) : IOotoDirectoryService",
            "    LeapDbContext? dbContext,",
        };

        // Act & Assert
        foreach (var line in offending)
        {
            LeapDbContextInjection.IsMatch(line).ShouldBeTrue(
                $"the LeapDbContext-injection detector must fire on: {line}");
        }
    }

    [Fact]
    public void TheDetector_DoesNotFireOnALeapDbContextMentionThatIsNotAnInjection()
    {
        // Arrange — DI registration, service-locator resolution, and a doc reference all NAME the
        // type without a consumer capturing it as its own dependency.
        var benign = new[]
        {
            "builder.Services.AddDbContext<LeapDbContext>(options => options.UseNpgsql(cs));",
            "var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();",
            "/// Do not \"consolidate\" this onto <c>LeapDbContext</c>.",
        };

        // Act & Assert
        foreach (var line in benign)
        {
            LeapDbContextInjection.IsMatch(line).ShouldBeFalse(
                $"the injection detector must not fire on a non-injection mention: {line}");
        }
    }

    // ------------------------------------------------------------------ helpers

    private sealed record SourceFile(string RelativePath, string Text);

    /// <summary>
    /// Every production C# file under <c>api/</c> that is NOT inside the Compass module. Throws
    /// rather than returning an empty set when the repository root cannot be resolved.
    /// </summary>
    private static List<SourceFile> SourceFilesOutsideCompass()
    {
        var root = RepositoryRoot();
        var apiRoot = Path.Combine(root, ApiDirectory);
        if (!Directory.Exists(apiRoot))
        {
            throw new DirectoryNotFoundException(
                $"found nothing to inspect: '{ApiDirectory}' does not exist under '{root}'");
        }

        var compassPrefix = CompassModuleDirectory.Replace('/', Path.DirectorySeparatorChar);

        var files = Directory
            .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => new SourceFile(
                Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(path)))
            .Where(f => !f.RelativePath.Replace('/', Path.DirectorySeparatorChar)
                .StartsWith(compassPrefix, StringComparison.Ordinal))
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                "found nothing to inspect: no production C# files outside the Compass module");
        }

        return files;
    }

    /// <summary>
    /// The composition root legitimately names every module in order to wire it up. Registering
    /// Compass is not consuming its internals.
    /// </summary>
    private static bool IsCompositionRoot(string relativePath) =>
        relativePath is "api/Program.cs";

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
