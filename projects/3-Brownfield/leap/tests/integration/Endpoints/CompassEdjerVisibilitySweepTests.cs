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
/// US3 / issue #72 — the active-inactive visibility rule holds across every EDJEr listing.
/// </summary>
/// <remarks>
/// <para>
/// The individual surfaces each assert this for themselves. This file exists because the property is
/// one of the set of screens, not of any one of them: AC-9 governs "an employee detail or
/// any EDJEr listing", and `#92` states it in bold for the same reason. A fourth listing added
/// later that forgets the rule passes every per-surface suite and fails here.
/// </para>
/// <para>
/// Against real PostgreSQL, because the rule is a query predicate. An in-memory provider will happily
/// evaluate a filter the database cannot translate.
/// </para>
/// </remarks>
public class CompassEdjerVisibilitySweepTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private readonly IntegrationTestFactory _factory = factory;

    /// <summary>
    /// Every surface that LISTS other EDJErs, named so a failure says which one leaked.
    /// </summary>
    /// <remarks>
    /// The employee detail is deliberately not here. AC-9 names it alongside the listings, but it
    /// shows ONE EDJEr rather than a set — so the rule applies to whether the record is reachable at
    /// all, not to what it contains. That direction is asserted separately below.
    /// </remarks>
    public static TheoryData<string, string> AllEdjerListings() => new()
    {
        { "Team Directory", "/api/compass/team-directory?status=all" },
        { "client assignment history", "/api/compass/client-directory/1" },
    };

    [Theory]
    [MemberData(nameof(AllEdjerListings))]
    public async Task BaselineViewer_ReceivesNoInactiveEdjer_FromAnyListing(string listing, string url)
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        var response = await _factory.AsBaseEdjErOnly()
            .GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldNotContain(
            "Inactive",
            Case.Insensitive,
            $"the {listing} leaked a former EDJEr to a baseline viewer");
    }

    [Theory]
    [MemberData(nameof(AllEdjerListings))]
    public async Task EveryElevatedRole_ReceivesInactiveEdjers_FromEveryListing(string listing, string url)
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        // All three elevated strings plus the Compass root. BR-1 treats them identically for
        // visibility, so a check written against one would miss a surface wired to the wrong role.
        HttpClient[] elevated =
        [
            _factory.AsCompassAdmin(),
            _factory.AsCompassOps(),
            _factory.AsCompassSales(),
            _factory.AsCompassSuperAdmin(),
        ];

        foreach (var client in elevated)
        {
            var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            body.ShouldContain(
                "Inactive",
                Case.Insensitive,
                $"the {listing} withheld a former EDJEr from an elevated viewer");
        }
    }

    [Fact]
    public async Task EveryElevatedRole_CanOpenAnInactiveEdjersDetail()
    {
        // The employee detail's half of the rule: the record itself is reachable for an elevated
        // viewer and absent for a baseline one (asserted below).
        await ResetDatabaseAsync();
        await SeedAsync();

        HttpClient[] elevated =
        [
            _factory.AsCompassAdmin(),
            _factory.AsCompassOps(),
            _factory.AsCompassSales(),
            _factory.AsCompassSuperAdmin(),
        ];

        foreach (var client in elevated)
        {
            var detail = await client.GetFromJsonAsync<EmployeeDetailDto>(
                "/api/compass/team-directory/2", TestContext.Current.CancellationToken);

            detail.ShouldNotBeNull();
            detail.LastName.ShouldBe("Inactive");
            detail.IsActive.ShouldBe(false);
        }
    }

    [Fact]
    public async Task BaselineViewer_RequestingAnInactiveEdjerDirectly_IsNotFound_NotForbidden()
    {
        // A 403 confirms the record exists. Compass employee ids are sequential, so that turns a
        // denial into an enumeration oracle — the whole reason FR-021 specifies not-found.
        await ResetDatabaseAsync();
        await SeedAsync();

        var response = await _factory.AsBaseEdjErOnly()
            .GetAsync("/api/compass/team-directory/2", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("inactive")]
    [InlineData("ACTIVE")]
    [InlineData("nonsense")]
    public async Task BaselineViewer_ForgingTheStatusFilter_StillReceivesNoInactiveEdjer(string status)
    {
        // FR-011a: the entitlement clause and the presentation filter are separate, and only the
        // filter is request-controlled. Collapsing them is invisible in the UI, because a baseline
        // viewer is never shown the control.
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await _factory.AsBaseEdjErOnly()
            .GetFromJsonAsync<List<TeamDirectoryRowDto>>(
                $"/api/compass/team-directory?status={status}",
                TestContext.Current.CancellationToken);

        rows.ShouldNotBeNull();
        rows.ShouldNotContain(r => r.LastName == "Inactive");
    }

    [Fact]
    public async Task ElevatedViewer_CanTellAnInactiveEdjerApartFromAnActiveOne()
    {
        // AC-15: showing both without distinguishing them is its own defect.
        await ResetDatabaseAsync();
        await SeedAsync();

        var rows = await _factory.AsCompassAdmin()
            .GetFromJsonAsync<List<TeamDirectoryRowDto>>(
                "/api/compass/team-directory?status=all", TestContext.Current.CancellationToken);

        rows.ShouldNotBeNull();
        rows.ShouldContain(r => r.IsActive == false);
        rows.ShouldContain(r => r.IsActive == true);
    }

    /// <summary>One active EDJEr, one inactive, both assigned to client 1.</summary>
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<EmployeeType>().Add(new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });
        context.Set<Client>().Add(new Client { Id = 1, ClientName = "Sweep Client" });
        context.Set<Employee>().AddRange(
            new Employee
            {
                Id = 1,
                FirstName = "Ada",
                LastName = "Active",
                Email = "ada.active@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = 1,
                HireDate = new DateOnly(2020, 1, 15),
                IsActive = true,
            },
            new Employee
            {
                Id = 2,
                FirstName = "Ivor",
                LastName = "Inactive",
                Email = "ivor.inactive@example.test",
                StateOfResidence = "CA",
                EmployeeTypeId = 1,
                HireDate = new DateOnly(2016, 9, 1),
                IsActive = false,
            });
        context.Set<ClientAssignment>().AddRange(
            new ClientAssignment { Id = 1, EmployeeId = 1, ClientId = 1, StartDate = new DateOnly(2022, 1, 1) },
            new ClientAssignment
            {
                Id = 2,
                EmployeeId = 2,
                ClientId = 1,
                StartDate = new DateOnly(2021, 1, 1),
                EndDate = new DateOnly(2022, 6, 30),
            });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
