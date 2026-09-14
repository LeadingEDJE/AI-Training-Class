using System.Net;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using CompassEmployee = LeadingEDJE.Leap.Api.Modules.Compass.Employee;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Pins the gate on the Compass development directory: it seeds outside the Development environment
/// when — and only when — <c>StartupTasks:SeedCompassDirectory</c> is explicitly true.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists. The synthetic Compass directory used to be reachable only through
/// <c>IsDevelopment()</c>, so the deployed dev environment (which runs <c>Staging</c>) had seven empty
/// <c>compass.*</c> tables and no way to fill them. The flag is the sanctioned way in, and it is
/// deliberately default-OFF — the opposite default to <c>StartupTasks:SeedReferenceData</c>.
/// Putting invented EDJErs and clients into a database is a data decision, so an absent or
/// unparseable value must never be read as consent.
/// </para>
/// <para>
/// Why a host test and not a unit test on the gate expression. The shape of the seed graph is
/// already covered by <c>tests/unit/Data/CompassDirectorySeederTests.cs</c> and its integration twin.
/// What is untested — and what actually broke — is the *reachability* of that seeder from a booted
/// application in a non-Development environment. A test that re-derives the boolean would pass while
/// the call sat behind the wrong branch.
/// </para>
/// <para>
/// Non-vacuity. Every case asserts the host environment is genuinely NOT Development first. A
/// gate test that accidentally ran under Development would pass for the wrong reason and report that a
/// flag works when the branch it guards was never consulted.
/// </para>
/// </remarks>
public class CompassDirectorySeedGateTests
{
    /// <summary>
    /// 19 hand-authored anchors + 78 generated + 2 feature-007 fixtures + 8 feature-017 OOTO persona
    /// counterparts + 1 feature-019 DevBypass caller counterpart = 108.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exact rather than "greater than zero", deliberately — a half-applied seed must read as a
    /// failure, and it would not against a floor. That is why this constant needs maintaining when the
    /// roster grows, and the maintenance is the price of the assertion rather than a defect in it.
    /// </para>
    /// <para>
    /// It is the only ABSOLUTE roster count in the test suite. Its neighbours are all relative:
    /// <c>CompassSeedDeterminismTests.Seeder_AssignsContiguousIdentifiersFromOne</c> asserts
    /// contiguity from 1 for whatever the count is, and the seeder's own tests assert per-row
    /// invariants. So when this fails after an append, the question to ask is whether the roster grew
    /// on purpose — not whether the assertion should become a floor.
    /// </para>
    /// <para>
    /// Feature 018's eight are the OOTO development and end-to-end personas
    /// (<c>alex.coach@example.test</c> and kin), which exist so OOTO's email-correlated timezone read
    /// resolves outside a hand-built fixture. See <c>CompassDirectorySeeder.OotoPersonaCounterparts</c>.
    /// </para>
    /// </remarks>
    private const int ExpectedSeededEmployees = 108;

    [Fact]
    public async Task TestHost_WithFlagOn_SeedsCompassDirectory_OutsideDevelopment()
    {
        // Arrange
        using var factory = new CompassSeedEnabledFactory();

        // Act
        var response = await factory.CreateClient()
            .GetAsync("/health/live", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertNotDevelopment(factory);

        var seeded = await WaitForCompassEmployeesAsync(factory, ExpectedSeededEmployees);

        seeded.ShouldBe(ExpectedSeededEmployees,
            "StartupTasks:SeedCompassDirectory=true must seed the Compass directory even though the "
                + "environment is not Development. This is the whole point of the flag: the deployed dev "
                + "environment runs Staging, so an IsDevelopment()-only path leaves compass.* empty with "
                + "no way in.");
    }

    [Fact]
    public async Task TestHost_WithoutFlag_SeedsNoCompassDirectory()
    {
        // Arrange — a host that sets no flag at all.
        using var factory = new CompassSeedDefaultFactory();

        // Act
        var response = await factory.CreateClient()
            .GetAsync("/health/live", TestContext.Current.CancellationToken);
        await SettleAsync();

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertNotDevelopment(factory);

        var seeded = await CountCompassEmployeesAsync(factory);

        seeded.ShouldBe(0,
            "an absent StartupTasks:SeedCompassDirectory must seed nothing. Default-ON would put "
                + "synthetic people and clients into every deployed environment that never asked for "
                + "them, including production.");
    }

    [Fact]
    public async Task TestHost_WithReferenceSeedingOff_SeedsNoCompassDirectory_EvenWithFlagOn()
    {
        // Arrange
        using var factory = new CompassSeedEnabledButReferenceSeedingOffFactory();

        // Act
        var response = await factory.CreateClient()
            .GetAsync("/health/live", TestContext.Current.CancellationToken);
        await SettleAsync();

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertNotDevelopment(factory);

        var seeded = await CountCompassEmployeesAsync(factory);

        seeded.ShouldBe(0,
            "the Compass flag must sit INSIDE the StartupTasks:SeedReferenceData gate, so that gate "
                + "remains a single kill switch for everything the start-up callback writes. A Compass "
                + "seed that outlives it turns one switch into two.");
    }

    // Polls rather than sleeping a flat interval: the seed runs fire-and-forget from the
    // ApplicationStarted callback, so the only options are a deadline or an arbitrary sleep long enough
    // to be slow on every run. Returns the last observed count so a timeout fails on the assertion's
    // message rather than on an opaque timeout.
    private static async Task<int> WaitForCompassEmployeesAsync(
        TestWebApplicationFactory factory,
        int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var seeded = 0;

        while (DateTime.UtcNow < deadline)
        {
            seeded = await CountCompassEmployeesAsync(factory);
            if (seeded >= expected)
            {
                return seeded;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }

        return seeded;
    }

    // A bounded window for the fire-and-forget callback to do the wrong thing. Matches the two seconds
    // StartupDoesNotMigrateTests allows for the same reason: proving a negative about an async callback
    // means giving it time to misbehave first.
    private static Task SettleAsync() =>
        Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

    private static async Task<int> CountCompassEmployeesAsync(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        return await context.Set<CompassEmployee>()
            .CountAsync(TestContext.Current.CancellationToken);
    }

    private static void AssertNotDevelopment(TestWebApplicationFactory factory)
    {
        var environment = factory.Services.GetRequiredService<IHostEnvironment>();
        environment.IsDevelopment().ShouldBeFalse(
            "these cases exist to exercise the NON-Development path. Under Development the seeder is "
                + "reached regardless of the flag, and every assertion here would pass without proving "
                + "the gate works.");
    }

    /// <summary>
    /// A host whose InMemory database name is FIXED for the lifetime of the factory, so the seeder's
    /// scope and the assertion's scope share one store.
    /// </summary>
    /// <remarks>
    /// <see cref="TestWebApplicationFactory"/> builds its name as <c>$"TestDb_{Guid.NewGuid():N}"</c>
    /// INSIDE the options lambda. <c>AddDbContext</c> registers <c>DbContextOptions</c> scoped by
    /// default, so that lambda runs per scope and every scope gets a brand-new empty database. Reading
    /// back what start-up wrote is then impossible: the seeder reports success and the assertion sees
    /// zero rows — which is exactly how this test first failed after the gate was already working.
    /// Overriding the registration with a name captured once per factory keeps the isolation between
    /// factories (each still gets its own store) while making a single host's writes observable.
    /// </remarks>
    private abstract class SharedStoreFactory : TestWebApplicationFactory
    {
        private readonly string databaseName = $"CompassSeedGate_{Guid.NewGuid():N}";

        protected abstract void ConfigureSeedSettings(IWebHostBuilder builder);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            ConfigureSeedSettings(builder);

            // Registered AFTER base, so these removals see the base class's registrations.
            builder.ConfigureServices(services =>
            {
                foreach (var descriptor in services
                    .Where(d => d.ServiceType == typeof(DbContextOptions<LeapDbContext>)
                        || d.ServiceType == typeof(DbContextOptions)
                        || d.ServiceType == typeof(LeapDbContext))
                    .ToList())
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<LeapDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName)
                           .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }
    }

    /// <summary>The standard unit-test host with the Compass seed flag explicitly on.</summary>
    private sealed class CompassSeedEnabledFactory : SharedStoreFactory
    {
        protected override void ConfigureSeedSettings(IWebHostBuilder builder) =>
            builder.UseSetting("StartupTasks:SeedCompassDirectory", "true");
    }

    /// <summary>A host that sets no seed flag, pinning the default-OFF behaviour.</summary>
    private sealed class CompassSeedDefaultFactory : SharedStoreFactory
    {
        protected override void ConfigureSeedSettings(IWebHostBuilder builder)
        {
            // Deliberately empty: the absence of the setting IS the case under test.
        }
    }

    /// <summary>Compass seeding requested, but the outer reference-data gate closed.</summary>
    private sealed class CompassSeedEnabledButReferenceSeedingOffFactory : SharedStoreFactory
    {
        protected override void ConfigureSeedSettings(IWebHostBuilder builder)
        {
            builder.UseSetting("StartupTasks:SeedCompassDirectory", "true");
            builder.UseSetting("StartupTasks:SeedReferenceData", "false");
        }
    }
}
