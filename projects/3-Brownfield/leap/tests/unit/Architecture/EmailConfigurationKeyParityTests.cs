using System.Text.RegularExpressions;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Pins every <c>Email__*</c> key the deploy sets to a key the application understands, so a value
/// cannot be configured under one name and read under another.
/// </summary>
/// <remarks>
/// This class of bug produces no error anywhere: the Helm value renders, the ConfigMap carries it,
/// the container gets the variable, and the code's fallback quietly wins. Two shipped that way. The
/// assertion runs deploy-sets against code-reads and not the reverse, because keys with code defaults
/// are intentionally unset; a key set but unread is always a typo or dead configuration. A key counts
/// as understood either as a literal <c>config["Email:X"]</c> lookup or as a reflected property of
/// <see cref="EmailEnvironmentOptions"/>. The second axis is production parity: <c>runtime</c> merges
/// key by key, so an unpinned production key inherits the chart's dev-owned value. Every check asserts
/// non-vacuity first — a regex matching nothing passes. <c>docs/ops/email-environment-labelling.md</c>.
/// </remarks>
public class EmailConfigurationKeyParityTests
{
    /// <summary>`config["Email:Key"]` as written in C#.</summary>
    private static readonly Regex CodeReadKey =
        new(@"""Email:(?<key>[A-Za-z0-9]+)""", RegexOptions.Compiled);

    [Fact]
    public void TheSesRegion_IsReadUnderTheNameTheDeploySets()
    {
        // Arrange -- the key that selects the transport, and it is not optional decoration: read
        // under the wrong name, the client falls through to the SDK's default chain and SES routing
        // follows the container's AWS_REGION instead of this value.
        var understood = UnderstoodKeys();

        // Assert
        understood.ShouldContain(
            "SesRegion",
            "the SES transport must read Email:SesRegion -- the name values.yaml, "
                + "values-production.yaml and deploy-aws.yml all set. It both selects the SES sender "
                + "over the in-memory one and supplies the client's region.");
    }

    [Fact]
    public void NoSourceFileStillReadsTheAbandonedAppBaseUrlKey()
    {
        // Arrange -- asserted over source rather than over the understood-key set, because
        // `App:BaseUrl` is a different SECTION and so would not appear there at all. Without this, the
        // fix could add the new read and leave the old one behind, which reads as working.
        var offenders = SourceFiles()
            .Where(file => File.ReadAllText(file).Contains("\"App:BaseUrl\"", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepositoryRoot(), file))
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        offenders.ShouldBeEmpty(
            $"these files still read the App:BaseUrl key, which nothing anywhere sets: "
                + $"{string.Join(", ", offenders)}. The deploy sets Email__AppBaseUrl.");
    }

    /// <summary>
    /// Every key the application can act on: the literal <c>config["Email:X"]</c> lookups plus the
    /// bound properties of the <c>Email</c> options class.
    /// </summary>
    private static HashSet<string> UnderstoodKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles())
        {
            foreach (var match in CodeReadKey.Matches(File.ReadAllText(file)).Cast<Match>())
            {
                keys.Add(match.Groups["key"].Value);
            }
        }

        // Reflected, not text-matched, so a property rename carries this check along with it.
        EmailEnvironmentOptions.SectionName.ShouldBe(
            "Email",
            "this check assumes the options class binds the Email section; if that changed, the "
                + "reflected property names below belong to a different section.");

        foreach (var property in typeof(EmailEnvironmentOptions).GetProperties())
        {
            keys.Add(property.Name);
        }

        return keys;
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Absolute("api"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Absolute(string relativePath)
        => Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    // Walks up from the test assembly until the solution file appears. Throws rather than returning a
    // best guess: a wrong root would make every read fail in a way that looks like a missing file.
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
            $"The email configuration-key parity check could not locate '{SolutionFile}' walking up "
                + $"from '{AppContext.BaseDirectory}'.");
    }
}
