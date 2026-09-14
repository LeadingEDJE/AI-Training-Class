using System.Text.RegularExpressions;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// Three independent files each carry their own fallback database name. This pins them to the same
/// string, so one of them cannot quietly point a component at a different database than the others.
/// </summary>
/// <remarks>
/// <para>
/// The three, and why they are deliberately independent. The application's composition root
/// reads its connection inputs from <c>IConfiguration</c>; the design-time factory reads raw
/// environment variables because the migration tooling and the shipped EF bundle both run without a
/// host; and the readiness probe builds a raw <c>NpgsqlConnection</c> that is deliberately isolated
/// from the object-relational layer, so a broken request path cannot mask or trigger a readiness
/// failure. Each is independent on purpose.
/// </para>
/// <para>
/// Why this test reads SOURCE and does not introduce a shared constant. A shared constant is
/// the obvious fix and it is the wrong one: it would couple the design-time factory and the readiness
/// probe back into the application's configuration assembly graph and undo the isolation that is the
/// whole reason they are separate. So the invariant is enforced from outside instead — the files stay
/// independent, and drift between them fails a test.
/// </para>
/// <para>
/// Why it matters concretely. During the additive Phase 51 rename, moving one default and not
/// the others would mean an unset <c>POSTGRES_DB</c> silently sends the application to one database,
/// the migration bundle to another, and the readiness probe to a third — each individually plausible,
/// and the composite invisible until something reports healthy against a database nothing is using.
/// </para>
/// <para>
/// Non-vacuity. The repository root is resolved by walking up to the solution file and THROWS
/// if it cannot be found; every file must exist and must yield a match, and a file whose default
/// cannot be extracted fails with a message naming it rather than being skipped. A path-based check
/// that reads nothing passes, which is the shape this repository keeps finding.
/// </para>
/// </remarks>
public class DatabaseNameDefaultsAgreeTests
{
    /// <summary>
    /// Relative path -> the pattern that captures that file's fallback database name. Each pattern is
    /// anchored to the `POSTGRES_DB` lookup immediately preceding it, so it cannot drift onto some
    /// other defaulted string in the same file.
    /// </summary>
    private static readonly (string RelativePath, string Description, string Variable, Regex Pattern)[] Defaults =
    [
        (
            "api/Program.cs",
            "the application's composition root",
            "POSTGRES_DB",
            new Regex(@"Configuration\[""POSTGRES_DB""\]\s*\?\?\s*""(?<name>[^""]+)""", RegexOptions.Compiled)),
        (
            "api/Platform/Data/DesignTimeDbContextFactory.cs",
            "the design-time factory (used by the migration tooling AND the shipped EF bundle)",
            "POSTGRES_DB",
            new Regex(@"GetEnvironmentVariable\(""POSTGRES_DB""\)\s*\?\?\s*""(?<name>[^""]+)""", RegexOptions.Compiled)),
        (
            "api/Platform/HealthChecks/NpgsqlHealthCheck.cs",
            "the readiness probe's raw connection",
            "POSTGRES_DB",
            new Regex(@"config\[""POSTGRES_DB""\]\s*\?\?\s*""(?<name>[^""]+)""", RegexOptions.Compiled)),
    ];

    /// <summary>The same three files, and each one's fallback HOST.</summary>
    private static readonly (string RelativePath, string Description, string Variable, Regex Pattern)[] HostDefaults =
    [
        (
            "api/Program.cs",
            "the application's composition root",
            "POSTGRES_HOST",
            new Regex(@"Configuration\[""POSTGRES_HOST""\]\s*\?\?\s*""(?<name>[^""]+)""", RegexOptions.Compiled)),
        (
            "api/Platform/Data/DesignTimeDbContextFactory.cs",
            "the design-time factory (used by the migration tooling AND the shipped EF bundle)",
            "POSTGRES_HOST",
            new Regex(@"GetEnvironmentVariable\(""POSTGRES_HOST""\)\s*\?\?\s*""(?<name>[^""]+)""", RegexOptions.Compiled)),
        (
            "api/Platform/HealthChecks/NpgsqlHealthCheck.cs",
            "the readiness probe's raw connection",
            "POSTGRES_HOST",
            new Regex(@"config\[""POSTGRES_HOST""\]\s*\?\?\s*""(?<name>[^""]+)""", RegexOptions.Compiled)),
    ];

    [Fact]
    public void AllThreeDatabaseNameDefaults_AreTheSameString()
    {
        // Arrange & Act
        var found = Defaults
            .Select(d => (d.RelativePath, d.Description, Name: ExtractDefault(d)))
            .ToList();

        // Assert
        var distinct = found.Select(f => f.Name).Distinct(StringComparer.Ordinal).ToList();

        distinct.Count.ShouldBe(1,
            "the three database-name defaults must agree, or an unset POSTGRES_DB points components at "
                + "different databases. Found:"
                + Environment.NewLine
                + string.Join(
                    Environment.NewLine,
                    found.Select(f => $"  {f.Name,-20} <- {f.RelativePath} ({f.Description})")));
    }

    /// <summary>
    /// The same three files each carry a fallback HOST, and it drifts for the same reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added 2026-08-25 after <c>make dev-all</c> failed with "Database server localhost:5432 was not
    /// reachable after 60 attempts" against a Docker Postgres that was healthy throughout. On Windows
    /// <c>localhost</c> resolves to <c>::1</c> first, and Docker Desktop's WSL2 backend serves that leg
    /// through <c>wslrelay.exe</c>; with WSL's network stack wedged the relay ACCEPTS the connection
    /// and then aborts it. <c>docker ps</c> reports healthy and a plain TCP connect succeeds — only a
    /// real Postgres handshake shows the abort — so every available signal points at the database.
    /// </para>
    /// <para>
    /// The defaults are now <c>127.0.0.1</c>, which skips name resolution and is equally correct on
    /// Linux and macOS, where the published port binds <c>0.0.0.0</c>. This test exists so the three
    /// cannot drift back apart one file at a time, for the same reason as the database name above: a
    /// partial change sends the application to one address and the readiness probe to another, and the
    /// composite stays invisible until something reports healthy against a connection nothing uses.
    /// </para>
    /// </remarks>
    [Fact]
    public void AllThreeHostDefaults_AreTheSameString()
    {
        // Arrange & Act
        var found = HostDefaults
            .Select(d => (d.RelativePath, d.Description, Name: ExtractDefault(d)))
            .ToList();

        // Assert
        var distinct = found.Select(f => f.Name).Distinct(StringComparer.Ordinal).ToList();

        distinct.Count.ShouldBe(1,
            "the three host defaults must agree, or an unset POSTGRES_HOST points components at "
                + "different addresses. Found:"
                + Environment.NewLine
                + string.Join(
                    Environment.NewLine,
                    found.Select(f => $"  {f.Name,-20} <- {f.RelativePath} ({f.Description})")));
    }

    [Fact]
    public void TheHostDefault_IsNotLocalhost_WhichResolvesToIpv6First()
    {
        // Not merely "the three agree": all three agreeing on `localhost` IS the bug this was filed
        // for. The value itself is the requirement, so it is asserted rather than left to consistency.
        ExtractDefault(HostDefaults[0]).ShouldBe("127.0.0.1",
            "`localhost` resolves to ::1 on Windows, where Docker Desktop's WSL2 relay can accept a "
                + "connection and then abort it — see the remarks on the test above for the mechanism.");
    }

    [Fact]
    public void TheReadinessProbe_StillBuildsARawConnection_AndDoesNotGoThroughTheContext()
    {
        // Arrange — comment lines are stripped before scanning, deliberately: this file's own doc
        // comment NAMES the context in order to forbid consolidating onto it, and a naive scan would
        // fire on the very sentence that documents the rule.
        var path = Absolute("api/Platform/HealthChecks/NpgsqlHealthCheck.cs");
        var code = string.Join(
            Environment.NewLine,
            File.ReadAllLines(path)
                .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        code.ShouldNotBeNullOrWhiteSpace(
            "stripping comments must not empty the file — an empty scan passes for free");

        // Assert — the isolation this file exists for. Consolidating it onto the shared context would
        // make a broken object-relational configuration able to fail the readiness probe, and vice
        // versa, so the two must stay separate.
        code.ShouldContain("NpgsqlConnection",
            customMessage: "the readiness probe must keep using a raw connection");
        code.ShouldNotContain("LeapDbContext",
            customMessage: "the readiness probe must stay isolated from the EF stack — do not "
                + "consolidate it onto LeapDbContext (comments are excluded from this scan, so this "
                + "is real code)");
    }

    private static string ExtractDefault((string RelativePath, string Description, string Variable, Regex Pattern) target)
    {
        var absolute = Absolute(target.RelativePath);

        if (!File.Exists(absolute))
        {
            throw new FileNotFoundException(
                $"The {target.Variable} drift check found nothing to inspect: '{target.RelativePath}' does "
                    + "not exist. A file scan that reads nothing PASSES — treat this as a broken check.",
                absolute);
        }

        var match = target.Pattern.Match(File.ReadAllText(absolute));
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Could not extract the {target.Variable} fallback from '{target.RelativePath}' "
                    + $"({target.Description}). Either the default was removed — in which case say so "
                    + "here deliberately — or the expression changed shape and this pattern must be "
                    + "updated. Do NOT delete the entry: that would silently stop checking that file.");
        }

        return match.Groups["name"].Value;
    }

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
            $"The database-name drift check could not locate '{SolutionFile}' walking up from "
                + $"'{AppContext.BaseDirectory}'.");
    }
}
