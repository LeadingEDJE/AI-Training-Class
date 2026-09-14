using System.Text.RegularExpressions;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Issue #234 on the SERVER side: every date a user reads is <c>MM/dd/yyyy</c> (issue #306).
/// </summary>
/// <remarks>
/// <para>
/// The frontend has <c>lib/date.ts</c> and a gate; the backend had neither. Compass composes
/// user-facing strings server-side too — an overlap rejection a form renders verbatim, an email body a
/// coach reads — and one of them shipped rendering <c>2024-04-01</c>, the exact format #234 was filed
/// to remove. Review did not catch it because nothing looked.
/// </para>
/// <para>
/// What this gate does NOT forbid. <c>ToString("O")</c> appears 17 times across the Compass
/// services and every one is an <c>AuditEntry</c> <c>FieldChange</c> value — a round-trip
/// serialisation nobody reads as prose, where a stable machine-comparable form is the correct choice.
/// The rule is about DISPLAY, so it targets the one format that is only ever wrong for a human:
/// <c>yyyy-MM-dd</c>.
/// </para>
/// </remarks>
public class CompassDisplayDateTests
{
    private const string CompassModuleDirectory = "api/Modules/Compass";

    /// <summary>
    /// A date formatted <c>yyyy-MM-dd</c> — as a composite-format interpolation (<c>{x:yyyy-MM-dd}</c>)
    /// or a <c>ToString</c> argument. Both shipped forms of the same mistake.
    /// </summary>
    private static readonly Regex IsoDisplayFormat = new(
        @"(:yyyy-MM-dd|ToString\(\s*""yyyy-MM-dd)",
        RegexOptions.Compiled
    );

    // ---------------------------------------------------------------- the helper's behaviour

    [Fact]
    public void Format_RendersADateAsMonthDayYear()
    {
        // Arrange / Act / Assert — the one shape #234 asks for.
        CompassDisplayDate.Format(new DateOnly(2024, 4, 1)).ShouldBe("04/01/2024");
        CompassDisplayDate.Format(new DateOnly(2025, 12, 31)).ShouldBe("12/31/2025");
    }

    [Fact]
    public void Format_PadsSingleDigitMonthsAndDays()
    {
        // A server rendering "4/1/2024" while the SPA renders "04/01/2024" is the same date shown two
        // ways, which is the inconsistency #234 exists to end.
        CompassDisplayDate.Format(new DateOnly(2024, 1, 5)).ShouldBe("01/05/2024");
    }

    [Fact]
    public void Format_IsInvariantOfTheServerCulture()
    {
        // Arrange — a culture whose short-date pattern is dd/MM/yyyy. Without an explicit invariant
        // culture the SAME call renders 01/04/2024 here, and "coming to an end on 01/04/2024" is
        // ambiguous in a way nobody notices until someone acts on the wrong month.
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-GB");

        try
        {
            // Act / Assert
            CompassDisplayDate.Format(new DateOnly(2024, 4, 1)).ShouldBe("04/01/2024");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Format_RendersAnAbsentDateAsEmpty()
    {
        // Absent is a real answer — an assignment with no end date. Empty rather than a placeholder:
        // choosing "—" or "N/A" belongs to whoever composes the sentence, and a caller that receives
        // one it did not ask for has to strip it back out. Mirrors the SPA's formatOptionalDate.
        CompassDisplayDate.Format(null).ShouldBe(string.Empty);
    }

    // ---------------------------------------------------------------- the gate

    [Fact]
    public void NoCompassProductionFile_FormatsADateAsIsoForDisplay()
    {
        // Arrange
        var files = CompassSourceFiles();

        // Act
        var offenders = files
            .Where(file => IsoDisplayFormat.IsMatch(StripComments(file.Contents)))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        // Assert
        offenders.ShouldBeEmpty(
            "these Compass files render a date as yyyy-MM-dd, which issue #234 exists to remove: "
                + string.Join(", ", offenders)
                + ". Use CompassDisplayDate.Format instead. ToString(\"O\") for an AuditEntry "
                + "FieldChange is NOT display and is deliberately not matched by this rule."
        );
    }

    // ---------------------------------------------------------------- non-vacuity

    [Fact]
    public void NonVacuity_TheScanFindsCompassFilesAndThePatternMatchesWhatItClaims()
    {
        // A path-based scan that resolves no files PASSES the assertion above — the fail-open shape
        // docs/platform/adding-a-module.md records finding eight times across Phases 48-49.
        var files = CompassSourceFiles();

        files.Count.ShouldBeGreaterThan(30, "the Compass source scan found almost nothing");

        // The pattern matches both forms of the mistake, including the one that actually shipped.
        IsoDisplayFormat.IsMatch("$\"({conflict.SowStartDate:yyyy-MM-dd})\"").ShouldBeTrue();
        IsoDisplayFormat.IsMatch("date.ToString(\"yyyy-MM-dd\")").ShouldBeTrue();

        // And does NOT match the audit round-trip, or this gate would forbid the correct answer for
        // the 17 FieldChange values that legitimately use it.
        IsoDisplayFormat.IsMatch("assignment.StartDate.ToString(\"O\")").ShouldBeFalse();
        IsoDisplayFormat.IsMatch("CompassDisplayDate.Format(sow.SowEndDate)").ShouldBeFalse();
    }

    // ---------------------------------------------------------------- helpers

    private sealed record SourceFile(string RelativePath, string Contents);

    /// <summary>Every production <c>.cs</c> file in the Compass module, with repo-relative paths.</summary>
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

    /// <summary>
    /// Blanks comments so a doc-comment NAMING the forbidden format is not itself an offender.
    /// </summary>
    private static string StripComments(string contents) =>
        Regex.Replace(contents, @"//.*?$|/\*.*?\*/", string.Empty, RegexOptions.Multiline | RegexOptions.Singleline);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "leap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "The Compass display-date check found nothing to inspect: no repository root containing "
                    + $"leap.slnx above '{AppContext.BaseDirectory}'.");
    }
}
