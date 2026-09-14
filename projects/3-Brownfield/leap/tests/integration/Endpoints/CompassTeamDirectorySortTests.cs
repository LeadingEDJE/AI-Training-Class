using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Authorization;
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
/// AC-6's column sort, in both directions, against real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// This file exists because of a bug that shipped: <c>Sort()</c> ordered ascending and then called
/// <c>Queryable.Reverse()</c>, which Npgsql cannot translate. Every <c>desc=true</c> request threw
/// against a real database while every unit test passed, because the InMemory provider evaluates
/// <c>Reverse()</c> in memory quite happily.
/// </para>
/// <para>
/// The lesson generalises past this one method: a sort assertion against the InMemory provider
/// proves ordering, not translatability. Every sortable column is therefore exercised here in
/// both directions — the ascending half is cheap and guards the same way if a branch is ever
/// rewritten.
/// </para>
/// </remarks>
public class CompassTeamDirectorySortTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    private readonly IntegrationTestFactory _factory = factory;

    /// <summary>Every column AC-6 allows sorting by, plus the unrecognised-value fallback.</summary>
    public static TheoryData<string> SortableColumns() =>
        ["lastName", "firstName", "email", "employeeType", "state", "coach", "hireDate", "notAColumn"];

    [Theory]
    [MemberData(nameof(SortableColumns))]
    public async Task Sorting_Descending_IsTranslatableAndOrdersOppositeToAscending(string column)
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        var ascending = await GetRows($"?sort={column}");
        var descending = await GetRows($"?sort={column}&desc=true");

        // The bug was a 500, so simply getting rows back is most of the assertion. The reversal check
        // is what stops a "fix" that translates but ignores the flag.
        descending.Count.ShouldBe(ascending.Count);
        descending.Select(r => r.Id).ShouldBe(ascending.Select(r => r.Id).Reverse());
    }

    [Theory]
    [MemberData(nameof(SortableColumns))]
    public async Task Sorting_Ascending_IsTranslatable(string column)
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await GetRows($"?sort={column}");

        rows.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task SortingByCoach_KeepsCoachlessEdjers_EvenDescending()
    {
        // The nullable-navigation branch. Ordering by the coach's surname must not turn the LEFT
        // join into an inner one and drop every coachless EDJEr — the internal, non-billable staff.
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await GetRows("?sort=coach&desc=true");

        rows.ShouldContain(r => r.LastName == "Nocoach");
    }

    [Fact]
    public async Task SortingByCurrentClients_OrdersAlphabetically_WithUnassignedLast()
    {
        // Feature 008 FR-010, from mockup annotation TD-3 ("every column header sorts") and Journey
        // Map v6 J2 step 4 ("EDJEr sorts by any column").
        //
        // THIS IS THE TRANSLATION TEST, not merely an ordering one. `Current Client(s)` is a projected
        // COLLECTION, so ordering by it is a correlated subquery — the one shape the unit suite
        // cannot catch, because the InMemory provider evaluates the expression in .NET where it
        // always "works". If this branch cannot
        // translate, every real request 500s while every unit test stays green.
        //
        // The semantics are a DECISION, not an inheritance: neither the mockup, PRD v11 nor the
        // Journey Map says what "sort by current clients" means for a multi-valued column. Alphabetical
        // by the employee's first current client, unassigned last — because it matches what the cell
        // displays, which is the only ordering a viewer can predict from the screen.
        await ResetDatabaseAsync();
        await SeedAsync();
        await SeedAssignmentsAsync();

        var rows = await GetRows("?sort=currentClients");

        // Ada -> Acme, Cody -> Zenith, Ned -> none.
        rows.Select(r => r.FirstName).ShouldBe(["Ada", "Cody", "Ned"]);
    }

    [Fact]
    public async Task SortingByCurrentClients_Descending_ReversesAndStillTranslates()
    {
        await ResetDatabaseAsync();
        await SeedAsync();
        await SeedAssignmentsAsync();

        var ascending = await GetRows("?sort=currentClients");
        var descending = await GetRows("?sort=currentClients&desc=true");

        descending.Select(r => r.Id).ShouldBe(ascending.Select(r => r.Id).Reverse());
    }

    [Fact]
    public async Task SortingByCurrentClients_IgnoresEndedAssignments()
    {
        // The column shows CURRENT clients, so the ordering key must come from the same set the cell
        // renders. An ended assignment that still influenced the order would sort a row by a client
        // the viewer cannot see — the ordering and the visible value disagreeing is worse than either
        // being wrong on its own.
        await ResetDatabaseAsync();
        await SeedAsync();
        await SeedAssignmentsAsync();

        var rows = await GetRows("?sort=currentClients");

        // Ned's only assignment ended in the past, so he sorts as unassigned and his cell is empty.
        var ned = rows.Single(r => r.FirstName == "Ned");
        ned.CurrentAssignments.ShouldBeEmpty();
        rows[^1].FirstName.ShouldBe("Ned");
    }

    [Fact]
    public async Task SortingByCurrentClients_IssuesOneQuery_HoweverManyEdjers()
    {
        // AC-NFR-4 (p95 under one second), guarded the way this repository already guards it for the
        // client directory: assert the command count is INDEPENDENT of the row count, rather than
        // measuring a wall-clock number that would vary with the machine.
        //
        // This column is the one place in the feature where that could regress. Ordering by a
        // projected collection is a correlated subquery, and the failure mode is not a 500 — it is EF
        // deciding it cannot translate the ordering, pulling the assignments back per row, and
        // producing an N+1 that is correct, silent, and slow. A count that grows with the EDJEr count
        // is exactly that.
        await ResetDatabaseAsync();
        await SeedAsync();
        await SeedAssignmentsAsync();

        var threeEdjers = await CountCommandsForSortedDirectoryAsync();
        await SeedManyEdjersAsync(count: 30);
        var manyEdjers = await CountCommandsForSortedDirectoryAsync();

        manyEdjers.Rows.ShouldBeGreaterThan(threeEdjers.Rows, "the extra EDJErs must actually be there");
        manyEdjers.Commands.ShouldBe(
            threeEdjers.Commands,
            $"sorting by current clients issued {threeEdjers.Commands} command(s) for 3 EDJErs and "
                + $"{manyEdjers.Commands} for {manyEdjers.Rows}. The ordering must be resolved IN SQL "
                + "as one query; a count that grows with the row count is an N+1 that puts AC-NFR-4's "
                + "p95 at risk while every functional assertion still passes.");
    }

    /// <summary>Runs the sorted listing through a context whose commands are counted.</summary>
    /// <remarks>
    /// A hand-built context rather than the host's, because the interceptor has to be attached at
    /// options-build time and the count must cover only this call. Same shape as
    /// <c>CompassClientDirectoryEndpointsTests</c>, whose interceptor is private to that class.
    /// </remarks>
    private async Task<(int Commands, int Rows)> CountCommandsForSortedDirectoryAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var hostContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<ICompassBusinessDate>().Today();

        var counter = new CommandCountingInterceptor();
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseNpgsql(hostContext.Database.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(counter)
            .Options;

        await using var context = new LeapDbContext(options);
        var repository = new CompassReadRepository(context, new ClientStatusDerivation());

        var rows = await repository.GetTeamDirectoryAsync(
            new TeamDirectoryQuery(Sort: "currentClients"),
            CompassTier.Baseline,
            today,
            TestContext.Current.CancellationToken);

        return (counter.Count, rows.Count);
    }

    /// <summary>Enough extra active EDJErs that an N+1 would show up as a command-count jump.</summary>
    private async Task SeedManyEdjersAsync(int count)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        for (var i = 0; i < count; i++)
        {
            context.Set<Employee>().Add(new Employee
            {
                Id = 100 + i,
                FirstName = $"Bulk{i:D2}",
                LastName = $"Surname{i:D2}",
                Email = $"bulk{i:D2}@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = 1,
                HireDate = new DateOnly(2022, 1, 1),
                IsActive = true,
            });
            // Every other one gets a current assignment, so the subquery has real work per row rather
            // than resolving to NULL for the whole bulk set.
            if (i % 2 == 0)
            {
                context.Set<ClientAssignment>().Add(new ClientAssignment
                {
                    Id = 100 + i,
                    EmployeeId = 100 + i,
                    ClientId = (i % 2) + 1,
                    StartDate = new DateOnly(2022, 2, 1),
                });
            }
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

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
    }

    /// <summary>Two current assignments with alphabetically distinct clients, plus one ended.</summary>
    private async Task SeedAssignmentsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<Client>().AddRange(
            new Client { Id = 1, ClientName = "Acme Industrial" },
            new Client { Id = 2, ClientName = "Zenith Mutual" });

        context.Set<ClientAssignment>().AddRange(
            // Ada -> Acme, open-ended, so current.
            new ClientAssignment { Id = 1, EmployeeId = 2, ClientId = 1, StartDate = new DateOnly(2021, 1, 1) },
            // Cody -> Zenith, current.
            new ClientAssignment { Id = 2, EmployeeId = 1, ClientId = 2, StartDate = new DateOnly(2021, 1, 1) },
            // Ned -> Acme, but ENDED. He must sort as unassigned.
            new ClientAssignment
            {
                Id = 3,
                EmployeeId = 3,
                ClientId = 1,
                StartDate = new DateOnly(2019, 1, 1),
                EndDate = new DateOnly(2020, 1, 1),
            });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<TeamDirectoryRowDto>> GetRows(string query)
    {
        var response = await _factory.AsBaseEdjErOnly()
            .GetAsync($"/api/compass/team-directory{query}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"GET /api/compass/team-directory{query} must be translatable to SQL");

        return await response.Content.ReadFromJsonAsync<List<TeamDirectoryRowDto>>(
            TestContext.Current.CancellationToken) ?? [];
    }

    /// <summary>Three active EDJErs with distinct values in every sortable column, one without a coach.</summary>
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<EmployeeType>().AddRange(
            new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true },
            new EmployeeType { Id = 2, TypeName = "Contractor", IsActive = true });

        context.Set<Employee>().AddRange(
            new Employee
            {
                Id = 1,
                FirstName = "Cody",
                LastName = "Coach",
                Email = "cody.coach@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = 1,
                HireDate = new DateOnly(2015, 3, 1),
                IsActive = true,
            },
            new Employee
            {
                Id = 2,
                FirstName = "Ada",
                LastName = "Active",
                Email = "ada.active@example.test",
                StateOfResidence = "TX",
                EmployeeTypeId = 2,
                CoachEmployeeId = 1,
                HireDate = new DateOnly(2020, 1, 15),
                IsActive = true,
            },
            new Employee
            {
                Id = 3,
                FirstName = "Ned",
                LastName = "Nocoach",
                Email = "ned.nocoach@example.test",
                StateOfResidence = "CA",
                EmployeeTypeId = 1,
                HireDate = new DateOnly(2017, 8, 1),
                IsActive = true,
            });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
