using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// The client view — AC-13, AC-14, AC-15, AC-16.
/// </summary>
/// <remarks>
/// <para>
/// AC-13 exists because of AC-14. AC-14 hides the Client Details panel from four of the five
/// tiers, and that panel is where a client's name would naturally live — so without AC-13 those four
/// tiers would get a nameless page. The criterion says so verbatim: "including when the Client
/// Details panel is hidden for the viewer's role". The two are tested together for that reason.
/// </para>
/// </remarks>
public class CompassClientViewEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private const int EngagedClientId = 1;
    private const int NeverAssignedClientId = 2;
    private const int InternalClientId = 3;

    /// <summary>
    /// A client whose three assignments are seeded OUT of start-date order (issue #456), so a passing
    /// sort assertion cannot be explained by insertion order coincidentally matching chronological order.
    /// </summary>
    private const int SortOrderClientId = 4;

    private readonly TestWebApplicationFactory _factory;

    public CompassClientViewEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        Seed(factory);
    }

    private static string Route(int id) => $"/api/compass/client-directory/{id}";

    // ------------------------------------------------------------------ AC-13 — name prominence

    [Theory]
    [InlineData(null)]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    [InlineData(RolePolicy.CompassSuperAdminRole)]
    public async Task Get_ReturnsTheClientName_ForEveryTier(string? role)
    {
        // All five, including the four for whom Client Details is hidden. This is the assertion
        // AC-13 was written to force.
        var view = await GetView(role is null ? _factory.AsBaselineEdjer() : _factory.AsRoles(role), EngagedClientId);

        view.ClientName.ShouldBe("Currently Engaged");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    [InlineData(RolePolicy.CompassSuperAdminRole)]
    public async Task Get_ReturnsIsInternal_ForEveryTier(string? role)
    {
        // Issue #243: unlike the rest of Client Details, IsInternal is served to every tier — the
        // assignment history table needs it, at every tier, to decide whether to render the SOW
        // column at all.
        var client = role is null ? _factory.AsBaselineEdjer() : _factory.AsRoles(role);

        (await GetView(client, EngagedClientId)).IsInternal.ShouldBeFalse();
        (await GetView(client, InternalClientId)).IsInternal.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ AC-14 — panel scoping

    [Fact]
    public async Task Get_AsSuperAdmin_IncludesTheClientDetailsPanel()
    {
        var view = await GetView(_factory.AsRoles(RolePolicy.CompassSuperAdminRole), EngagedClientId);

        view.ClientDetails.ShouldNotBeNull();
        view.ClientDetails.MsaSignedDate.ShouldBe(new DateOnly(2023, 1, 1));
        view.ClientDetails.NdaSignedDate.ShouldBe(new DateOnly(2023, 2, 1));
        view.ClientDetails.IsInternal.ShouldBeFalse();
        view.ClientDetails.InvoiceFrequency.ShouldBe("Monthly");
        view.ClientDetails.BillableTimeCategories.ShouldContain("Development");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    public async Task Get_AsAnyoneBelowSuperAdmin_OmitsTheClientDetailsPanelEntirely(string? role)
    {
        // Absent from the payload, not merely unrendered (FR-005). Asserted on raw JSON because a
        // nullable property cannot distinguish "absent" from "present and null".
        var json = await GetRawJson(role is null ? _factory.AsBaselineEdjer() : _factory.AsRoles(role), EngagedClientId);

        json.ShouldNotContain("clientDetails", Case.Insensitive);
        json.ShouldNotContain("msaSignedDate", Case.Insensitive);
        json.ShouldNotContain("billableTimeCategories", Case.Insensitive);
    }

    // ------------------------------------------------------------------ AC-15 — assignment history

    [Fact]
    public async Task Get_AssignmentHistory_ShowsStartEndAndEdjerStatusPerRow()
    {
        var view = await GetView(_factory.AsRoles(RolePolicy.CompassAdminRole), EngagedClientId);

        var row = view.AssignmentHistory.Single(a => a.EmployeeName == "Ada Active");
        row.StartDate.ShouldBe(new DateOnly(2022, 1, 1));
        row.EndDate.ShouldBeNull();
        row.EmployeeIsActive.ShouldBe(true);
        row.EmployeeId.ShouldBe(1);
    }

    [Fact]
    public async Task Get_AssignmentHistory_CarriesTheAssignmentsOwnId()
    {
        // AC-2 / mockup screen 5: a history row must link into that assignment's own detail screen
        // (/compass/client-directory/{clientId}/assignments/{assignmentId}), not just to the EDJEr.
        var view = await GetView(_factory.AsRoles(RolePolicy.CompassAdminRole), EngagedClientId);

        var row = view.AssignmentHistory.Single(a => a.EmployeeName == "Ada Active");
        row.AssignmentId.ShouldBe(1);
    }

    [Fact]
    public async Task Get_AsBaseline_AssignmentHistoryExcludesInactiveEdjers()
    {
        // AC-15 and BR-1: the visibility rule holds on THIS listing too, not only the directory.
        var view = await GetView(_factory.AsBaselineEdjer(), EngagedClientId);

        view.AssignmentHistory.ShouldNotContain(a => a.EmployeeName == "Ivor Inactive");
        view.AssignmentHistory.ShouldContain(a => a.EmployeeName == "Ada Active");
    }

    [Theory]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    [InlineData(RolePolicy.CompassSuperAdminRole)]
    public async Task Get_AsAnyElevatedRole_AssignmentHistoryIncludesInactiveEdjers(string role)
    {
        var view = await GetView(_factory.AsRoles(role), EngagedClientId);

        view.AssignmentHistory.ShouldContain(a => a.EmployeeName == "Ivor Inactive");
    }

    // ------------------------------------------------------------------ AC-16 — View-SOW

    [Theory]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    [InlineData(RolePolicy.CompassSuperAdminRole)]
    public async Task Get_AsAnyElevatedRole_GrantsTheViewSowAffordance(string role)
    {
        var view = await GetView(_factory.AsRoles(role), EngagedClientId);

        view.AssignmentHistory.ShouldAllBe(a => a.CanViewSow == true);
    }

    [Fact]
    public async Task Get_AsBaseline_WithholdsTheViewSowAffordance()
    {
        // Modelled as an entitlement the SERVER grants, not a UI conditional — an affordance the
        // server did not grant must not be renderable.
        var json = await GetRawJson(_factory.AsBaselineEdjer(), EngagedClientId);

        json.ShouldNotContain("canViewSow", Case.Insensitive);
    }

    [Theory]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    [InlineData(RolePolicy.CompassSuperAdminRole)]
    public async Task Get_AsAnyElevatedRole_WithholdsTheViewSowAffordance_ForAnInternalClient(string role)
    {
        // Issue #243: an internal (EDJE-to-EDJE) client has no contracts, so no tier gets the
        // View-SOW affordance for its assignments — not even the elevated tiers AC-16 otherwise
        // grants it to.
        var json = await GetRawJson(_factory.AsRoles(role), InternalClientId);

        json.ShouldNotContain("canViewSow", Case.Insensitive);
    }

    // ------------------------------------------------------------------ issue #456 — newest first

    /// <summary>
    /// The client assignment-history panel lists the newest assignment first (issue #456) — the screens
    /// this panel backs (Client Details AND the Admin/Edit Client screen, both fed by this one endpoint)
    /// are a history a reader scans top-down, and the most recent engagement is what they are almost
    /// always looking for. The EDJEr-side twin of this rule shipped as issue #450.
    /// </summary>
    [Fact]
    public async Task Get_AssignmentHistory_IsOrderedNewestStartDateFirst()
    {
        var view = await GetView(_factory.AsRoles(RolePolicy.CompassAdminRole), SortOrderClientId);

        view.AssignmentHistory.Select(a => a.StartDate).ShouldBe(
            [new DateOnly(2022, 6, 15), new DateOnly(2021, 3, 10), new DateOnly(2020, 1, 1)]);
    }

    // ------------------------------------------------------------------ Edges

    [Fact]
    public async Task Get_ClientWithNoAssignments_ReturnsAnEmptyHistoryNotAnError()
    {
        var view = await GetView(_factory.AsBaselineEdjer(), NeverAssignedClientId);

        view.ClientName.ShouldBe("Never Assigned");
        view.AssignmentHistory.ShouldBeEmpty();
        view.Status.ShouldBe("Inactive");
    }

    [Fact]
    public async Task Get_StatusAgreesWithTheClientDirectory()
    {
        // SC-005: one derivation, so the two surfaces cannot disagree. This is the assertion that
        // makes the shared component's value observable rather than asserted.
        var directory = await _factory.AsBaselineEdjer()
            .GetFromJsonAsync<List<ClientDirectoryRowDto>>(
                "/api/compass/client-directory", TestContext.Current.CancellationToken);
        var view = await GetView(_factory.AsBaselineEdjer(), EngagedClientId);

        directory!.Single(r => r.Id == EngagedClientId).Status.ShouldBe(view.Status);
    }

    [Fact]
    public async Task Get_UnknownId_IsNotFound()
    {
        var response = await _factory.AsBaselineEdjer().GetAsync(Route(9999), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_Unauthenticated_IsRejected()
    {
        var response = await _factory.AsAnonymous().GetAsync(Route(EngagedClientId), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ Helpers

    private static async Task<ClientViewDto> GetView(HttpClient client, int id)
    {
        var response = await client.GetAsync(Route(id), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ClientViewDto>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<string> GetRawJson(HttpClient client, int id)
    {
        var response = await client.GetAsync(Route(id), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static void Seed(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (context.Set<Client>().Any())
        {
            return;
        }

        context.Set<EmployeeType>().Add(new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });
        context.Set<InvoiceFrequencyType>().Add(
            new InvoiceFrequencyType { Id = 1, TypeName = "Monthly", IsActive = true });

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

        context.Set<Client>().AddRange(
            new Client
            {
                Id = EngagedClientId,
                ClientName = "Currently Engaged",
                MsaSignedDate = new DateOnly(2023, 1, 1),
                NdaSignedDate = new DateOnly(2023, 2, 1),
                IsInternal = false,
                InvoiceFrequencyTypeId = 1,
            },
            new Client { Id = NeverAssignedClientId, ClientName = "Never Assigned" },
            new Client { Id = InternalClientId, ClientName = "Leading EDJE (Internal)", IsInternal = true },
            new Client { Id = SortOrderClientId, ClientName = "Sort Order Client" });

        context.Set<BillableTimeCategory>().Add(new BillableTimeCategory
        {
            Id = 1,
            ClientId = EngagedClientId,
            CategoryName = "Development",
            IsActive = true,
        });

        context.Set<ClientAssignment>().AddRange(
            new ClientAssignment
            {
                Id = 1,
                EmployeeId = 1,
                ClientId = EngagedClientId,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = null,
            },
            new ClientAssignment
            {
                Id = 2,
                EmployeeId = 2,
                ClientId = EngagedClientId,
                StartDate = new DateOnly(2021, 1, 1),
                EndDate = new DateOnly(2022, 6, 30),
            },
            new ClientAssignment
            {
                Id = 3,
                EmployeeId = 1,
                ClientId = InternalClientId,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = null,
            },

            // Issue #456: seeded deliberately OUT of start-date order — id 4 is the oldest and id 6 the
            // newest — so a passing sort assertion cannot be explained by insertion order alone.
            new ClientAssignment
            {
                Id = 4,
                EmployeeId = 1,
                ClientId = SortOrderClientId,
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2020, 12, 31),
            },
            new ClientAssignment
            {
                Id = 5,
                EmployeeId = 1,
                ClientId = SortOrderClientId,
                StartDate = new DateOnly(2022, 6, 15),
                EndDate = null,
            },
            new ClientAssignment
            {
                Id = 6,
                EmployeeId = 1,
                ClientId = SortOrderClientId,
                StartDate = new DateOnly(2021, 3, 10),
                EndDate = new DateOnly(2022, 1, 1),
            });

        context.SaveChanges();
    }
}
