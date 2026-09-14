using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// The coach drill-in and direct-reports listing (issue #245), against real PostgreSQL.
/// </summary>
/// <remarks>
/// <c>DirectReports</c> projects a NAVIGATION COLLECTION (<c>Employee.Coachees</c>) through
/// <c>.AsQueryable().Where(EdjerVisibility.For(tier)).OrderBy(...).Select(...)</c> — a shape the
/// InMemory provider in <c>tests/unit</c> evaluates as ordinary LINQ-to-Objects regardless of
/// whether Npgsql can render it. This exact class of defect has shipped — passing every unit
/// test and failing 500 in the wild — so this file's whole job is proving
/// the query translates.
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassEmployeeCoachAndDirectReportsTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassEmployeeCoachAndDirectReportsTests(IntegrationTestFactory factory)
        : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const int CoachId = 9;
    private const int ActiveReportId = 1;
    private const int InactiveReportId = 3;
    private const int NoCoacheesId = 5;

    private const int SortOrderCoachId = 30;
    private const int SortOrderNewestId = 31;
    private const int SortOrderOldestId = 32;
    private const int SortOrderTieAId = 33;
    private const int SortOrderTieBId = 34;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Route(int id) => $"/api/compass/team-directory/{id}";

    [Fact]
    public async Task Coachee_CarriesTheCoachsIdAndName_FromRealSql()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(ActiveReportId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();
        detail.CoachId.ShouldBe(CoachId);
        detail.Coach.ShouldBe("Cody Coach");
    }

    [Fact]
    public async Task ACoach_ListsTheirActiveDirectReports_ToABaselineViewer()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsBaseEdjErOnly().GetAsync(Route(CoachId), Token);

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "the Coachees projection (EdjerVisibility over a navigation collection) must translate to SQL"
        );
        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();
        detail.DirectReports.Select(r => r.Id).ShouldBe([ActiveReportId], ignoreOrder: true);
    }

    [Fact]
    public async Task ACoach_ListsInactiveDirectReportsToo_ForAnElevatedViewer()
    {
        // Arrange — BR-1's rule applies to Coachees the same as every other listing.
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(CoachId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();
        detail.DirectReports.Select(r => r.Id).ShouldBe([ActiveReportId, InactiveReportId], ignoreOrder: true);
    }

    [Fact]
    public async Task ACoach_DirectReports_CarryWhetherEachIsActive_ForAnElevatedViewer()
    {
        // Arrange — issue #399: an elevated viewer must be able to tell a former direct report apart
        // from a current one without cross-referencing another screen.
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(CoachId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();
        detail.DirectReports.Single(r => r.Id == ActiveReportId).IsActive.ShouldBe(true);
        detail.DirectReports.Single(r => r.Id == InactiveReportId).IsActive.ShouldBe(false);
    }

    [Fact]
    public async Task AnEdjerWithNoCoachees_ReturnsAnEmptyList_NotAnError()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(NoCoacheesId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();
        detail.DirectReports.ShouldBeEmpty();
    }

    [Fact]
    public async Task DirectReports_AreSortedByHireDateOldestFirst_WithLastNameBreakingTies()
    {
        // Arrange — issue #401: seniority order (oldest hire date first), ties broken alphabetically
        // by last name.
        await ResetDatabaseAsync();
        await SeedSortOrderFixtureAsync();

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(SortOrderCoachId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();
        detail.DirectReports.Select(r => r.Id)
            .ShouldBe([SortOrderOldestId, SortOrderTieAId, SortOrderTieBId, SortOrderNewestId]);
    }

    [Fact]
    public async Task AnEdjerWithNoCoach_CoachIdAndCoachAreBothNull()
    {
        // Arrange — Cody Coach is the top of this fixture's chain.
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(CoachId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();
        detail.CoachId.ShouldBeNull();
        detail.Coach.ShouldBeNull();
    }

    // ------------------------------------------------------------------ Helpers

    private async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<EmployeeType>()
            .Add(new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });

        context.Set<Employee>()
            .AddRange(
                new Employee
                {
                    Id = CoachId,
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
                    Id = ActiveReportId,
                    FirstName = "Ada",
                    LastName = "Active",
                    Email = "ada.active@example.test",
                    StateOfResidence = "OH",
                    EmployeeTypeId = 1,
                    CoachEmployeeId = CoachId,
                    HireDate = new DateOnly(2020, 1, 6),
                    IsActive = true,
                },
                new Employee
                {
                    Id = InactiveReportId,
                    FirstName = "Ivor",
                    LastName = "Inactive",
                    Email = "ivor.inactive@example.test",
                    StateOfResidence = "CA",
                    EmployeeTypeId = 1,
                    CoachEmployeeId = CoachId,
                    HireDate = new DateOnly(2016, 9, 1),
                    IsActive = false,
                },
                new Employee
                {
                    Id = NoCoacheesId,
                    FirstName = "Nora",
                    LastName = "Nobody",
                    Email = "nora.nobody@example.test",
                    StateOfResidence = "OH",
                    EmployeeTypeId = 1,
                    HireDate = new DateOnly(2019, 4, 1),
                    IsActive = true,
                }
            );

        await context.SaveChangesAsync(Token);
    }

    private async Task SeedSortOrderFixtureAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<EmployeeType>()
            .Add(new EmployeeType { Id = 2, TypeName = "Part Time", IsActive = true });

        context.Set<Employee>()
            .AddRange(
                new Employee
                {
                    Id = SortOrderCoachId,
                    FirstName = "Sasha",
                    LastName = "Seniorcoach",
                    Email = "sasha.seniorcoach@example.test",
                    StateOfResidence = "OH",
                    EmployeeTypeId = 2,
                    HireDate = new DateOnly(2010, 1, 1),
                    IsActive = true,
                },
                // Deliberately seeded newest-first so a naive "insertion order" pass would fail —
                // the fixture only proves the sort if it disagrees with the seed order.
                new Employee
                {
                    Id = SortOrderNewestId,
                    FirstName = "Nina",
                    LastName = "Newesthire",
                    Email = "nina.newesthire@example.test",
                    StateOfResidence = "OH",
                    EmployeeTypeId = 2,
                    CoachEmployeeId = SortOrderCoachId,
                    HireDate = new DateOnly(2023, 6, 1),
                    IsActive = true,
                },
                new Employee
                {
                    Id = SortOrderOldestId,
                    FirstName = "Otto",
                    LastName = "Oldesthire",
                    Email = "otto.oldesthire@example.test",
                    StateOfResidence = "OH",
                    EmployeeTypeId = 2,
                    CoachEmployeeId = SortOrderCoachId,
                    HireDate = new DateOnly(2011, 2, 1),
                    IsActive = true,
                },
                // Same hire date as SortOrderTieBId — the tie must resolve to "Alpha" before "Beta".
                new Employee
                {
                    Id = SortOrderTieAId,
                    FirstName = "Amy",
                    LastName = "Alphatie",
                    Email = "amy.alphatie@example.test",
                    StateOfResidence = "OH",
                    EmployeeTypeId = 2,
                    CoachEmployeeId = SortOrderCoachId,
                    HireDate = new DateOnly(2015, 5, 1),
                    IsActive = true,
                },
                new Employee
                {
                    Id = SortOrderTieBId,
                    FirstName = "Bea",
                    LastName = "Betatie",
                    Email = "bea.betatie@example.test",
                    StateOfResidence = "OH",
                    EmployeeTypeId = 2,
                    CoachEmployeeId = SortOrderCoachId,
                    HireDate = new DateOnly(2015, 5, 1),
                    IsActive = true,
                }
            );

        await context.SaveChangesAsync(Token);
    }
}
