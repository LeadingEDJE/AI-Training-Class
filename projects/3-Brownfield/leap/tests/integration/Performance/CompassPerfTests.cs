using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Write;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
// Compass's own entities, aliased for the same reason as in the Compass integration tests: this
// file's namespace already ends in a Compass-adjacent segment.
using CompassEmployee = LeadingEDJE.Leap.Api.Modules.Compass.Employee;
using CompassEmployeeType = LeadingEDJE.Leap.Api.Modules.Compass.EmployeeType;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Performance;

/// <summary>
/// Server-side p95 harness for the Compass employee read (FR-087 / issue #79 / T009–T010).
/// </summary>
/// <remarks>
/// <para>
/// Measures server response time through the real HTTP pipeline against Testcontainers
/// Postgres, seeded to ~90 directory EDJErs. Client rendering is out of scope — that is the
/// Playwright / axe surface, not this suite.
/// </para>
/// <para>
/// The directory boundary still reads the Timesheet <c>public.employees</c> table today
/// (<see cref="LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories.CompassDirectoryRepository"/>).
/// Seed that table, not <c>compass.employee</c>, or the endpoint under test 404s and the harness
/// measures nothing.
/// </para>
/// <para>
/// CI fail-closed: <c>.github/workflows/ci.yml</c> lists this type name before the
/// integration suite runs and fails the job if the filter selects zero tests. Renaming the class
/// without updating that step is a red build, not a silent skip.
/// </para>
/// </remarks>
public class CompassPerfTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
    public CompassPerfTests(IntegrationTestFactory factory) : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string BaseUrl = "/api/compass/v1/employees";
    private const int SeedCount = 90;
    private const int WarmupRequests = 5;
    private const int SampleRequests = 40;
    private const int P95BudgetMs = 1000;

    /// <summary>
    /// Samples per write surface. Lower than <see cref="SampleRequests"/> because each write creates
    /// a row and needs its own seeded EDJEr, so the arrange cost grows with it.
    /// </summary>
    private const int WriteSampleRequests = 20;

    private const string AssignmentsUrl = "/api/compass/assignments";
    private const string SowsUrl = "/api/compass/v1/admin/sows";

    private HttpClient CreateCompassAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.PrivilegeOverrideHeader, RolePolicy.CompassAdminRole);
        return client;
    }

    private const int EmployeeTypeId = 1;

    private async Task<IReadOnlyList<int>> SeedDirectoryEmployeesAsync(int count)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        db.Set<CompassEmployeeType>().Add(new CompassEmployeeType
        {
            Id = EmployeeTypeId,
            TypeName = "Full Time",
            IsActive = true,
        });

        var ids = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            var employeeId = i + 1;
            ids.Add(employeeId);
            db.Set<CompassEmployee>().Add(new CompassEmployee
            {
                Id = employeeId,
                FirstName = $"Perf{i:D3}",
                LastName = "EDJEr",
                Email = $"compass-perf-{employeeId}@example.test",
                HireDate = new DateOnly(2020, 1, 1),
                EmployeeTypeId = EmployeeTypeId,
                IsActive = true,
                StateOfResidence = "OH",
                TimesheetRequired = true,
                CanSubmitUnder40 = false,
                IncludeInPayroll = true,
            });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return ids;
    }

    private static long Percentile95(IReadOnlyList<long> sortedAscending)
    {
        // Nearest-rank: ceil(0.95 * N), 1-based → index ceil(0.95*N)-1.
        var rank = (int)Math.Ceiling(0.95 * sortedAscending.Count);
        return sortedAscending[Math.Clamp(rank - 1, 0, sortedAscending.Count - 1)];
    }

    [Fact]
    public async Task GetById_P95ServerLatency_UnderOneSecond_WithNinetyEdjers()
    {
        // Arrange
        await ResetDatabaseAsync();
        var ids = await SeedDirectoryEmployeesAsync(SeedCount);
        var client = CreateCompassAdminClient();
        var ct = TestContext.Current.CancellationToken;
        var targetId = ids[ids.Count / 2];

        // Warmup — discard cold-start noise (JIT, connection pool, first EF plan).
        for (var i = 0; i < WarmupRequests; i++)
        {
            var warm = await client.GetAsync($"{BaseUrl}/{ids[i % ids.Count]}", ct);
            warm.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Act
        var samples = new List<long>(SampleRequests);
        var sw = new Stopwatch();
        for (var i = 0; i < SampleRequests; i++)
        {
            sw.Restart();
            var response = await client.GetAsync($"{BaseUrl}/{targetId}", ct);
            sw.Stop();
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            samples.Add(sw.ElapsedMilliseconds);
        }

        samples.Sort();
        var p50 = samples[samples.Count / 2];
        var p95 = Percentile95(samples);

        // Assert — FR-087 budget. Distribution rides in the failure message so a red CI run is diagnosable.
        p95.ShouldBeLessThan(
            P95BudgetMs,
            $"Compass employee GET p95 was {p95}ms (p50={p50}ms max={samples[^1]}ms n={samples.Count}) over a {SeedCount}-EDJEr seed (budget {P95BudgetMs}ms).");
    }

    /// <summary>
    /// The two write surfaces feature 010 added, against the p95 budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Constitution Principle IX requires p95 server response under one second on every surface, and
    /// says it is "verified by a perf test asserting server p95" — so a claim in a plan is not
    /// enforcement. This is that enforcement for <c>POST /api/compass/assignments</c> and
    /// <c>POST /api/compass/v1/admin/sows</c>.
    /// </para>
    /// <para>
    /// The assignment half measures feature 006's route rather than one of 010's. Feature 010
    /// originally had its own assignment create surface; merging main showed 006 had already shipped
    /// an equivalent one, so 010's was deleted and this test was repointed. The p95 obligation
    /// followed the route — it did not disappear with the duplicate.
    /// </para>
    /// <para>
    /// Writes are measured differently from the read above. Each POST creates a row, so the
    /// same request cannot be replayed — every sample targets a fresh assignment, and the SOW samples
    /// each need their own assignment because the exclusion constraint would reject a second
    /// overlapping period on the same one. The seeding cost is deliberately kept OUTSIDE the
    /// stopwatch: what is being measured is the write, not the arrange.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Writes_P95ServerLatency_UnderOneSecond_ForTheFeature010Surfaces()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeIds = await SeedDirectoryEmployeesAsync(WriteSampleRequests + WarmupRequests + 1);
        var clientId = await SeedPerfClientAsync();
        var client = _factory.AsCompassSuperAdmin();
        var ct = TestContext.Current.CancellationToken;

        // Warmup — discard cold-start noise before either measurement.
        for (var i = 0; i < WarmupRequests; i++)
        {
            var warm = await client.PostAsJsonAsync(
                AssignmentsUrl,
                new CreateAssignmentRequest(employeeIds[i], clientId, new DateOnly(2024, 1, 15), null, null),
                ct);
            warm.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        // Act — assignment creates, one per EDJEr so no request is a no-op.
        var assignmentSamples = new List<long>(WriteSampleRequests);
        var createdAssignmentIds = new List<int>(WriteSampleRequests);
        var sw = new Stopwatch();

        for (var i = 0; i < WriteSampleRequests; i++)
        {
            var request = new CreateAssignmentRequest(
                employeeIds[WarmupRequests + i], clientId, new DateOnly(2024, 1, 15), null, null);

            sw.Restart();
            var response = await client.PostAsJsonAsync(AssignmentsUrl, request, ct);
            sw.Stop();

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            assignmentSamples.Add(sw.ElapsedMilliseconds);

            var created = await response.Content.ReadFromJsonAsync<AssignmentRowDto>(ct);
            createdAssignmentIds.Add(created!.Id);
        }

        // Act — SOW creates. One per assignment: two periods on the same assignment would collide
        // with the exclusion constraint and turn a latency test into a constraint test.
        var sowSamples = new List<long>(WriteSampleRequests);

        for (var i = 0; i < WriteSampleRequests; i++)
        {
            var request = new CompassSowRequest(
                createdAssignmentIds[i],
                SowType.InitialContract,
                RateIncrease: false,
                SowStartDate: new DateOnly(2024, 1, 1),
                SowEndDate: new DateOnly(2024, 12, 31),
                Note: null);

            sw.Restart();
            var response = await client.PostAsJsonAsync(SowsUrl, request, ct);
            sw.Stop();

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            sowSamples.Add(sw.ElapsedMilliseconds);
        }

        assignmentSamples.Sort();
        sowSamples.Sort();
        var assignmentP95 = Percentile95(assignmentSamples);
        var sowP95 = Percentile95(sowSamples);

        // Assert — distribution rides in the message so a red CI run is diagnosable without a rerun.
        assignmentP95.ShouldBeLessThan(
            P95BudgetMs,
            $"Assignment POST p95 was {assignmentP95}ms (max={assignmentSamples[^1]}ms n={assignmentSamples.Count}, budget {P95BudgetMs}ms).");

        sowP95.ShouldBeLessThan(
            P95BudgetMs,
            $"SOW POST p95 was {sowP95}ms (max={sowSamples[^1]}ms n={sowSamples.Count}, budget {P95BudgetMs}ms).");
    }

    /// <summary>A client to hang the measured assignments from. Seeded outside the stopwatch.</summary>
    private async Task<int> SeedPerfClientAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var perfClient = new LeadingEDJE.Leap.Api.Modules.Compass.Client
        {
            ClientName = $"PerfClient-{Guid.NewGuid():N}"[..24],
            IsInternal = false,
        };
        db.Set<LeadingEDJE.Leap.Api.Modules.Compass.Client>().Add(perfClient);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return perfClient.Id;
    }
}
