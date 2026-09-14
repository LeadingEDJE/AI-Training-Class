using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Jobs;
using LeadingEDJE.Leap.Api.Platform.Services;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// The scheduled half of ADR-007's retention (issues #145, #317).
/// </summary>
/// <remarks>
/// The job is deliberately thin — resolve a service, call one method, log — so these tests cover the
/// only behaviour it owns: that it calls the service, and that a failure does not escape. The purge
/// itself needs real PostgreSQL (a trigger, <c>SET LOCAL</c> and <c>ExecuteDelete</c>, none of which
/// the InMemory provider has) and is covered by <c>AuditRetentionServiceTests</c>.
/// </remarks>
public class AuditRetentionJobTests
{
    private sealed class StubRetentionService : IAuditRetentionService
    {
        public int Calls { get; private set; }

        public int Returns { get; init; }

        public Exception? Throws { get; init; }

        public Task<int> PurgeExpiredAuditEntriesAsync(CancellationToken cancellationToken)
        {
            Calls++;

            return Throws is not null ? Task.FromException<int>(Throws) : Task.FromResult(Returns);
        }
    }

    private static (AuditRetentionJob Job, StubRetentionService Service) Build(
        StubRetentionService service)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuditRetentionService>(service);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var provider = services.BuildServiceProvider();

        return (new AuditRetentionJob(provider.GetRequiredService<IServiceScopeFactory>()), service);
    }

    private static IJobExecutionContext CreateContext() =>
        new TestJobExecutionContext("AuditRetentionDaily3am");

    [Fact]
    public async Task Execute_RunsARetentionPass()
    {
        // Arrange
        var (job, service) = Build(new StubRetentionService { Returns = 7 });

        // Act
        await job.Execute(CreateContext());

        // Assert
        service.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Execute_WhenThePurgeThrows_DoesNotPropagate()
    {
        // Arrange — retention is housekeeping. A failed pass costs one night of unpurged rows and the
        // next pass fixes it, so it must never take the host down or leave Quartz retrying a broken
        // job. The failure is logged instead.
        var (job, _) = Build(new StubRetentionService { Throws = new InvalidOperationException("db down") });

        // Act
        var act = async () => await job.Execute(CreateContext());

        // Assert
        await act.ShouldNotThrowAsync();
    }

    [Fact]
    public async Task Execute_WhenNothingIsExpired_StillCompletes()
    {
        // Arrange
        var (job, service) = Build(new StubRetentionService { Returns = 0 });

        // Act
        await job.Execute(CreateContext());

        // Assert
        service.Calls.ShouldBe(1);
    }
}

/// <summary>Defaults for ADR-007's retention window (issue #86's configuration half).</summary>
public class AuditRetentionOptionsTests
{
    [Fact]
    public void DefaultsToTheAdrsTwoYears()
    {
        // Nothing sets this in any environment, so the default IS the deployed policy. If someone
        // changes it, ADR-007 has to change with it — which is the point of asserting it here rather
        // than trusting the literal in the property initialiser.
        new AuditRetentionOptions().AuditEntryYears.ShouldBe(2);
    }

    [Fact]
    public void BindsFromTheRetentionSection()
    {
        AuditRetentionOptions.SectionName.ShouldBe("Retention");
    }

    [Fact]
    public void CarriesNoSettingForBusinessRecordRetention()
    {
        // "Never purged" is the absence of a policy, not a large number of years. A knob here would
        // invite someone to put a number in it, and AC-24/AC-39 both read the full history.
        typeof(AuditRetentionOptions).GetProperties().Length.ShouldBe(1);
    }
}
