using System.Text.RegularExpressions;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Fences the business date: nothing in Compass reads the machine clock directly (feature 004, T115).
/// </summary>
/// <remarks>
/// <para>
/// The failure this prevents is invisible for nineteen hours a day. Compass evaluates "today" in
/// <c>America/New_York</c> (<c>CompassBusinessDate</c>), because that is the day a human at Leading EDJE
/// means. <c>DateTime.UtcNow</c> agrees with that for most of the day and disagrees between roughly
/// 20:00 Eastern and midnight, when UTC has already rolled over. A date derived from the machine clock
/// is therefore correct in every test run during working hours and wrong every evening — the shape
/// AC-42 names as the one nobody catches by hand.
/// </para>
/// <para>
/// It has already happened once in this module, which is why this is a gate and not a note:
/// <c>CompassDirectorySeeder</c>'s default anchor was <c>DateOnly.FromDateTime(DateTime.UtcNow)</c>, so
/// in that evening window the demo directory was seeded a day ahead of the day the application judged
/// status against — an assignment seeded as "ended yesterday" ended today in the application's
/// terms, and its client read Active when the fixture meant Inactive. Tests never saw it because they
/// all pass an explicit anchor; only the seeded environment the CP1 walkthrough uses was affected.
/// </para>
/// <para>
/// What is allowed. <c>CompassBusinessDate</c> itself must read a clock — it is the one place
/// that converts an instant into the business day, and it takes an injected <see cref="TimeProvider"/>
/// rather than a static, so tests can control it. Everything else asks it.
/// </para>
/// <para>
/// A source scan, for the same reason <c>ClientStatusSingleDerivationTests</c> is one: the rule is about
/// how a value is obtained, which reflection cannot see.
/// </para>
/// </remarks>
public class CompassBusinessDateTests
{
    /// <summary>The one file permitted to turn an instant into a date. Not a list.</summary>
    private const string TheBusinessDate = "api/Modules/Compass/Services/CompassBusinessDate.cs";

    private const string CompassModuleDirectory = "api/Modules/Compass";

    /// <summary>
    /// A date or time taken from the ambient machine clock.
    /// </summary>
    /// <remarks>
    /// <c>TimeProvider.GetUtcNow()</c> is deliberately NOT matched: it is the injected, controllable
    /// form, and it is what the permitted file uses. The defect is the STATIC clock, which no test can
    /// move.
    /// </remarks>
    private static readonly Regex AmbientClock = new(
        @"DateTime\s*\.\s*(?:UtcNow|Now|Today)|DateTimeOffset\s*\.\s*(?:UtcNow|Now)|DateOnly\s*\.\s*FromDateTime\s*\(\s*DateTime\s*\.",
        RegexOptions.Compiled);

    [Fact]
    public void NoCompassProductionFile_ReadsTheMachineClockDirectly()
    {
        // Arrange
        var files = CompassSourceFiles();

        // Act
        var offenders = files
            .Where(file => !IsTheBusinessDate(file.RelativePath))
            .Where(file => AmbientClock.IsMatch(StripComments(file.Contents)))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        // Assert
        offenders.ShouldBeEmpty(
            "these Compass files read the machine clock directly: "
                + string.Join(", ", offenders)
                + ". Compass's day is America/New_York, so a UTC-derived date is wrong every evening "
                + "between about 20:00 Eastern and midnight. Ask ICompassBusinessDate instead — and if "
                + "the caller is a static helper, take the date as a parameter rather than reaching "
                + "for a clock it cannot be given."
        );
    }

    [Fact]
    public void TheBusinessDate_ConvertsThroughTheEasternZone_RatherThanUsingUtcDirectly()
    {
        // Arrange / Act — the permitted file is permitted because of what it does, so assert that
        // rather than trusting the exemption.
        var contents = CompassSourceFiles()
            .Single(file => IsTheBusinessDate(file.RelativePath))
            .Contents;

        // Assert
        contents.ShouldContain(
            "America/New_York",
            Case.Sensitive,
            "the business date must convert into the Eastern zone, which is the whole point of it"
        );
        contents.ShouldContain(
            "timeProvider",
            Case.Sensitive,
            "it must take an INJECTED clock, or no test can move the boundary it exists to define"
        );
    }

    // ------------------------------------------------------------------ Non-vacuity

    [Fact]
    public void NonVacuity_TheScanFindsCompassFilesAndThePatternMatchesWhatItClaims()
    {
        // A path-based scan that resolves no files PASSES every assertion above — the fail-open shape
        // docs/platform/adding-a-module.md records finding eight times across Phases 48-49.
        var files = CompassSourceFiles();

        files.Count.ShouldBeGreaterThan(30, "the Compass source scan found almost nothing");
        files.ShouldContain(
            file => IsTheBusinessDate(file.RelativePath),
            "CompassBusinessDate.cs is missing from the scan, so its exemption is untested"
        );

        // The pattern really matches the forms it claims to, including the one that actually shipped.
        AmbientClock.IsMatch("DateOnly.FromDateTime(DateTime.UtcNow)").ShouldBeTrue();
        AmbientClock.IsMatch("DateTime.Today").ShouldBeTrue();
        AmbientClock.IsMatch("DateTimeOffset.UtcNow").ShouldBeTrue();

        // And does NOT match the injected form, or this gate would forbid the correct answer.
        AmbientClock.IsMatch("timeProvider.GetUtcNow()").ShouldBeFalse();
        AmbientClock.IsMatch("businessDate.Today()").ShouldBeFalse();
    }

    // ------------------------------------------------------------------ Helpers

    private sealed record SourceFile(string RelativePath, string Contents);

    private static bool IsTheBusinessDate(string relativePath) =>
        relativePath.Equals(TheBusinessDate, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every production <c>.cs</c> file in the Compass module, with repo-relative paths.
    /// </summary>
    /// <remarks>
    /// Walks up from the test assembly to find the repository root and FAILS LOUDLY if it cannot, for
    /// the reason the non-vacuity test above states.
    /// </remarks>
    private static List<SourceFile> CompassSourceFiles()
    {
        var root = RepositoryRoot();
        var directory = Path.Combine(root, CompassModuleDirectory);

        Directory.Exists(directory).ShouldBeTrue($"expected the Compass module at '{directory}'");

        return
        [
            .. Directory
                .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Select(path => new SourceFile(
                    Path.GetRelativePath(root, path).Replace('\\', '/'),
                    File.ReadAllText(path)
                )),
        ];
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
                "The Compass business-date check found nothing to inspect: no repository root containing "
                    + $"leap.slnx above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>Removes comments, so prose describing the trap does not read as committing it.</summary>
    private static string StripComments(string contents) =>
        Regex.Replace(
            Regex.Replace(contents, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
            @"//.*?$",
            string.Empty,
            RegexOptions.Multiline
        );
}
