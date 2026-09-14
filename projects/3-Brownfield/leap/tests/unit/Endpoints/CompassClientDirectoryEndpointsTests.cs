using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// The Client Directory — AC-12.
/// </summary>
/// <remarks>
/// <para>
/// The derived-status component's second consumer, which is what makes SC-005 observable: the
/// status a client shows here must equal the status the client view shows, because one implementation
/// produces both. A disagreement is the exact failure AC-42 predicts.
/// </para>
/// <para>
/// PRD v11 added the requirement that status is sortable and filterable, not merely displayed —
/// which is only possible because the value space is closed and total. Issue #274 made it
/// three-valued without weakening that: a client with zero assignments is <c>Inactive</c> and one
/// whose assignments have all ended is <c>Former</c>, and neither is ever blank.
/// </para>
/// </remarks>
public class CompassClientDirectoryEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Route = "/api/compass/client-directory";

    private readonly TestWebApplicationFactory _factory;

    public CompassClientDirectoryEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        Seed(factory);
    }

    [Fact]
    public async Task Get_AsAnyAuthenticatedUser_ReturnsOk()
    {
        // AC-12 opens "Given any authenticated EDJEr" — no Compass role required (FR-001).
        var response = await _factory.AsBaselineEdjer().GetAsync(Route, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_Unauthenticated_IsRejected()
    {
        var response = await _factory.AsAnonymous().GetAsync(Route, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_ReturnsEveryClient()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.Count.ShouldBe(4);
        rows.Select(r => r.ClientName).ShouldContain("Never Assigned");
    }

    // ------------------------------------------------------------------ BR-11 — the derived status

    [Fact]
    public async Task Get_ClientWithAnOpenEndedAssignment_IsActive()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.Single(r => r.ClientName == "Currently Engaged").Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Get_ClientWhoseAssignmentsHaveAllEnded_IsFormer()
    {
        // Issue #274: a client we worked with and stopped is FORMER, distinct from one we set up and
        // never engaged. The two used to read the same word — which is what the issue asked to fix.
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.Single(r => r.ClientName == "All Ended").Status.ShouldBe("Former");
    }

    [Fact]
    public async Task Get_ClientWithNoAssignmentsAtAll_IsInactive_NotBlank()
    {
        // The case PRD v11 added and the repo's v5 snapshot got wrong. A brand-new client is Inactive
        // from creation — and simultaneously fully selectable for assignment, because status gates
        // nothing.
        //
        // Since issue #274 this is the case that DEFINES Inactive rather than merely falling into it:
        // read together with Get_ClientWhoseAssignmentsHaveAllEnded_IsFormer, the pair is the split.
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.Single(r => r.ClientName == "Never Assigned").Status.ShouldBe("Inactive");
    }

    [Fact]
    public async Task Get_ClientWhoseLatestAssignmentEndsExactlyToday_IsActive()
    {
        // The boundary the derivation contract names as the one independent implementations diverge
        // on. Owner direction supersedes PRD v11's literal "inactive otherwise" here.
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.Single(r => r.ClientName == "Ends Today").Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Get_EveryClientResolvesToExactlyOneOfThreeValues()
    {
        // Closed and TOTAL — the property the sort depends on, and the one issue #274 preserved while
        // adding Former. No fourth state, no null, no blank.
        var rows = await GetRows(_factory.AsBaselineEdjer());

        rows.ShouldAllBe(r =>
            r.Status == "Active" || r.Status == "Inactive" || r.Status == "Former");
    }

    // ------------------------------------------------------------------ AC-12 — search and sort

    [Fact]
    public async Task Get_SearchesByPartialClientName_CaseInsensitively()
    {
        var lower = await GetRows(_factory.AsBaselineEdjer(), "?search=ended");
        var upper = await GetRows(_factory.AsBaselineEdjer(), "?search=ENDED");

        lower.ShouldContain(r => r.ClientName == "All Ended");
        upper.Select(r => r.ClientName).ShouldBe(lower.Select(r => r.ClientName));
    }

    [Fact]
    public async Task Get_SortsByName()
    {
        var rows = await GetRows(_factory.AsBaselineEdjer(), "?sort=clientName");

        rows.Select(r => r.ClientName)
            .ShouldBe(rows.Select(r => r.ClientName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Get_SortsByStatus()
    {
        // FR-030: status is sortable, which the binary rule is what makes possible.
        var rows = await GetRows(_factory.AsBaselineEdjer(), "?sort=status");

        rows.Select(r => r.Status)
            .ShouldBe(rows.Select(r => r.Status).OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Get_SortDirectionIsReversible()
    {
        var ascending = await GetRows(_factory.AsBaselineEdjer(), "?sort=clientName");
        var descending = await GetRows(_factory.AsBaselineEdjer(), "?sort=clientName&desc=true");

        descending.Select(r => r.ClientName).ShouldBe(ascending.Select(r => r.ClientName).Reverse());
    }

    [Fact]
    public async Task Get_NoMatches_ReturnsAnEmptyListNotAnError()
    {
        var response = await _factory.AsBaselineEdjer()
            .GetAsync($"{Route}?search=zzzznoclient", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Rows(response)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_DoesNotLeakClientDetailFieldsToABaselineViewer()
    {
        // Spec assumption 4: AC-14 hides MSA/NDA dates, the internal flag and the invoice default
        // from four of five tiers, so surfacing them in a directory open to every authenticated user
        // would defeat it. The directory is name + status + link, deliberately.
        var response = await _factory.AsBaselineEdjer().GetAsync(Route, TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        json.ShouldNotContain("msa", Case.Insensitive);
        json.ShouldNotContain("nda", Case.Insensitive);
        json.ShouldNotContain("invoiceFrequency", Case.Insensitive);
        json.ShouldNotContain("isInternal", Case.Insensitive);
    }

    [Theory]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassSuperAdminRole)]
    public async Task Get_ReturnsTheSameShapeForEveryTier(string role)
    {
        // Unlike the employee surfaces, the client DIRECTORY is not tier-scoped: AC-12 gives every
        // authenticated viewer the same three columns. The role-scoped panels are the client VIEW's
        // concern (AC-14).
        var baseline = await GetRows(_factory.AsBaselineEdjer());
        var elevated = await GetRows(_factory.AsRoles(role));

        elevated.Select(r => r.ClientName).ShouldBe(baseline.Select(r => r.ClientName));
        elevated.Select(r => r.Status).ShouldBe(baseline.Select(r => r.Status));
    }

    // ------------------------------------------------------------------ Helpers

    private static async Task<List<ClientDirectoryRowDto>> GetRows(HttpClient client, string query = "")
    {
        var response = await client.GetAsync(Route + query, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await Rows(response);
    }

    private static async Task<List<ClientDirectoryRowDto>> Rows(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<List<ClientDirectoryRowDto>>(
            TestContext.Current.CancellationToken) ?? [];

    private static void Seed(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (context.Set<Client>().Any())
        {
            return;
        }

        // Must be the SAME clock the endpoint derives status from — see the note in
        // CompassTeamDirectoryEndpointsTests. Seeding from UtcNow makes "All Ended" read Active for
        // the last four or five hours of every Eastern day.
        var today = new CompassBusinessDate(TimeProvider.System).Today();

        context.Set<EmployeeType>().Add(new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });
        context.Set<Employee>().Add(new Employee
        {
            Id = 1,
            FirstName = "Ada",
            LastName = "Active",
            Email = "ada.active@example.test",
            StateOfResidence = "OH",
            EmployeeTypeId = 1,
            HireDate = new DateOnly(2020, 1, 15),
            IsActive = true,
        });

        // One client per BR-11 case, plus MSA/NDA/internal values that must NOT reach the directory.
        context.Set<Client>().AddRange(
            new Client
            {
                Id = 1,
                ClientName = "Currently Engaged",
                MsaSignedDate = today.AddYears(-2),
                NdaSignedDate = today.AddYears(-2),
                IsInternal = false,
            },
            new Client { Id = 2, ClientName = "All Ended" },
            new Client { Id = 3, ClientName = "Never Assigned" },
            new Client { Id = 4, ClientName = "Ends Today" });

        context.Set<ClientAssignment>().AddRange(
            new ClientAssignment { Id = 1, EmployeeId = 1, ClientId = 1, StartDate = new DateOnly(2022, 1, 1), EndDate = null },
            new ClientAssignment { Id = 2, EmployeeId = 1, ClientId = 2, StartDate = new DateOnly(2022, 1, 1), EndDate = today.AddDays(-1) },
            new ClientAssignment { Id = 3, EmployeeId = 1, ClientId = 4, StartDate = new DateOnly(2022, 1, 1), EndDate = today.AddDays(-30) },
            new ClientAssignment { Id = 4, EmployeeId = 1, ClientId = 4, StartDate = new DateOnly(2023, 1, 1), EndDate = today });

        context.SaveChanges();
    }
}
