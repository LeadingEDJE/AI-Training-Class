using System.Text.RegularExpressions;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Fences how every test under <c>tests/</c> locates the repository root: on <c>leap.slnx</c>, never
/// on a <c>.git</c> directory (issue #507).
/// </summary>
/// <remarks>
/// <para>
/// <c>git worktree add</c> writes <c>.git</c> as a file containing <c>gitdir: …</c>, not a
/// directory. A walk that tests <c>Directory.Exists(… ".git")</c> therefore never recognises the root
/// of a worktree: it climbs past it to the filesystem top and the gate throws. Measured on
/// 2026-09-01 — 7 tests failed inside a throwaway worktree against a commit that was
/// 0 failed in the main checkout.
/// </para>
/// <para>
/// Why a gate rather than three edits. The defect is invisible from the main checkout, so
/// nothing about running the suite normally can catch a fourth file being written the same way — and
/// the next author copies whichever sibling they happen to open. A failure that depends on where
/// the suite was run from is also the shape this repository has already paid for once: it reads
/// as a code defect, gets triaged as flakiness, and trains people to disbelieve gate failures.
/// </para>
/// <para>
/// The anchor is not interchangeable. <c>leap.slnx</c> is a real file at the root of a worktree and of
/// a clone alike, and it is what ten sibling gates already walk to.
/// </para>
/// </remarks>
public class RepositoryRootAnchorTests
{
    /// <summary>
    /// The whole test tree, not just <c>Architecture/</c>. The three offenders lived there, but a
    /// root walk is written wherever a test needs to read a repository file: eleven other directories
    /// under <c>tests/</c> resolve the root today, across the unit, integration and migration
    /// projects. Scoping this to the folder that happened to be broken would leave every one of them
    /// free to reintroduce the defect.
    /// </summary>
    private const string TestTree = "tests";

    /// <summary>
    /// This file, excluded from its own scan: its controls below quote the offending line verbatim.
    /// Asserted to exist before it is used, so renaming the file fails loudly instead of silently
    /// turning the exclusion into a no-op.
    /// </summary>
    private const string ThisFile = "tests/unit/Architecture/RepositoryRootAnchorTests.cs";

    /// <summary>
    /// A root walk anchored on a <c>.git</c> DIRECTORY — the shape that cannot see a worktree root.
    /// </summary>
    /// <remarks>
    /// Written with <c>\s*</c> around the tokens because C# permits whitespace there, and against
    /// <c>Directory\.Exists</c> specifically: <c>File.Exists(… ".git")</c> would be a legitimate test
    /// for the worktree marker file rather than the defect, so it is deliberately not matched. The
    /// pattern does not match its own source, because every metacharacter in it is escaped.
    /// </remarks>
    private static readonly Regex GitDirectoryAnchor = new(
        @"Directory\s*\.\s*Exists\s*\([^)]*""\.git""",
        RegexOptions.Compiled);

    [Fact]
    public void NoTestFile_LocatesTheRepositoryRoot_ByAGitDirectory()
    {
        // Arrange
        var files = GateSourceFiles();

        // Act
        var offenders = files
            .Where(f => GitDirectoryAnchor.IsMatch(f.Contents))
            .Select(f => f.RelativePath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        // Assert
        offenders.ShouldBeEmpty(
            "a test walks up for a '.git' DIRECTORY, which does not exist in a git worktree — the "
                + "walk runs past the root and the test throws, so the test cannot run there at all. "
                + "Anchor on leap.slnx instead, as PlatformPurityTests.RepositoryRoot does. "
                + "Offenders: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheDetector_FiresOnTheOffendingWalk_AndNotOnTheAnchorThatReplacesIt()
    {
        // Arrange — the two spellings, copied from the code this gate governs.
        const string offending =
            "while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, \".git\")))";
        const string corrected =
            "while (directory is not null && !File.Exists(Path.Combine(directory.FullName, \"leap.slnx\")))";

        // Act & Assert
        GitDirectoryAnchor.IsMatch(offending).ShouldBeTrue(
            "the detector must fire on the walk it exists to forbid, or the rule above passes "
                + "for the wrong reason");

        GitDirectoryAnchor.IsMatch(corrected).ShouldBeFalse(
            "the detector must not fire on the leap.slnx anchor — that is the fix, not the defect");
    }

    /// <summary>
    /// Every <c>.cs</c> file under <c>tests/</c> except this one and the build output, keyed by
    /// repository-relative path.
    /// </summary>
    private static List<SourceFile> GateSourceFiles()
    {
        var root = RepositoryRoot();

        File.Exists(Path.Combine(root, ThisFile.Replace('/', Path.DirectorySeparatorChar)))
            .ShouldBeTrue(
                $"'{ThisFile}' is excluded from the scan because it quotes the offending line, but "
                    + "it is not there — a stale exclusion silently stops excluding anything, so "
                    + "this fails loudly instead.");

        var separator = Path.DirectorySeparatorChar;

        var files = Directory
            .EnumerateFiles(Path.Combine(root, TestTree), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .Select(p => new SourceFile(
                Path.GetRelativePath(root, p).Replace(separator, '/'),
                File.ReadAllText(p)))
            .Where(f => !string.Equals(f.RelativePath, ThisFile, StringComparison.Ordinal))
            .ToList();

        // A scan over zero files passes vacuously, which is the failure that hides all the others.
        files.Count.ShouldBeGreaterThan(
            0, $"found nothing to inspect: no C# files under '{TestTree}/'");

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
                "The repository-root anchor check found nothing to inspect: no repository root "
                    + $"containing leap.slnx above '{AppContext.BaseDirectory}'.");
    }

    private sealed record SourceFile(string RelativePath, string Contents);
}
