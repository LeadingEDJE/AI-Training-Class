using System.Net;
using System.Text.RegularExpressions;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Pins the entry point to STARTING the application and nothing else: it must apply no schema change
/// when it boots, and it must not describe a mechanism it no longer has.
/// </summary>
/// <remarks>
/// <para>
/// Why this is a test and not a review note. The start-up path used to apply pending migrations
/// from a lifetime callback, and after exhausting its retries it logged an error and carried on serving
/// traffic against a schema that might be out of date — no signal at the deploy boundary, nothing
/// failed. Schema application now happens in a pre-rollout deploy hook where a failure stops the
/// release. Re-adding a convenience call at boot would silently restore the old failure mode, and a
/// reviewer skimming a large diff would not necessarily catch it. This makes it a red suite instead.
/// </para>
/// <para>
/// Why the prose assertion is here too. A comment saying "migrations run at start-up" is how a
/// reader six months from now concludes the behaviour still exists, and it is also what makes the
/// phase's own grep-based success criterion read as a false positive. The file must describe what it
/// does, not what it used to do — the version history holds the rest.
/// </para>
/// <para>
/// What the host assertions do and do NOT prove. The test host registers a non-relational
/// provider, so a provider-guarded schema call would be skipped there regardless. Booting the host
/// therefore proves that start-up completes and serves without schema work blocking or breaking it —
/// a shape check, not a substitute for the source scan above it. Stated rather than glossed: the
/// source scan is the load-bearing assertion.
/// </para>
/// <para>
/// Non-vacuity. A path-based check that reads nothing PASSES, which is the fail-open shape this
/// repository keeps finding. The repository root is resolved by walking up to the solution file and
/// THROWS if it cannot be found, the entry point is asserted non-empty, and
/// <see cref="Scanners_FireOnPlantedViolations_SoAPassIsNotVacuous"/> runs both scanners over samples
/// that must match.
/// </para>
/// </remarks>
public class StartupDoesNotMigrateTests
{
    private const string EntryPointRelativePath = "api/Program.cs";

    /// <summary>
    /// Calls that APPLY schema. Read-only inspection (<c>GetPendingMigrations</c>,
    /// <c>GetAppliedMigrations</c>) is deliberately not listed — reporting drift is fine, applying it
    /// is not.
    /// </summary>
    private static readonly (string Name, Regex Pattern)[] SchemaApplicationCalls =
    [
        ("an EF Core migration application call",
            new Regex(@"\.\s*Migrate(Async)?\s*\(", RegexOptions.Compiled)),
        ("a bare migration application call",
            new Regex(@"(?<![A-Za-z0-9_.])Migrate(Async)?\s*\(", RegexOptions.Compiled)),
        ("a schema-creation call that bypasses the migration history",
            new Regex(@"EnsureCreated(Async)?\s*\(", RegexOptions.Compiled)),
        ("a raw SQL schema-application call",
            new Regex(@"ExecuteSqlRaw\s*\(\s*""\s*(CREATE|ALTER|DROP)\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase)),
    ];

    /// <summary>
    /// Prose that would tell a reader the deleted mechanism is still there. Each pattern is anchored to
    /// a start-up word NEXT TO a migration word on the same sentence, so a legitimate mention of the
    /// deploy-time hook ("the Helm migration hook Job runs as an init container") does not fire.
    /// </summary>
    private static readonly (string Name, Regex Pattern)[] RemovedMechanismProse =
    [
        ("prose placing migrations at start-up",
            new Regex(@"migrat\w*[^.\n]{0,60}\bat\s+start-?up\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("prose placing start-up before migrations",
            new Regex(@"\bstart-?up\b[^.\n]{0,60}migrat",
                RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("prose tying migrations to the application-started callback",
            new Regex(@"ApplicationStarted[^.\n]{0,80}migrat|migrat[^.\n]{0,80}ApplicationStarted",
                RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("prose describing the application running migrations",
            new Regex(@"\bRun(s|ning)?\s+(the\s+|pending\s+|EF\s+Core\s+)*migrations\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("prose describing migration retry attempts",
            new Regex(@"\bmigration attempt", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
    ];

    [Fact]
    public void EntryPoint_AppliesNoSchemaChanges()
    {
        // Arrange
        var lines = EntryPointLines();

        // Act
        var offenders = Scan(lines, SchemaApplicationCalls);

        // Assert
        offenders.ShouldBeEmpty(
            $"{EntryPointRelativePath} must apply no schema change when the application starts. "
                + "Schema is applied by the pre-rollout deploy hook, where a failure stops the release "
                + "instead of being logged into a void — see docs/ops/pre-deploy-migrations.md. "
                + $"Offending line(s):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void EntryPoint_DescribesNoStartUpSchemaWork()
    {
        // Arrange
        var lines = EntryPointLines();

        // Act
        var offenders = Scan(lines, RemovedMechanismProse);

        // Assert
        offenders.ShouldBeEmpty(
            $"{EntryPointRelativePath} must not describe applying schema at start-up. A comment about a "
                + "mechanism the file no longer has reads as documentation of current behaviour. "
                + $"Offending line(s):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public async Task TestHost_Starts_AndServes_WithNoSchemaWork()
    {
        // Arrange
        using var factory = new TestWebApplicationFactory();

        // Act
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        context.Database.IsRelational().ShouldBeFalse(
            "the test host must stay on the non-relational provider; if this ever flips, the boot "
                + "assertions in this class start exercising a real database and must be reconsidered");
    }

    [Fact]
    public async Task TestHost_WithSeedingGateOff_SeedsNothing()
    {
        // Arrange
        using var factory = new SeedingDisabledFactory();

        // Act — boot, then give the fire-and-forget lifetime callback a bounded window to misbehave.
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var seeded = await context.SystemSettings
            .CountAsync(TestContext.Current.CancellationToken);

        seeded.ShouldBe(0,
            "StartupTasks:SeedReferenceData=false must suppress reference-data seeding. Seeding is the "
                + "ONLY thing the surviving lifetime callback does, so a broken gate here means the "
                + "callback is doing something it was not asked to.");
    }

    [Fact]
    public void Scanners_FireOnPlantedViolations_SoAPassIsNotVacuous()
    {
        // Arrange — samples of exactly what the two scanners exist to catch.
        string[] plantedCalls =
        [
            "                    await dbContext.Database.MigrateAsync();",
            "        dbContext.Database.Migrate();",
            "        dbContext.Database.EnsureCreated();",
        ];
        string[] plantedProse =
        [
            "// Migrations now run in the ApplicationStarted callback above,",
            "// Run migrations and seed data after the app starts listening.",
            "// The application applies migrations at startup so probes answer early.",
            "logger.LogWarning(\"Migration attempt {Attempt} failed\", attempt);",
        ];

        // Act
        var callHits = Scan(plantedCalls, SchemaApplicationCalls);
        var proseHits = Scan(plantedProse, RemovedMechanismProse);

        // Assert
        callHits.Count.ShouldBe(plantedCalls.Length,
            "every planted schema-application call must be detected, otherwise a clean scan of the real "
                + "file means nothing");
        proseHits.Count.ShouldBe(plantedProse.Length,
            "every planted piece of removed-mechanism prose must be detected");
    }

    private static List<string> Scan(
        IReadOnlyList<string> lines,
        (string Name, Regex Pattern)[] patterns)
    {
        var offenders = new List<string>();
        for (var index = 0; index < lines.Count; index++)
        {
            foreach (var (name, pattern) in patterns)
            {
                if (pattern.IsMatch(lines[index]))
                {
                    offenders.Add($"line {index + 1}: {name} -> {lines[index].Trim()}");
                    break;
                }
            }
        }

        return offenders;
    }

    private static IReadOnlyList<string> EntryPointLines()
    {
        var absolute = Path.Combine(
            RepositoryRoot(),
            EntryPointRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absolute))
        {
            throw new FileNotFoundException(
                $"The start-up check found nothing to inspect: '{EntryPointRelativePath}' does not exist "
                    + "under the repository root. A file scan that reads nothing PASSES — treat this as "
                    + "a broken check, not a clean result.",
                absolute);
        }

        var lines = File.ReadAllLines(absolute);
        if (lines.Length == 0)
        {
            throw new InvalidOperationException(
                $"The start-up check read '{EntryPointRelativePath}' and found it empty. An empty "
                    + "inspection set passes silently.");
        }

        return lines;
    }

    // Walks up from the test assembly until the solution file appears. Throws rather than returning a
    // best guess: a wrong root would make the scan empty, and an empty scan passes for free.
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
            $"The start-up check could not locate '{SolutionFile}' walking up from "
                + $"'{AppContext.BaseDirectory}'. Without a repository root the file scan is empty and "
                + "the check passes for free.");
    }

    /// <summary>
    /// The standard unit-test host with reference-data seeding switched off, so the surviving lifetime
    /// callback is observed taking its gated no-op path.
    /// </summary>
    private sealed class SeedingDisabledFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("StartupTasks:SeedReferenceData", "false");
        }
    }
}
