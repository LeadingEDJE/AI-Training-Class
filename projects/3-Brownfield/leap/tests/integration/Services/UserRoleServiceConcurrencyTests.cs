using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Services;

/// <summary>
/// Integration coverage for the read-then-write race in
/// <c>UserRoleService.SyncFromProfileAsync</c>. Exercises the real Oracle
/// MySQL provider via Testcontainers so the fix is proven against the same
/// driver behavior that surfaces in production (parallel-project Playwright
/// matrix runs).
/// </summary>
public class UserRoleServiceConcurrencyTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private static readonly Guid TargetEdjeId =
        Guid.Parse("aaaaaaaa-1111-2222-3333-aaaaaaaaaaaa");

    [Fact]
    public async Task SyncFromProfileAsync_ConcurrentDisjointDesiredSets_NoExceptionsAndConsistentState()
    {
        // Arrange: clean DB, then resolve the service from the host's DI
        // container so we exercise the real EF Core path against MySQL.
        await ResetDatabaseAsync();

        // Disjoint desired role sets — emulates parallel Playwright projects
        // hitting the API with different X-Dev-Roles for the same EdjeId,
        // each triggering DevBypassMiddleware -> SyncFromProfileAsync.
        // See INVESTIGATION.md for the live reproduction this guards against.
        //
        // NOTE on the contract: when N concurrent callers each say "I want
        // ONLY my single role; delete everything else", the final state is
        // by construction non-deterministic — they are mutually destructive.
        // What the race-safe implementation MUST guarantee is:
        //   (a) no caller throws (no 500 to the API caller),
        //   (b) the final state is a consistent subset of the union of all
        //       desired sets — no rogue rows, no half-committed transactions,
        //       no orphan rows from rolled-back batches.
        // We verify (a) by awaiting Task.WhenAll without a try/catch — any
        // exception fails the test. We verify (b) with ShouldBeSubsetOf.
        var desiredSets = new[]
        {
            new[] { "Admin" },
            new[] { "Manager" },
            new[] { "EDJEr" },
            new[] { "HR" },
            new[] { "Accounting" },
            new[] { "TimesheetProcessor" },
            new[] { "SalesRep" },
            new[] { "SuperAdmin" },
        };

        // Act: spawn N concurrent syncs against the same EdjeId. Each scope
        // gets its own DbContext so we model the per-request lifetime
        // accurately.
        var tasks = desiredSets.Select(async desired =>
        {
            using var scope = Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IUserRoleService>();
            await service.SyncFromProfileAsync(TargetEdjeId, desired, "concurrency-test");
        });

        await Task.WhenAll(tasks);

        // Assert: every sync completed (no exceptions) and the final state
        // is a subset of the union of every concurrent sync's desired set.
        using var assertScope = Services.CreateScope();
        var repo = assertScope.ServiceProvider.GetRequiredService<IUserRoleRepository>();
        var finalRoles = (await repo.GetByEdjeIdAsync(TargetEdjeId))
            .Select(r => r.Role)
            .ToHashSet();

        var allDesired = desiredSets.SelectMany(s => s).ToHashSet();
        finalRoles.ShouldBeSubsetOf(allDesired);
    }

    [Fact]
    public async Task SyncFromProfileAsync_ConcurrentSameDesiredSet_AllRowsSurvive()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act: 10 concurrent syncs all asking for the same desired set.
        // The race-safe implementation must converge on exactly that set,
        // with no duplicate-key 500s and no lost writes.
        var sameDesired = new[] { "Admin", "Manager", "EDJEr" };

        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            using var scope = Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IUserRoleService>();
            await service.SyncFromProfileAsync(TargetEdjeId, sameDesired, "concurrency-test-same");
        });

        await Task.WhenAll(tasks);

        // Assert: final state is exactly the desired set.
        using var assertScope = Services.CreateScope();
        var repo = assertScope.ServiceProvider.GetRequiredService<IUserRoleRepository>();
        var finalRoles = (await repo.GetByEdjeIdAsync(TargetEdjeId))
            .Select(r => r.Role)
            .ToHashSet();

        finalRoles.ShouldBe(sameDesired.ToHashSet(), ignoreOrder: true);
    }

    [Fact]
    public async Task PostUserRole_FollowedByGetByEmployee_ReturnsTheAssignedRole()
    {
        // Mirrors the 2 currently-skipped @auth-scenario-b Playwright tests at
        // the integration level: assign a role via POST, then GET it back. The
        // guard is that DevBypassMiddleware-driven SyncFromProfileAsync on the
        // readback request must not erase the just-written row, even with the
        // hardened implementation.
        await ResetDatabaseAsync();

        var assignRequest = new UserRoleRequest
        {
            EdjeId = TargetEdjeId,
            Role = "SuperAdmin",
            Reason = "Integration parity with @auth-scenario-b",
        };
        var assignResponse = await Client.PostAsJsonAsync(
            "/api/admin/user-roles",
            assignRequest,
            TestContext.Current.CancellationToken);
        assignResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act: GET back the roles for this EdjeId. The endpoint returns
        // IReadOnlyList<string>, NOT a list of objects — verifies the same
        // shape the un-skipped Playwright tests now consume.
        var getResponse = await Client.GetAsync(
            $"/api/admin/user-roles/by-employee/{TargetEdjeId}",
            TestContext.Current.CancellationToken);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var roles = await getResponse.Content.ReadFromJsonAsync<List<string>>(
            TestContext.Current.CancellationToken);
        roles.ShouldNotBeNull();
        roles.ShouldContain("SuperAdmin");
    }
}
