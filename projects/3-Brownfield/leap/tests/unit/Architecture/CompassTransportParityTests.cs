using System.Reflection;
using System.Text.RegularExpressions;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Enforces transport parity in the direction nothing else checks: every method on a PUBLISHED
/// Compass contract has an HTTP twin on the boundary endpoints, or an allowlist entry saying why not.
/// </summary>
/// <remarks>
/// <para>
/// Every existing gate reads the HTTP side. <c>CompassTransportContractTests</c> enumerates
/// <see cref="IDirectory"/>'s methods only to check the DTO namespace and shape uniqueness, and its
/// route counters are keyed on the handlers. So a contract method with no route was invisible: the
/// boundary reaches eleven methods with eight routes and no test, and no reviewer, could see the
/// asymmetry. This converts an invisible asymmetry into a recorded one — the next person adding a
/// boundary method has to state their intent.
/// </para>
/// <para>
/// Structure copied from <see cref="PlatformPurityTests"/>, which already solves exactly this
/// problem: a frozen allowlist carrying a reason per entry, a stale-entry check so it shrinks as the
/// exceptions are retired, a reason check so an entry cannot arrive bare, and non-vacuity assertions
/// so a scan that resolves nothing fails rather than passes.
/// </para>
/// <para>
/// Why a source scan and not reflection. A handler's payload type cannot identify which
/// contract method it serves: <see cref="IDirectory.GetClientAsync"/> and
/// <see cref="IDirectory.GetClientsAsync"/> both unwrap to <c>CompassClientDto</c>, and the three
/// assignment lookups all unwrap to <c>CompassAssignmentDto</c>, so a payload-keyed check would call
/// a family covered while one of its routes was missing. What a handler DOES name is the contract
/// method it delegates to, and that is a source-level fact. Commentary is excluded from the scan for
/// the obvious fail-open reason: a <c>&lt;see cref&gt;</c> naming a method must not be able to satisfy
/// the gate.
/// </para>
/// </remarks>
public class CompassTransportParityTests
{
    /// <summary>The published contract seam — the interfaces a consumer outside Compass may name.</summary>
    private const string PublishedContractsNamespace = "LeadingEDJE.Leap.Api.Modules.Compass.Contracts";

    /// <summary>
    /// The published HTTP boundary's endpoint files. Top directory only: the <c>Admin/</c>,
    /// <c>Read/</c> and <c>Write/</c> subtrees are the application surfaces ADR-008 keeps separate
    /// from the published boundary, so a route there is not the twin this gate is looking for.
    /// </summary>
    private const string BoundaryEndpointsDirectory = "api/Modules/Compass/Endpoints";

    /// <summary>
    /// Floors, not exact counts. A reflection or file scan that resolves FEWER items than exist is the
    /// fail-open shape this repository has produced repeatedly; an exact count would additionally fail
    /// on legitimate growth, which is friction that gets bumped without thought.
    /// </summary>
    private const int KnownContractInterfaceCount = 1;

    /// <summary>Eleven <see cref="IDirectory"/> methods, which is the whole published seam.</summary>
    private const int KnownContractMethodCount = 11;

    /// <summary>Four endpoint files sit directly under the boundary directory.</summary>
    private const int KnownBoundaryEndpointFileCount = 4;

    /// <summary>
    /// Every published contract method deliberately served on ONE transport only, with the reason.
    /// </summary>
    /// <remarks>
    /// Keyed <c>Interface.Method</c>. An entry here is a decision, not a to-do: it says the method is
    /// in-process-only on purpose. <see cref="TheAllowlist_ContainsNoStaleEntry"/> deletes it the day
    /// a route appears, so the list cannot outlive its reasons.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> InProcessOnlyAllowlist =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IDirectory.GetClientsAsync"] =
                "In-process only (issue #430): the client list exists for a consumer that reads it "
                    + "inside the process, and no consumer reads it over the wire. A route would put "
                    + "an unpaged read of every client on the published contract for nobody, and "
                    + "/api/compass/v1 has a committed snapshot that check-openapi-contract.sh gates",

            ["IDirectory.GetEmployeeByEmailAsync"] =
                "In-process only by decision (issue #424, spec 018 deviation D-3): an employee's "
                    + "email address in a URL is personal data in every access log, proxy log and "
                    + "referrer along the path, where an opaque integer identifier is not — and "
                    + "there is no out-of-monolith consumer to serve while OAuth2 "
                    + "client-credentials (ADR-005) is unbuilt. Publishing later is additive within "
                    + "v1 and so always available; publishing now could not be withdrawn",

            ["IDirectory.GetEmployeesByEmailAsync"] =
                "In-process only for the same reason as its single-key sibling (issue #424, spec "
                    + "018 deviation D-3): a batch of email addresses in a request is the same "
                    + "personal data in the same logs, and its only consumer resolves a page of "
                    + "employees inside the process",

            ["IDirectory.GetEmployeesAsync"] =
                "In-process only (issue #428, OOTO cutover): the unpaged directory list exists for "
                    + "OOTO's accessible-employee and report reads, which walk it inside the process. "
                    + "A route would put an unpaged read of every employee on the published contract "
                    + "for nobody, and /api/compass/v1 has a committed snapshot check-openapi-contract.sh gates",
        };

    // ---------------------------------------------------------------- main rule

    /// <summary>
    /// Every published contract method has an HTTP twin, unless it is allowlisted as in-process-only.
    /// </summary>
    [Fact]
    public void EveryPublishedContractMethod_HasABoundaryHandler_UnlessAllowlisted()
    {
        // Arrange
        var methods = PublishedContractMethods();
        var code = BoundaryEndpointCode();

        // Non-vacuity: an empty method set or an empty scan text satisfies the assertion for free,
        // which is the failure mode that hides every other one.
        methods.Count.ShouldBeGreaterThanOrEqualTo(
            KnownContractMethodCount,
            $"the reflection scan found {methods.Count} published contract method(s), fewer than the "
                + $"{KnownContractMethodCount} that exist — it is inspecting the wrong namespace, and "
                + "a scan over too few items passes for free");
        code.Length.ShouldBeGreaterThan(
            0, $"the scan of {BoundaryEndpointsDirectory} produced no code text to search");

        // Act
        var untwinned = methods
            .Where(method => !IsCalledIn(code, method.MethodName))
            .Select(method => method.Key)
            .Where(key => !InProcessOnlyAllowlist.ContainsKey(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        // Assert
        untwinned.ShouldBeEmpty(
            "a published Compass contract method has no handler under "
                + $"{BoundaryEndpointsDirectory}, so the in-process and HTTP transports have drifted "
                + "apart. Either add the route, or add the method to this file's allowlist with the "
                + "reason it is in-process-only. Untwinned: " + string.Join(", ", untwinned));
    }

    // ---------------------------------------------------------------- stale-entry check

    /// <summary>
    /// No allowlist entry survives its reason: an entry must name a real method that still has no
    /// handler.
    /// </summary>
    [Fact]
    public void TheAllowlist_ContainsNoStaleEntry()
    {
        // Arrange
        var methods = PublishedContractMethods();
        var code = BoundaryEndpointCode();
        var keys = methods.Select(method => method.Key).ToHashSet(StringComparer.Ordinal);

        var twinned = methods
            .Where(method => IsCalledIn(code, method.MethodName))
            .Select(method => method.Key)
            .ToHashSet(StringComparer.Ordinal);

        // Non-vacuity.
        keys.Count.ShouldBeGreaterThanOrEqualTo(KnownContractMethodCount);

        // Act
        var unknown = InProcessOnlyAllowlist.Keys
            .Where(key => !keys.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        var nowTwinned = InProcessOnlyAllowlist.Keys
            .Where(twinned.Contains)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        // Assert
        unknown.ShouldBeEmpty(
            "an allowlist entry names no published contract method — the method was renamed or "
                + "removed, so delete the entry: " + string.Join(", ", unknown));
        nowTwinned.ShouldBeEmpty(
            "an allowlisted method now HAS a boundary handler, so it is no longer in-process-only — "
                + "delete the entry rather than leaving an exception that protects nothing: "
                + string.Join(", ", nowTwinned));
    }

    // ---------------------------------------------------------------- reason check

    /// <summary>Every allowlist entry states why the method is served on one transport only.</summary>
    [Fact]
    public void EveryAllowlistEntry_CarriesAReason()
    {
        // Arrange & Act
        var bare = InProcessOnlyAllowlist
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => entry.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        // Assert
        bare.ShouldBeEmpty(
            "every in-process-only method must state WHY it has no HTTP twin: "
                + string.Join(", ", bare));
    }

    // ---------------------------------------------------------------- positive controls

    /// <summary>
    /// The scan really does see the boundary, measured rather than argued.
    /// </summary>
    /// <remarks>
    /// Without this, a mistyped directory or a namespace rename would make every set above empty and
    /// every assertion pass. <see cref="IDirectory.GetEmployeeAsync"/> is the one method that has had
    /// an HTTP twin since the boundary's first slice.
    /// </remarks>
    [Fact]
    public void TheScan_FindsTheBoundaryItInspects()
    {
        // Arrange & Act
        var interfaces = PublishedContractInterfaces();
        var files = BoundaryEndpointFiles();
        var code = BoundaryEndpointCode();

        // Assert
        interfaces.Count.ShouldBeGreaterThanOrEqualTo(
            KnownContractInterfaceCount,
            $"expected at least {KnownContractInterfaceCount} published contract interfaces under "
                + $"{PublishedContractsNamespace}; found {interfaces.Count}");
        files.Count.ShouldBeGreaterThanOrEqualTo(
            KnownBoundaryEndpointFileCount,
            $"expected at least {KnownBoundaryEndpointFileCount} endpoint files directly under "
                + $"{BoundaryEndpointsDirectory}; found {files.Count}");
        IsCalledIn(code, nameof(IDirectory.GetEmployeeAsync)).ShouldBeTrue(
            $"{nameof(IDirectory.GetEmployeeAsync)} has had an HTTP twin since the boundary's first "
                + "slice, so a scan that cannot find it is broken rather than clean");
    }

    /// <summary>The detector fires on a delegation and not on prose that merely names the method.</summary>
    /// <remarks>
    /// The negative cases are the ones that matter: a <c>&lt;see cref&gt;</c> or a line comment naming
    /// a method must not be able to satisfy the gate, because that is the shape in which a missing
    /// route hides behind its own documentation.
    /// </remarks>
    [Fact]
    public void TheDetector_SeesADelegation_AndNotAMention()
    {
        // Arrange & Act & Assert — real delegations, including the spacing C# permits.
        IsCalledIn("await directory.GetClientsAsync(cancellationToken);", "GetClientsAsync")
            .ShouldBeTrue("a plain delegation must be seen");
        IsCalledIn("await directory . GetClientsAsync (token);", "GetClientsAsync")
            .ShouldBeTrue("C# permits whitespace around the dot and before the parenthesis");

        // Mentions, which must not count.
        IsCalledIn("/// <see cref=\"IDirectory.GetClientsAsync\"/>", "GetClientsAsync")
            .ShouldBeFalse("a cref is documentation, not a route");
        IsCalledIn("// TODO: expose directory.GetClientsAsync(ct) over HTTP", "GetClientsAsync")
            .ShouldBeFalse("a commented-out delegation is not a route");
        IsCalledIn("var name = nameof(GetClientsAsync);", "GetClientsAsync")
            .ShouldBeFalse("an unqualified mention is not a delegation to the contract");
        IsCalledIn("await directory.GetClientAsync(id, ct);", "GetClientsAsync")
            .ShouldBeFalse("the singular lookup must not satisfy the collection read");
    }

    // ---------------------------------------------------------------- helpers

    private sealed record ContractMethod(string InterfaceName, string MethodName)
    {
        public string Key => $"{InterfaceName}.{MethodName}";
    }

    private static List<Type> PublishedContractInterfaces() =>
        [.. typeof(IDirectory)
            .Assembly.GetTypes()
            .Where(type => type.IsInterface && type.Namespace == PublishedContractsNamespace)
            .OrderBy(type => type.Name, StringComparer.Ordinal)];

    private static List<ContractMethod> PublishedContractMethods() =>
        [.. PublishedContractInterfaces()
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => !method.IsSpecialName)
                .Select(method => new ContractMethod(type.Name, method.Name)))
            .OrderBy(method => method.Key, StringComparer.Ordinal)];

    /// <summary>
    /// Does <paramref name="code"/> DELEGATE to <paramref name="methodName"/> — a dot, the name, then
    /// an open parenthesis — on a line the compiler reads?
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole decision lives here rather than being split between a text-assembling step and a
    /// regex, so it can be asserted directly against synthetic input — see
    /// <see cref="TheDetector_SeesADelegation_AndNotAMention"/>. A detector spread across two places
    /// gets half-tested.
    /// </para>
    /// <para>
    /// Line comments are dropped, and that covers <c>///</c>. A <c>&lt;see cref&gt;</c> naming
    /// a method must not satisfy this gate: that is precisely how a missing route would hide behind
    /// its own documentation. A block comment spanning a delegation is still read, which is the
    /// fail-CLOSED direction — it can only report a commented-out route as present, and a route
    /// somebody commented out is one they meant to have.
    /// </para>
    /// </remarks>
    private static bool IsCalledIn(string code, string methodName)
    {
        var delegation = new Regex($@"\.\s*{Regex.Escape(methodName)}\s*\(");

        return code
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Any(delegation.IsMatch);
    }

    private sealed record SourceFile(string RelativePath, string Text);

    private static List<SourceFile> BoundaryEndpointFiles()
    {
        var root = RepositoryRoot();
        var absolute = Path.Combine(
            root, BoundaryEndpointsDirectory.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(absolute))
        {
            throw new DirectoryNotFoundException(
                $"found nothing to inspect: '{BoundaryEndpointsDirectory}' does not exist under "
                    + $"'{root}'. A path-based check that resolves no files PASSES");
        }

        var files = Directory
            .EnumerateFiles(absolute, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => new SourceFile(
                Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(path)))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                $"found nothing to inspect: no C# files directly under {BoundaryEndpointsDirectory}");
        }

        return files;
    }

    /// <summary>Every boundary endpoint file's text, as one document. Filtering is the detector's job.</summary>
    private static string BoundaryEndpointCode() =>
        string.Join('\n', BoundaryEndpointFiles().Select(file => file.Text));

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
