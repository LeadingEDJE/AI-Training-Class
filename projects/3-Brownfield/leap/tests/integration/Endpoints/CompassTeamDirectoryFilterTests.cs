using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// AC-6's <c>employeeType</c> and <c>state</c> filters, and the last-name search, against real
/// PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// These three parameters had no real-database coverage until the Team Directory screen started
/// sending them. <see cref="CompassTeamDirectorySortTests"/> covers every value <c>sort</c> takes
/// — because a <c>Queryable.Reverse()</c> that only PostgreSQL rejects once shipped through a green
/// unit suite — but the filter clauses were exercised only by the InMemory provider, which translates
/// nothing and evaluates the same expression tree in .NET. A parameter no client sent was a parameter
/// nothing could break; now that the search row sends all three, each needs a case whose job is simply
/// it translates.
/// </para>
/// <para>
/// The employee-type clause is the one worth naming: it filters through the nullable
/// <c>EmployeeType</c> navigation, which is exactly the shape that turns a LEFT join into an inner one
/// and silently drops rows. Hence an assertion on which EDJErs come back, not just on the count.
/// </para>
/// <para>
/// Combining the filters is asserted too. Each clause composes onto the same <c>IQueryable</c>, and a
/// combination is where an <c>OR</c> written as an <c>AND</c> — or a re-assignment dropped by mistake —
/// shows up: individually they would both still pass.
/// </para>
/// </remarks>
public class CompassTeamDirectoryFilterTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private readonly IntegrationTestFactory _factory = factory;

    [Fact]
    public async Task FilteringByEmployeeType_IsTranslatable_AndKeepsOnlyThatType()
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await GetRows("?employeeType=Full%20Time");

        rows.Select(r => r.LastName).ShouldBe(["Coach", "Nocoach"], ignoreOrder: true);
    }

    [Fact]
    public async Task FilteringByEmployeeType_MatchesTheNameExactly()
    {
        // The screen offers the exact names the directory reports, so a partial match here would mean
        // choosing "Full Time" could also return a hypothetical "Full Time (Contract)".
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await GetRows("?employeeType=Full");

        rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task FilteringByState_IsTranslatable_AndKeepsOnlyThatState()
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await GetRows("?state=TX");

        rows.Select(r => r.LastName).ShouldBe(["Active"]);
    }

    [Fact]
    public async Task FiltersCompose_RatherThanReplacingEachOther()
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        // Full Time holds Coach (OH) and Nocoach (CA); adding OH must leave exactly Coach.
        var rows = await GetRows("?employeeType=Full%20Time&state=OH");

        rows.Select(r => r.LastName).ShouldBe(["Coach"]);
    }

    [Fact]
    public async Task FilteringByCoach_IsTranslatable_AndKeepsOnlyThatCoachsTeam()
    {
        // Issue #655: narrows the directory to one coach's team, by the coach's own id — a name
        // alone would not be safe against two coaches sharing a display name.
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await GetRows("?coachId=1");

        rows.Select(r => r.LastName).ShouldBe(["Active"]);
    }

    [Fact]
    public async Task FilteringByCoach_ForAnIdNoOneReportsTo_YieldsAnEmptyList()
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await GetRows("?coachId=999999");

        rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_RowsCarryTheCoachsOwnId_FromRealSql()
    {
        // Issue #245 follow-up: the Team Directory's coach cell is a link into the coach's own
        // record, which needs `CoachId` alongside the display name — the same null-conditional
        // navigation-property shape as `Coach` itself, proven translatable here rather than assumed.
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await GetRows("?state=TX");

        rows.Single(r => r.LastName == "Active").CoachId.ShouldBe(1);
    }

    [Fact]
    public async Task SearchFiltersAndSortCompose_WithoutLosingAnyOfThem()
    {
        // What the screen actually sends once a viewer has used the search row: every parameter at once.
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await GetRows("?search=coach&employeeType=Full%20Time&state=OH&sort=lastName&desc=true");

        rows.Select(r => r.LastName).ShouldBe(["Coach"]);
    }

    [Fact]
    public async Task SearchIsCaseInsensitive_AndPartial()
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await GetRows("?search=OAC");

        // "Coach" and "Nocoach" both contain it, whatever case the term arrives in.
        rows.Select(r => r.LastName).ShouldBe(["Coach", "Nocoach"], ignoreOrder: true);
    }

    [Fact]
    public async Task AnUnmatchedFilterYieldsAnEmptyList_NotAnError()
    {
        // The screen's "no EDJErs match" state depends on this being 200 + [] rather than a 404 or a 500.
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await GetRows("?state=ZZ");

        rows.ShouldBeEmpty();
    }

    /// <summary>
    /// FR-032 — a future-dated assignment is NOT a current client (issue #77 follow-on, 2026-08-20).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This was a live defect, and the team-directory VISUAL baseline is what caught it.
    /// <c>CompassReadRepository</c> derived CURRENT CLIENT(S) from <c>IsCurrent</c> alone, which is
    /// <c>EndDate == null || EndDate &gt;= today</c> and never reads <c>StartDate</c> — so an
    /// assignment starting tomorrow rendered as though the EDJEr were on it today. Nothing in the seed
    /// started in the future until feature 007 added a fixture for exactly this, at which point the
    /// screenshot diff surfaced it on a row nobody was looking at.
    /// </para>
    /// <para>
    /// Asserted at the API rather than left to the baseline: a screenshot proves the pixels changed, not
    /// which rule was wrong, and a re-baseline would have recorded the defect as expected output.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAssignmentStartingTomorrow_IsNotACurrentClient()
    {
        await ResetDatabaseAsync();
        await SeedAsync();
        var today = await SeedFutureStartAssignmentAsync();

        var rows = await GetRows(string.Empty);

        var ada = rows.Where(r => r.LastName == "Active").ToList().ShouldHaveSingleItem();
        ada.CurrentAssignments.ShouldBeEmpty(
            $"the only assignment starts {today.AddDays(1):yyyy-MM-dd}, after the business date "
                + $"{today:yyyy-MM-dd} — it has not begun, so it is not a current client");
    }

    /// <summary>
    /// Gives Ada Active one assignment that starts TOMORROW and never ends. Open-ended deliberately:
    /// a null end date is what makes `IsCurrent` true on its own, so this is the shape that exposes the
    /// missing `HasStarted`.
    /// </summary>
    private async Task<DateOnly> SeedFutureStartAssignmentAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<ICompassBusinessDate>().Today();

        context.Set<Client>().Add(new Client { Id = 1, ClientName = "Tomorrow Industries" });
        context.Set<ClientAssignment>().Add(new ClientAssignment
        {
            Id = 1,
            EmployeeId = 2,
            ClientId = 1,
            StartDate = today.AddDays(1),
            EndDate = null,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return today;
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

    /// <summary>Three active EDJErs across two employee types and three states, one without a coach.</summary>
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
