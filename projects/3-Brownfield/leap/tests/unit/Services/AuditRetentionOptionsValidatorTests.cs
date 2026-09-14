using LeadingEDJE.Leap.Api.Platform.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// ADR-007's retention window is rejected at startup when it could not produce a sane cutoff (#317).
/// </summary>
/// <remarks>
/// These exist because the previous version of this feature claimed startup rejection in a doc
/// comment while only checking at runtime — caught in review on PR #318. The claim is now true, and
/// these are what keep it true.
/// </remarks>
public class AuditRetentionOptionsValidatorTests
{
    [Fact]
    public void AcceptsTheAdrDefault()
    {
        // The value every environment actually runs, since nothing sets the section.
        var act = () => AuditRetentionOptionsValidator.Validate(new AuditRetentionOptions());

        act.ShouldNotThrow();
    }

    [Fact]
    public void AcceptsAOneYearWindow_TheSmallestUsableOne()
    {
        var act = () =>
            AuditRetentionOptionsValidator.Validate(new AuditRetentionOptions { AuditEntryYears = 1 });

        act.ShouldNotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void RejectsAWindowThatWouldPurgeEverything(int years)
    {
        // A cutoff at or after "now" is a request to empty the audit trail. Refused rather than
        // honoured, and refused rather than silently corrected to the default — coercing would let the
        // deploy succeed while ignoring what the operator asked for.
        var act = () =>
            AuditRetentionOptionsValidator.Validate(
                new AuditRetentionOptions { AuditEntryYears = years });

        var error = act.ShouldThrow<InvalidOperationException>();
        error.Message.ShouldContain("Retention:AuditEntryYears");
        error.Message.ShouldContain(years.ToString());
    }

    [Fact]
    public void RejectsNull_RatherThanTreatingItAsTheDefault()
    {
        var act = () => AuditRetentionOptionsValidator.Validate(null!);

        act.ShouldThrow<ArgumentNullException>();
    }

    /// <summary>
    /// The validator is actually WIRED at start-up, not merely available to be called.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The doc on <see cref="AuditRetentionOptions.AuditEntryYears"/> promises rejection "at start-up".
    /// The previous version of that sentence was false — there was no validator at all, only a runtime
    /// no-op — and it was caught in review rather than by a test. Asserting the function throws proves
    /// the function; only this proves the promise.
    /// </para>
    /// <para>
    /// A source scan, deliberately, rather than booting a host with a bad value: the wiring is a single
    /// line in <c>Program.cs</c>, and what can silently regress is that line being deleted. The
    /// non-vacuity guard below matters for the reason this repository has learned repeatedly — a scan
    /// that reads nothing PASSES.
    /// </para>
    /// </remarks>
    [Fact]
    public void IsInvokedFromProgramCs()
    {
        var entryPoint = Path.Combine(RepositoryRoot(), "api", "Program.cs");
        File.Exists(entryPoint).ShouldBeTrue($"nothing to scan at {entryPoint}");

        var source = File.ReadAllText(entryPoint);
        source.Length.ShouldBeGreaterThan(1000, "a scan that reads nothing passes for free");

        source.ShouldContain(
            $"{nameof(AuditRetentionOptionsValidator)}.{nameof(AuditRetentionOptionsValidator.Validate)}",
            Case.Sensitive,
            "AuditRetentionOptions' documentation promises start-up rejection; deleting this call makes "
                + "that promise false while every other test still passes");
    }

    /// <summary>Walks up to the repository root, mirroring <c>StartupDoesNotMigrateTests</c>.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "leap.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("could not locate leap.slnx above the test binaries");
    }
}
