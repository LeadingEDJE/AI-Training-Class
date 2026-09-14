using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// Derived client status against real PostgreSQL — feature 004 US4 (issue #61), T094-T095.
/// </summary>
/// <remarks>
/// <para>
/// This file is the only coverage that exists for the properties it asserts. Every other test of
/// the derivation runs on the EF Core InMemory provider, which translates nothing: it evaluates the
/// same expression tree in .NET, so it cannot distinguish a query that becomes SQL from one that
/// throws, and it cannot distinguish one round trip from N. Feature 004 US2 shipped an HTTP 500 that
/// every unit test passed — the "Project into a named type LAST" principle records it — and the
/// rule drawn from it is that every repository read needs at least one
/// real-PostgreSQL case whose job is simply "it translates."
/// </para>
/// <para>
/// Read the contract, not AC-42's PRD wording — they disagree on the exactly-today case, and the owner
/// decision the contract records (active for the whole calendar day) wins.
/// </para>
/// <para>
/// Why the tasks' named file is not this one. T094 and T095 name
/// <c>CompassAdminClientEndpointsTests.cs</c>, which belongs to US3's admin client surface — unbuilt.
/// The properties are observable on the Client Directory that feature 005 shipped, and this is the
/// file feature 005's own T067/T068 named and never created.
/// </para>
/// </remarks>
public class CompassClientDirectoryEndpointsTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private const string Route = "/api/compass/client-directory";

    private readonly IntegrationTestFactory _factory = factory;

    // Ids the assertions name, so a reader does not have to count seeded rows.
    private const int NeverAssignedClientId = 1;
    private const int OpenEndedClientId = 2;
    private const int EndsTodayClientId = 3;
    private const int AllEndedClientId = 4;

    // ------------------------------------------------------------------ The five cases, in SQL

    [Fact]
    public async Task ClientDirectory_DerivesAllFiveCases_AgainstRealPostgres()
    {
        // Arrange
        await ResetDatabaseAsync();
        var today = await SeedAsync();

        // Act
        var rows = await GetRows(string.Empty);

        // Assert — the exactly-today case is the one AC-42 names as the case independent
        // implementations get wrong, and the one an InMemory assertion cannot vouch for: whether the
        // comparison is inclusive is decided by the SQL Npgsql emits.
        Status(rows, NeverAssignedClientId).ShouldBe("Inactive", "no assignments at all (FR-034)");
        Status(rows, OpenEndedClientId).ShouldBe("Active", "an open-ended assignment (FR-030)");
        Status(rows, EndsTodayClientId).ShouldBe("Active", $"ends exactly today, {today} (FR-032)");
        Status(rows, AllEndedClientId).ShouldBe(
            "Former",
            "all assignments ended — FR-031, re-split by issue #274. Read against NeverAssigned above, "
                + "this pair IS the split, derived by real SQL rather than in memory");
    }

    [Fact]
    public async Task ClientDirectory_Search_And_SortBothDirections_AreTranslatable()
    {
        // Arrange — FR-030 makes status sortable, not merely displayed. Sorting on a value the
        // database never stored is the shape most likely to fall over in translation.
        //
        // Ordinal on three values orders Active < Former < Inactive (issue #274), which happens to
        // read as a sensible engagement ordering. That is a coincidence of spelling, not a designed
        // ordering, and this test asserts only that the sort is total and reversible — which is the
        // property the column needs.
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var byName = await GetRows("?sort=clientName");
        var byStatus = await GetRows("?sort=status");
        var byStatusDescending = await GetRows("?sort=status&desc=true");
        var searched = await GetRows("?search=ends");

        // Assert
        byName.Count.ShouldBe(4);
        byStatus.Count.ShouldBe(4);
        byStatusDescending.Select(r => r.Id).ShouldBe(byStatus.Select(r => r.Id).Reverse());
        searched.Select(r => r.Id).ShouldBe([EndsTodayClientId], "partial, case-insensitive (AC-12)");
    }

    // ------------------------------------------------------------------ T095 — the transition

    [Fact]
    public async Task AFormerClient_BecomesActive_ByAddingAnAssignmentAlone()
    {
        // Arrange — FR-040: no re-activation step exists, because there is nothing to trigger.
        // FR-039: no cascade either. Adding one assignment row is the whole act.
        //
        // The client starts as FORMER since issue #274, which makes this the re-engagement path the
        // issue's own wording describes: "we had assignments in the past but there are no current
        // edjers". Nothing about that word may make it harder to assign than any other client.
        await ResetDatabaseAsync();
        var today = await SeedAsync();
        (await StatusOf(AllEndedClientId)).ShouldBe("Former");

        // Act
        await AddAssignmentAsync(AllEndedClientId, today.AddDays(-1), endDate: null);

        // Assert — through the HTTP surface, so this is the value a consumer would see.
        (await StatusOf(AllEndedClientId)).ShouldBe("Active");
        Status(await GetRows(string.Empty), AllEndedClientId).ShouldBe("Active");
    }

    [Fact]
    public async Task AClientEndingToday_IsStillActive_WhenTheAssignmentIsTheOnlyOne()
    {
        // The boundary, end to end. Distinct from the five-case test above in that it reads the value
        // through the client VIEW as well — SC-005 is that both surfaces agree, which is only true
        // because one implementation produces both.
        await ResetDatabaseAsync();
        await SeedAsync();

        (await StatusOf(EndsTodayClientId)).ShouldBe("Active");
        Status(await GetRows(string.Empty), EndsTodayClientId).ShouldBe("Active");
    }

    // ------------------------------------------------------------------ T106 — status gates nothing

    [Fact]
    public async Task AZeroAssignmentClient_ReadsInactive_AndStaysFullyReachable()
    {
        // Arrange — FR-034 with FR-037: a brand-new client is Inactive from the moment it exists, and
        // Inactive must not hide it or gate reaching it. The prohibition is what makes the binary rule
        // safe (contract § 3).
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var listed = await GetRows(string.Empty);
        var view = await GetClientView(NeverAssignedClientId);

        // Assert
        listed.ShouldContain(r => r.Id == NeverAssignedClientId, "an Inactive client is still listed");
        Status(listed, NeverAssignedClientId).ShouldBe("Inactive");
        view.Status.ShouldBe("Inactive");
        view.ClientName.ShouldBe("NeverAssigned", "the view resolves, rather than 404ing on status");
    }

    [Fact]
    public async Task EveryClient_ResolvesToExactlyOneOfThreeValues_WithNoFourthState()
    {
        // FR-028's totality, asserted where it could actually break: a LEFT-join shape or an
        // outer-join miss would surface as an empty string here, not as an exception. Issue #274 added
        // a value without weakening that — the emptiness assertion below is the one doing the work.
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await GetRows(string.Empty);

        rows.Select(r => r.Status).Distinct().ShouldBeSubsetOf(["Active", "Inactive", "Former"]);
        rows.ShouldAllBe(r => r.Status.Length > 0);
    }

    // ------------------------------------------------------------------ T094 — set-wise, not per row

    [Fact]
    public async Task ClientDirectory_DerivesStatusSetWise_SoQueryCountDoesNotGrowWithClientCount()
    {
        // Arrange — the contract (§ 2) names per-row derivation an N+1 that puts AC-NFR-4's p95 under
        // one second at risk. Counting commands for one client count proves nothing; the property is
        // that the count is INDEPENDENT of the number of clients.
        await ResetDatabaseAsync();
        var today = await SeedAsync();

        // Act
        var fourClients = await CountCommandsForClientDirectoryAsync(today);
        await SeedAdditionalClientsAsync(count: 26, today);
        var thirtyClients = await CountCommandsForClientDirectoryAsync(today);

        // Assert
        thirtyClients.Rows.ShouldBe(30, "the second measurement must really have more clients");
        thirtyClients.Commands.ShouldBe(
            fourClients.Commands,
            $"the listing issued {fourClients.Commands} command(s) for 4 clients and "
                + $"{thirtyClients.Commands} for 30. Status must be derived SET-WISE — one query "
                + "answering \"which of these are active\" for the whole set. A count that grows with "
                + "the row count is the N+1 the contract forbids.");
    }

    // ------------------------------------------------------------------ Issue #430 — the same
    // invariant, on the BOUNDARY's client list

    /// <summary>
    /// <c>CompassDirectoryRepository.GetClientsAsync</c> issues THREE queries however many clients
    /// exist (issue #430).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both <c>ICompassDirectoryRepository</c> and its implementation state this number in their
    /// remarks, and until now nothing measured it. A refactor to per-row derivation — the N+1
    /// those remarks explicitly forbid, and which <c>IClientStatusDerivation.Of</c> makes a one-line
    /// change away — would have passed every test, because the EF Core InMemory provider evaluates the
    /// expression tree in .NET and cannot distinguish one round trip from N.
    /// </para>
    /// <para>
    /// Housed in this file to REUSE the counting harness, not because the boundary belongs to
    /// the read surface. <c>CountCommandsAsync</c> and <c>CommandCountingInterceptor</c> already exist
    /// here for <c>GetClientDirectoryAsync</c> — the method <c>GetClientsAsync</c>'s own remarks say
    /// it copies — and a second interceptor elsewhere would be two things to keep in step.
    /// </para>
    /// <para>
    /// Asserts the literal 3 as well as the independence: the count not GROWING is the property that
    /// matters, but the documented number is a claim in its own right, and a read that quietly became
    /// four round trips would still satisfy independence.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task BoundaryClientList_IssuesThreeQueries_HoweverManyClientsExist()
    {
        // Arrange
        await ResetDatabaseAsync();
        var today = await SeedAsync();

        // Act
        var fourClients = await CountCommandsForBoundaryClientListAsync();
        await SeedAdditionalClientsAsync(count: 26, today);
        var thirtyClients = await CountCommandsForBoundaryClientListAsync();

        // Assert
        fourClients.Rows.ShouldBe(4, "the first measurement must really have four clients");
        thirtyClients.Rows.ShouldBe(30, "the second measurement must really have more clients");

        fourClients.Commands.ShouldBe(
            3,
            $"the boundary client list issued {fourClients.Commands} command(s): two scoped id "
                + "projections answering \"which of these are active\" and \"which have ever been "
                + "assigned\" for the whole set, then one read of the rows. Both this repository and "
                + "its interface promise three");
        thirtyClients.Commands.ShouldBe(
            fourClients.Commands,
            $"the boundary client list issued {fourClients.Commands} command(s) for 4 clients and "
                + $"{thirtyClients.Commands} for 30. Status must be derived SET-WISE; a count that "
                + "grows with the row count is the N+1 both remarks forbid");
    }

    // ------------------------------------------------------------------ Issue #243 — internal clients

    [Fact]
    public async Task ClientView_WithholdsViewSowAffordance_ForAnInternalClient_AgainstRealPostgres()
    {
        // Arrange — an internal (EDJE-to-EDJE) client has no contracts, so CanViewSow must never be
        // true for its assignments, even for an elevated role that would otherwise get it. The
        // predicate combines the viewer-tier check with `c.IsInternal` inside the same Select
        // projection, and only a real-Postgres run proves that combination still translates
        // ("Project into a named type LAST").
        await ResetDatabaseAsync();
        const int internalClientId = 500;
        await SeedInternalClientWithAssignmentAsync(internalClientId);

        var response = await _factory.AsCompassAdmin()
            .GetAsync($"{Route}/{internalClientId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK, $"GET {Route}/{internalClientId} must be translatable to SQL");
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.ShouldNotContain("canViewSow", Case.Insensitive);
    }

    // ------------------------------------------------------------------ Helpers

    private static string Status(IEnumerable<ClientDirectoryRowDto> rows, int clientId) =>
        rows.Single(r => r.Id == clientId).Status;

    private async Task<string> StatusOf(int clientId) => (await GetClientView(clientId)).Status;

    private async Task<List<ClientDirectoryRowDto>> GetRows(string query)
    {
        var response = await _factory.AsBaseEdjErOnly()
            .GetAsync($"{Route}{query}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK, $"GET {Route}{query} must be translatable to SQL");

        return await response.Content.ReadFromJsonAsync<List<ClientDirectoryRowDto>>(
            TestContext.Current.CancellationToken) ?? [];
    }

    private async Task<ClientViewDto> GetClientView(int clientId)
    {
        var response = await _factory.AsBaseEdjErOnly()
            .GetAsync($"{Route}/{clientId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK, $"GET {Route}/{clientId} must be translatable to SQL");

        return await response.Content.ReadFromJsonAsync<ClientViewDto>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException($"no client view body for client {clientId}");
    }

    /// <summary>Runs the read-surface listing through a context whose commands are counted.</summary>
    private async Task<(int Commands, int Rows)> CountCommandsForClientDirectoryAsync(DateOnly today) =>
        await CountCommandsAsync(async context =>
        {
            var repository = new CompassReadRepository(context, new ClientStatusDerivation());

            var rows = await repository.GetClientDirectoryAsync(
                new ClientDirectoryQuery(), today, TestContext.Current.CancellationToken);

            return rows.Count;
        });

    /// <summary>Runs the BOUNDARY client list through a context whose commands are counted.</summary>
    /// <remarks>
    /// The business date comes from the host container rather than being fabricated, so this read
    /// resolves "today" exactly as the running application does.
    /// </remarks>
    private async Task<(int Commands, int Rows)> CountCommandsForBoundaryClientListAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var businessDate = scope.ServiceProvider.GetRequiredService<ICompassBusinessDate>();

        return await CountCommandsAsync(async context =>
        {
            var repository = new CompassDirectoryRepository(
                context, new ClientStatusDerivation(), businessDate);

            var rows = await repository.GetClientsAsync(TestContext.Current.CancellationToken);

            return rows.Count;
        });
    }

    /// <summary>
    /// Runs one read through a context whose commands are counted, and reports both numbers.
    /// </summary>
    /// <remarks>
    /// A hand-built context rather than the host's, because the interceptor has to be attached at
    /// options-build time and the count must cover only this call. The connection string is taken from
    /// the host's own context, so both talk to the same Testcontainers database.
    /// </remarks>
    private async Task<(int Commands, int Rows)> CountCommandsAsync(
        Func<LeapDbContext, Task<int>> read)
    {
        using var scope = _factory.Services.CreateScope();
        var hostContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var counter = new CommandCountingInterceptor();
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseNpgsql(hostContext.Database.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(counter)
            .Options;

        await using var context = new LeapDbContext(options);
        var rows = await read(context);

        return (counter.Count, rows);
    }

    /// <summary>The four contract cases, plus one employee to hang assignments from.</summary>
    private async Task<DateOnly> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Anchored to the business date the routes themselves use, never to an absolute date: a
        // fixture pinned to a literal passes the day it is written and fails later (SC-005).
        var today = scope.ServiceProvider.GetRequiredService<ICompassBusinessDate>().Today();

        context.Set<EmployeeType>().Add(
            new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });

        context.Set<Employee>().Add(new Employee
        {
            Id = 1,
            FirstName = "Ada",
            LastName = "Assigned",
            Email = "ada.assigned@example.test",
            StateOfResidence = "OH",
            EmployeeTypeId = 1,
            HireDate = today.AddYears(-2),
            IsActive = true,
        });

        context.Set<Client>().AddRange(
            new Client { Id = NeverAssignedClientId, ClientName = "NeverAssigned" },
            new Client { Id = OpenEndedClientId, ClientName = "OpenEnded" },
            new Client { Id = EndsTodayClientId, ClientName = "EndsToday" },
            new Client { Id = AllEndedClientId, ClientName = "AllEnded" });

        context.Set<ClientAssignment>().AddRange(
            Assignment(1, OpenEndedClientId, today.AddMonths(-6), endDate: null),
            Assignment(2, EndsTodayClientId, today.AddMonths(-6), today),
            Assignment(3, AllEndedClientId, today.AddMonths(-6), today.AddDays(-1)),

            // A second ended assignment, so "all ended" means all of them and not merely the latest.
            Assignment(4, AllEndedClientId, today.AddYears(-2), today.AddMonths(-9)));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return today;
    }

    private async Task SeedAdditionalClientsAsync(int count, DateOnly today)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // A mix of active and inactive, so the extra clients exercise both branches of the predicate
        // rather than letting an accidental short-circuit look like set-wise derivation.
        for (var i = 0; i < count; i++)
        {
            var id = 100 + i;
            context.Set<Client>().Add(new Client { Id = id, ClientName = $"Bulk {id}" });

            if (i % 2 == 0)
            {
                context.Set<ClientAssignment>().Add(
                    Assignment(500 + i, id, today.AddMonths(-3), endDate: null));
            }
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task AddAssignmentAsync(int clientId, DateOnly startDate, DateOnly? endDate)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<ClientAssignment>().Add(Assignment(900, clientId, startDate, endDate));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>One internal client with one assignment, for the issue #243 SOW-affordance case.</summary>
    private async Task SeedInternalClientWithAssignmentAsync(int clientId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<ICompassBusinessDate>().Today();

        context.Set<EmployeeType>().Add(
            new EmployeeType { Id = clientId, TypeName = "Full Time", IsActive = true });

        context.Set<Employee>().Add(new Employee
        {
            Id = clientId,
            FirstName = "Internal",
            LastName = "Edjer",
            Email = $"internal.edjer.{clientId}@example.test",
            StateOfResidence = "OH",
            EmployeeTypeId = clientId,
            HireDate = today.AddYears(-1),
            IsActive = true,
        });

        context.Set<Client>().Add(
            new Client { Id = clientId, ClientName = "Leading EDJE (Internal)", IsInternal = true });

        context.Set<ClientAssignment>().Add(
            Assignment(clientId, clientId, today.AddMonths(-1), endDate: null, employeeId: clientId));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static ClientAssignment Assignment(
        int id, int clientId, DateOnly start, DateOnly? endDate, int employeeId = 1) =>
        new()
        {
            Id = id,
            EmployeeId = employeeId,
            ClientId = clientId,
            StartDate = start,
            EndDate = endDate,
        };

    /// <summary>Counts the SQL commands a context executes.</summary>
    private sealed class CommandCountingInterceptor : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
