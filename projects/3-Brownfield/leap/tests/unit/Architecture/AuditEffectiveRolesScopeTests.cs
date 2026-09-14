using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Enforces FR-043: effective roles are captured on Compass write paths only. Timesheet and
/// OOTO writes must continue to leave <c>AuditLog.EffectiveRoles</c> null.
/// </summary>
/// <remarks>
/// <para>
/// Why a source scan rather than a behavioural test. The scope rule is about which call sites
/// supply the argument, and the argument is optional — so a Timesheet service that started passing it
/// would break nothing and fail nothing. Every existing unit test would still pass while the column
/// quietly began meaning something different for Timesheet rows, and any consumer told "null means a
/// Timesheet write" would start misreading history. There is no runtime moment at which that mistake
/// is observable, which is exactly why it needs a gate.
/// </para>
/// <para>
/// Known limit: this matches the named-argument form, which is what all 30 existing
/// <c>AuditEntry</c> call sites use. An eighth positional argument would evade it. That is an
/// acceptable gap for a convention check — the point is to make the mistake loud for anyone following
/// the local style, not to be a proof.
/// </para>
/// <para>
/// The scan fails loudly when it resolves no files. A path-based check that matches nothing does not
/// fail — it passes, and eight gates in this repository were found silently fail-open that way.
/// </para>
/// <para>
/// ⏳ THIS GATE HAS A KNOWN EXPIRY — read this before "fixing" a failure it reports. Timesheet
/// and OOTO are to be rewritten onto the same authentication and authorization model Compass uses,
/// with distinct roles per application (stated 2026-08-10). When that lands, those modules SHOULD
/// capture effective roles, and this check will fail on their new call sites — correctly, and by
/// design. That failure is the signal to retire or widen it, **not** to remove the argument from the
/// call site that tripped it. Retire it in the same change that moves a module onto the new model, and
/// update the three-value contract on <see cref="AuditLog.EffectiveRoles"/> at the same time: once a
/// module captures, <c>null</c> stops meaning "a write from that module" and narrows to "a row
/// predating capture for it". Until then the scope rule stands and this gate enforces it.
/// </para>
/// </remarks>
public class AuditEffectiveRolesScopeTests
{
    // The named-argument form, as written at a call site constructing an AuditEntry.
    private const string CaptureToken = "EffectiveRoles:";

    // The one module FR-043 permits to capture. Compare with the directory separator normalised.
    private const string CompassModule = "api/Modules/Compass/";

    // AuditService reads `entry.EffectiveRoles` to persist it and AuditLog declares the property —
    // neither is a call site electing to capture, so both are expected and must not be flagged.
    private static readonly string[] PlumbingFiles =
    [
        "api/Platform/Services/AuditService.cs",
        "api/Platform/Domain/AuditLog.cs",
        "api/Platform/Domain/AuditEntry.cs",
        "api/Platform/Dtos/AuditLogResponse.cs",
    ];

    [Fact]
    public void NoWritePathOutsideCompass_CapturesEffectiveRoles()
    {
        // Arrange
        var files = ProductionSourceFiles("api");

        // Act
        var offenders = files
            .Where(f => !PlumbingFiles.Contains(f.RelativePath, StringComparer.Ordinal))
            .Where(f => !f.RelativePath.StartsWith(CompassModule, StringComparison.Ordinal))
            .Where(f => f.Text.Contains(CaptureToken, StringComparison.Ordinal))
            .Select(f => f.RelativePath)
            .ToList();

        // Assert
        offenders.ShouldBeEmpty(
            "FR-043 scopes effective-roles capture to Compass. A Timesheet or OOTO write that supplies "
                + "them makes null stop meaning \"a non-Compass write\", which is what every consumer "
                + "of the column was told it means. If capture is genuinely wanted elsewhere, change "
                + "FR-043 and the remarks on AuditLog.EffectiveRoles first — do not widen it here.");
    }

    [Fact]
    public void TheScanSeesTheCaptureToken_SoItIsNotPassingOnAnEmptySet()
    {
        // The positive control. Without it, a renamed property or a broken path would make the check
        // above pass for free — it would find no offenders because it finds nothing at all.
        var files = ProductionSourceFiles("api");

        files.Count.ShouldBeGreaterThan(100);
        files.ShouldContain(
            f => f.RelativePath == "api/Platform/Domain/AuditEntry.cs",
            "the scan must reach the file declaring the parameter this check is about");
        files.Count(f => f.Text.Contains("EffectiveRoles", StringComparison.Ordinal))
            .ShouldBeGreaterThan(0, "the token this check searches for must exist somewhere in api/");
    }

    private sealed record SourceFile(string RelativePath, string Text);

    private static List<SourceFile> ProductionSourceFiles(string relativeDirectory)
    {
        var root = RepositoryRoot();
        var absolute = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(absolute))
        {
            throw new DirectoryNotFoundException(
                $"The effective-roles scope check found nothing to inspect: '{relativeDirectory}' does "
                    + $"not exist under repository root '{root}'. A path-based check that resolves no "
                    + "files PASSES — fix the path rather than letting the check run empty.");
        }

        var files = Directory
            .EnumerateFiles(absolute, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .Select(p => new SourceFile(
                Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(p)))
            .ToList();

        if (files.Count == 0)
        {
            throw new DirectoryNotFoundException(
                $"The effective-roles scope check found nothing to inspect: '{relativeDirectory}' "
                    + "contains no C# source files. An empty inspection set passes silently — treat it "
                    + "as a broken check, not a clean result.");
        }

        return files;
    }

    // Walks up from the test assembly until the solution file appears. Throws rather than returning a
    // best guess: a wrong root would make the scan empty, and an empty scan passes.
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
            $"The effective-roles scope check could not locate '{SolutionFile}' walking up from "
                + $"'{AppContext.BaseDirectory}'. Without a repository root the scan is empty and the "
                + "check passes for free.");
    }
}
