using System.Text.RegularExpressions;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Fences BR-16: assignment history is never truncated, so AC-24 and AC-39 return complete history.
/// </summary>
/// <remarks>
/// <para>
/// This gate exists because the thing it protects does not exist yet. ADR-007 decides that audit
/// entries are purged at two years while business records are never purged, and PRD v11 states the reason
/// inside AC-24's own paragraph: business records are kept indefinitely "because AC-24 and AC-39 depend
/// on history older than two years and would silently return incomplete results if business data were
/// aged out." Constitution Principle VIII names AC-24 by number for the same reason and adds the
/// obligation this file discharges: "nothing may be built that assumes business records age out."
/// The retention job itself is unbuilt (Known Gap <c>#145</c>), so there is nothing to test against —
/// only a shape to forbid.
/// </para>
/// <para>
/// What it forbids, and why a source scan. The two history projections must not paginate, cap, or
/// apply a date floor. `Take`/`Skip` have no signature that distinguishes "page a list" from "truncate a
/// history", and a date floor is a comparison written inside a method body — neither is visible to
/// reflection. The integration suite covers the other half:
/// <c>CompassClientAssignmentStatusTests.HistoryOlderThanTheAuditWindow_IsStillReturned</c> seeds a
/// three-year-old row and asserts it comes back. That test proves today's behaviour; this one fails the
/// build on the change that would break it.
/// </para>
/// <para>
/// The truncation would be silent, which is the whole argument for a gate. A `Take(50)` on a
/// history read looks like ordinary hygiene, breaks no test that does not seed 51 rows, and produces a
/// screen that is subtly, permanently wrong about the past. Nobody notices a row that is missing from a
/// list they have never counted.
/// </para>
/// <para>
/// Non-vacuity is asserted, not assumed. A path-based check that resolves no files PASSES — the
/// fail-open shape <c>docs/platform/adding-a-module.md</c> records finding eight times across Phases
/// 48-49, and the constitution's Governance section makes it a rule. So the scan proves it found the
/// repository, the file, and both method bodies before it forbids anything, and the patterns are asserted
/// against known-bad and known-good text.
/// </para>
/// </remarks>
public class CompassHistoryNoTruncationTests
{
    /// <summary>The file holding both history projections.</summary>
    private const string ReadRepository =
        "api/Modules/Compass/Data/Repositories/CompassReadRepository.cs";

    /// <summary>
    /// The two methods that project an assignment history — AC-24's and AC-20's.
    /// </summary>
    /// <remarks>
    /// Both are fenced, not just the client one. AC-39's duration report reads over the same history and
    /// PRD v11's retention paragraph names AC-24 and AC-39 together; fencing one and leaving its twin
    /// open would be an arbitrary half-measure, and <c>#223</c> shipped the EDJEr side days before this.
    /// </remarks>
    private static readonly string[] HistoryMethods =
        ["GetClientViewAsync", "GetEmployeeDetailAsync"];

    /// <summary>A row cap — `Take(...)`, `Skip(...)`, or their async cousins.</summary>
    /// <remarks>
    /// <c>TakeWhile</c> is deliberately caught too: a predicate-bounded read is still a bounded read.
    /// <c>SingleAsync</c>/<c>FirstOrDefaultAsync</c> are NOT caught — they select one CLIENT or one
    /// EDJEr, which is the record being viewed, not a limit on its history.
    /// </remarks>
    private static readonly Regex RowCap = new(
        @"\.\s*(?:Take|TakeWhile|TakeLast|Skip|SkipWhile|SkipLast)\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// A date floor applied to a history read — a retention window wearing a filter's clothes.
    /// </summary>
    /// <remarks>
    /// Matches a comparison against something whose name says "cutoff", "retention", "since", "oldest",
    /// "earliest" or "window". Deliberately NAME-based rather than shape-based: the legitimate date
    /// comparison in this file is the currency predicate, which is composed from
    /// <c>IClientStatusDerivation</c> and never spelled out here at all
    /// (<c>ClientStatusSingleDerivationTests</c> enforces that separately), so anything else comparing a
    /// date in a history projection is the thing being forbidden. A name-based pattern cannot catch a
    /// floor called <c>x</c> — recorded as a limit rather than papered over, because the honest scope of
    /// a source scan is what it can see.
    /// </remarks>
    private static readonly Regex RetentionWindow = new(
        @"(?:cutoff|retention|since|oldest|earliest|window)\w*\s*(?:<=|>=|<|>)"
            + @"|(?:<=|>=|<|>)\s*\w*(?:cutoff|retention|since|oldest|earliest|window)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void NeitherHistoryProjection_CapsTheNumberOfRows()
    {
        // Arrange
        var bodies = HistoryMethodBodies();

        // Act
        var offenders = bodies
            .Where(b => RowCap.IsMatch(StripComments(b.Body)))
            .Select(b => b.Method)
            .ToList();

        // Assert
        offenders.ShouldBeEmpty(
            "these assignment-history projections cap their rows: "
                + string.Join(", ", offenders)
                + ". BR-16 keeps business records forever and AC-24/AC-39 must return complete history — "
                + "a paginated history is a truncated one, and the rows it drops are the OLDEST, which is "
                + "exactly what those criteria depend on. If a genuine paging need appears, it belongs on "
                + "a different read, not on this one.");
    }

    [Fact]
    public void NeitherHistoryProjection_AppliesARetentionWindow()
    {
        // Arrange
        var bodies = HistoryMethodBodies();

        // Act
        var offenders = bodies
            .Where(b => RetentionWindow.IsMatch(StripComments(b.Body)))
            .Select(b => b.Method)
            .ToList();

        // Assert
        offenders.ShouldBeEmpty(
            "these assignment-history projections filter by a date floor: "
                + string.Join(", ", offenders)
                + ". Principle VIII: nothing may be built that assumes business records age out. The "
                + "retention job (#145) purges AUDIT entries only.");
    }

    // ------------------------------------------------------------------ Non-vacuity

    [Fact]
    public void NonVacuity_TheScanFindsTheFileAndBothMethodBodies()
    {
        // Both assertions above pass trivially on an empty list, so this is what stops the gate from
        // silently fencing nothing after a rename or a move.
        var bodies = HistoryMethodBodies();

        bodies.Count.ShouldBe(
            HistoryMethods.Length,
            "the no-truncation check did not find every history projection it fences. Either a method "
                + $"was renamed or moved out of {ReadRepository} — in which case HistoryMethods is stale "
                + "and this gate now protects less than it claims — or the extraction is broken.");

        foreach (var body in bodies)
        {
            body.Body.Length.ShouldBeGreaterThan(
                200,
                $"{body.Method}'s extracted body is implausibly short, so brace matching is probably "
                    + "wrong and the scan is inspecting almost nothing.");
        }

        // A body that does not mention the history it projects is not the body we think it is.
        bodies.ShouldAllBe(b => b.Body.Contains("AssignmentHistory", StringComparison.Ordinal));
    }

    [Fact]
    public void NonVacuity_ThePatternsMatchWhatTheyClaimTo()
    {
        // The regexes ARE the gate. If they stop matching, both rules pass on any input and the failure
        // is completely silent.
        RowCap.IsMatch(".Take(50)").ShouldBeTrue();
        RowCap.IsMatch(".Skip(10).Take(10)").ShouldBeTrue();
        RowCap.IsMatch(".TakeWhile(a => a.StartDate > cutoff)").ShouldBeTrue(
            "a predicate-bounded read is still a bounded read");

        // One row of the RECORD being viewed is not a cap on its history.
        RowCap.IsMatch(".FirstOrDefaultAsync(cancellationToken)").ShouldBeFalse();
        RowCap.IsMatch(".SingleAsync(cancellationToken)").ShouldBeFalse();
        RowCap.IsMatch("var taken = 5;").ShouldBeFalse("a local named `taken` is not a Take call");

        RetentionWindow.IsMatch("a.StartDate >= retentionCutoff").ShouldBeTrue();
        RetentionWindow.IsMatch("cutoff <= a.EndDate").ShouldBeTrue();
        RetentionWindow.IsMatch("a.StartDate > oldestVisible").ShouldBeTrue();
        RetentionWindow.IsMatch("a.EndDate >= today").ShouldBeFalse(
            "the currency predicate is legitimate, and is fenced separately by "
                + "ClientStatusSingleDerivationTests");
        RetentionWindow.IsMatch("Where(a => a.ClientId == clientId)").ShouldBeFalse();
    }

    [Fact]
    public void NonVacuity_AMissingMethod_ThrowsRatherThanInspectingNothing()
    {
        var thrown = Should.Throw<InvalidOperationException>(
            () => ExtractBody(SourceText(), "ThisMethodDoesNotExistAsync"));

        thrown.Message.ShouldContain(
            "found nothing to inspect",
            Case.Insensitive,
            "the failure must say the check found nothing, so the fail-open shape is unmistakable");
    }

    // ------------------------------------------------------------------ Scanning

    private static List<MethodBody> HistoryMethodBodies()
    {
        var source = SourceText();
        return HistoryMethods.Select(m => new MethodBody(m, ExtractBody(source, m))).ToList();
    }

    /// <summary>
    /// Returns the text between a method's opening brace and its matching close.
    /// </summary>
    /// <remarks>
    /// Brace-counted rather than regex-matched: a method body contains braces (object initialisers,
    /// lambdas, collection expressions), and a regex cannot pair them. Scoping to the body matters —
    /// scanning the whole file would flag any unrelated `Take` elsewhere in it, and this file legitimately
    /// contains paging-free but unrelated queries.
    /// </remarks>
    private static string ExtractBody(string source, string methodName)
    {
        var signature = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        if (signature < 0)
        {
            throw new InvalidOperationException(
                $"The no-truncation check found nothing to inspect: '{methodName}' is not in "
                    + $"{ReadRepository}. A check that resolves no code PASSES, which is the fail-open "
                    + "shape it exists to avoid — fix the name rather than letting it run empty.");
        }

        var open = source.IndexOf('{', signature);
        if (open < 0)
        {
            throw new InvalidOperationException(
                $"The no-truncation check found nothing to inspect: no body follows '{methodName}'.");
        }

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[(open + 1)..i];
                }
            }
        }

        throw new InvalidOperationException(
            $"The no-truncation check found nothing to inspect: '{methodName}'s braces are unbalanced.");
    }

    private static string SourceText()
    {
        var root = RepositoryRoot();
        var absolute = Path.Combine(root, ReadRepository.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absolute))
        {
            throw new FileNotFoundException(
                $"The no-truncation check found nothing to inspect: '{ReadRepository}' does not exist "
                    + $"under repository root '{root}'.");
        }

        return File.ReadAllText(absolute);
    }

    /// <summary>Removes comments before matching, so prose describing the rule is not a violation.</summary>
    /// <remarks>
    /// Load-bearing here: this feature's own implementation comment explains why there is no cap, and the
    /// word "window" appears in the surrounding discussion. A gate that trips on the comment justifying
    /// it gets suppressed within a day.
    /// </remarks>
    private static string StripComments(string source)
    {
        var withoutBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//.*?$", string.Empty, RegexOptions.Multiline);
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
                "The no-truncation check found nothing to inspect: no repository root containing "
                    + $"leap.slnx above '{AppContext.BaseDirectory}'.");
    }

    private sealed record MethodBody(string Method, string Body);
}
