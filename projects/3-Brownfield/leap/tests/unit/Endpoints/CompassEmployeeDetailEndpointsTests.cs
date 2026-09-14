using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// The read-only employee detail — AC-8, AC-10, AC-11.
/// </summary>
/// <remarks>
/// <para>
/// This file carries the stream's two most disclosure-sensitive rules: Time Tracking Settings are
/// Super Admin only (AC-10 — the cell "elevated means sees everything" intuition gets wrong),
/// and a regular EDJEr sees SOWs on their own record and on no other (AC-11).
/// </para>
/// <para>
/// Assertions are on the response payload, and several inspect the raw JSON rather than a
/// deserialised object. That is deliberate: FR-005 requires withheld data to be ABSENT, and
/// deserialising into a DTO with nullable properties cannot tell "absent" from "present and null".
/// </para>
/// </remarks>
public class CompassEmployeeDetailEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    /// <summary>
    /// The email <see cref="TestAuthHandler"/> authenticates as.
    /// </summary>
    /// <remarks>
    /// The own-record fixture lives here rather than in <c>CompassDirectorySeeder</c>, deliberately.
    /// That seeder reaches the deployed dev environment through
    /// <c>StartupTasks:SeedCompassDirectory</c>, so putting a real <c>@leadingedje.com</c> address in
    /// it would place a fixture identity in a deployed database — and its gate test asserts an exact
    /// employee count that a new anchor would break by design. Seeding it here keeps the address in
    /// the test process, where it belongs.
    /// </remarks>
    private const string ViewerEmail = "test@leadingedje.com";

    private const int OwnRecordId = 1;
    private const int OtherRecordId = 2;
    private const int InactiveId = 3;

    /// <summary>
    /// A record carrying one assignment of each status shape (AC-20, issue #223).
    /// </summary>
    /// <remarks>
    /// Its own fixture rather than more assignments on <see cref="OtherRecordId"/>, because that
    /// record's history is pinned at two rows by
    /// <c>Get_AssignmentHistory_IncludesEndedAssignments_NotJustCurrentOnes</c> and addressed with a
    /// <c>Single(a =&gt; a.EndDate != null)</c>. Adding to it would have broken both for a reason that
    /// has nothing to do with what they assert.
    /// </remarks>
    private const int StatusCasesId = 4;

    /// <summary>
    /// A record whose three assignments are seeded OUT of start-date order (issue #450), so a passing
    /// assertion cannot be explained by insertion order coincidentally matching chronological order.
    /// </summary>
    private const int SortOrderId = 5;

    private readonly TestWebApplicationFactory _factory;

    public CompassEmployeeDetailEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        Seed(factory);
    }

    private static string Route(int id) => $"/api/compass/team-directory/{id}";

    // ------------------------------------------------------------------ AC-8 — the read-only detail

    [Fact]
    public async Task Get_AsAnyAuthenticatedUser_ReturnsTheProfileAndAssignmentHistory()
    {
        var detail = await GetDetail(_factory.AsBaselineEdjer(), OtherRecordId);

        detail.FirstName.ShouldBe("Otto");
        detail.LastName.ShouldBe("Other");
        detail.EmployeeType.ShouldBe("Full Time");
        detail.Coach.ShouldBe("Cody Coach");
        detail.State.ShouldBe("OH");
        detail.AssignmentHistory.ShouldNotBeEmpty();
    }

    // ------------------------------------------------------------------ issue #245 — coach drill-in

    [Fact]
    public async Task Get_Detail_CarriesTheCoachsOwnId_ForTheDrillIn()
    {
        // The Team Directory issue that started this (#245): the coach's NAME alone cannot be a link.
        // Otto's coach is Cody Coach (id 9) per the fixture below.
        var detail = await GetDetail(_factory.AsBaselineEdjer(), OtherRecordId);

        detail.CoachId.ShouldBe(9);
    }

    [Fact]
    public async Task Get_Detail_WithNoCoach_CoachIdIsNull()
    {
        // Cody Coach (id 9) is the top of this fixture's chain and has no coach of their own.
        var detail = await GetDetail(_factory.AsBaselineEdjer(), 9);

        detail.CoachId.ShouldBeNull();
        detail.Coach.ShouldBeNull();
    }

    [Fact]
    public async Task Get_Detail_ListsEveryEdjerWhoNamesThisOneAsTheirCoach()
    {
        // Cody Coach (id 9) coaches OwnRecordId, OtherRecordId and InactiveId in the fixture below.
        // A baseline viewer's set is all-active (BR-1), same rule as every other listing, so the
        // inactive coachee is excluded here and covered by the elevated-role test below instead.
        var detail = await GetDetail(_factory.AsBaselineEdjer(), 9);

        detail.DirectReports.Select(r => r.Id).ShouldBe([OwnRecordId, OtherRecordId], ignoreOrder: true);
    }

    [Theory]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    [InlineData(RolePolicy.CompassSuperAdminRole)]
    public async Task Get_AsAnyElevatedRole_DirectReportsIncludeInactiveCoachees(string role)
    {
        var detail = await GetDetail(_factory.AsRoles(role), 9);

        detail.DirectReports.Select(r => r.Id).ShouldContain(InactiveId);
    }

    [Theory]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    [InlineData(RolePolicy.CompassSuperAdminRole)]
    public async Task Get_AsAnyElevatedRole_DirectReportsCarryWhetherEachIsActive(string role)
    {
        // Issue #399: an elevated viewer keeps seeing former direct reports (issue #245), but must be
        // able to tell them apart from current ones without cross-referencing another screen.
        var detail = await GetDetail(_factory.AsRoles(role), 9);

        detail.DirectReports.Single(r => r.Id == InactiveId).IsActive.ShouldBe(false);
        detail.DirectReports.Single(r => r.Id == OwnRecordId).IsActive.ShouldBe(true);
        detail.DirectReports.Single(r => r.Id == OtherRecordId).IsActive.ShouldBe(true);
    }

    [Fact]
    public async Task Get_AsBaseline_DirectReportsOmitTheIsActiveField()
    {
        // A baseline viewer's set is all-active (BR-1) already, so the field carries no information
        // for them — omitted entirely (FR-005), matching the top-level IsActive disclosure rule.
        var json = await GetRawJson(_factory.AsBaselineEdjer(), 9);

        json.ShouldNotContain("isActive", Case.Insensitive);
    }

    [Fact]
    public async Task Get_Detail_ForAnEdjerWithNoCoachees_DirectReportsIsEmptyNotNull()
    {
        // Otto (OtherRecordId) coaches no one — a data absence, not a disclosure withholding, so this
        // is an empty list rather than an omitted field (matching AssignmentHistory's own shape).
        var detail = await GetDetail(_factory.AsBaselineEdjer(), OtherRecordId);

        detail.DirectReports.ShouldNotBeNull();
        detail.DirectReports.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_AssignmentHistory_CarriesTheClientLinkAndBothDates()
    {
        // AC-8: "a client in the assignment history links to that client's view". The id is the link.
        var detail = await GetDetail(_factory.AsBaselineEdjer(), OtherRecordId);

        var ended = detail.AssignmentHistory.Single(a => a.EndDate != null);
        ended.ClientId.ShouldBe(2);
        ended.ClientName.ShouldBe("Client Two");
        ended.StartDate.ShouldBe(new DateOnly(2022, 1, 1));
    }

    [Fact]
    public async Task Get_AssignmentHistory_CarriesTheAssignmentsOwnId()
    {
        // AC-2 / mockup screen 5: a history row must link into that assignment's own detail screen
        // (/compass/team-directory/{employeeId}/assignments/{assignmentId}), not just to the client.
        var detail = await GetDetail(_factory.AsBaselineEdjer(), OtherRecordId);

        var ended = detail.AssignmentHistory.Single(a => a.EndDate != null);
        ended.AssignmentId.ShouldBe(3);
    }

    [Fact]
    public async Task Get_AssignmentHistory_IncludesEndedAssignments_NotJustCurrentOnes()
    {
        // History, not a snapshot. The Team Directory shows CURRENT assignments; this shows all of
        // them, which is what makes it a history.
        var detail = await GetDetail(_factory.AsBaselineEdjer(), OtherRecordId);

        detail.AssignmentHistory.Count.ShouldBe(2);
    }

    // ------------------------------------------------------------------ issue #450 — newest first

    /// <summary>
    /// The EDJEr assignment-history panel lists the newest assignment first (issue #450) — the
    /// screen this panel backs (Team Directory's EDJEr detail AND the Admin/EDJEr screen, both fed by
    /// this one endpoint) is a history a reader scans top-down, and the most recent engagement is what
    /// they are almost always looking for.
    /// </summary>
    [Fact]
    public async Task Get_AssignmentHistory_IsOrderedNewestStartDateFirst()
    {
        var detail = await GetDetail(_factory.AsBaselineEdjer(), SortOrderId);

        detail.AssignmentHistory.Select(a => a.StartDate).ShouldBe(
            [new DateOnly(2022, 6, 15), new DateOnly(2021, 3, 10), new DateOnly(2020, 1, 1)]);
    }

    [Fact]
    public async Task Get_Unauthenticated_IsRejected()
    {
        var response = await _factory.AsAnonymous().GetAsync(Route(OtherRecordId), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_UnknownId_IsNotFound()
    {
        var response = await _factory.AsBaselineEdjer().GetAsync(Route(9999), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------ AC-9 — visibility

    [Fact]
    public async Task Get_AsBaseline_AnInactiveEdjerIsNotFound_NotForbidden()
    {
        // FR-021 and the contract's §3 corollary. A 403 would CONFIRM the record exists, and Compass
        // employee ids are sequential — so for a baseline viewer an inactive EDJEr must be
        // indistinguishable from one that was never there.
        var response = await _factory.AsBaselineEdjer().GetAsync(Route(InactiveId), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    [InlineData(RolePolicy.CompassSuperAdminRole)]
    public async Task Get_AsAnyElevatedRole_AnInactiveEdjerIsReadable(string role)
    {
        var detail = await GetDetail(_factory.AsRoles(role), InactiveId);

        detail.LastName.ShouldBe("Inactive");
        detail.IsActive.ShouldBe(false);
    }

    // ------------------------------------------------------------------ AC-10 — Time Tracking

    [Fact]
    public async Task Get_AsSuperAdmin_IncludesTimeTrackingSettings()
    {
        var detail = await GetDetail(_factory.AsRoles(RolePolicy.CompassSuperAdminRole), OtherRecordId);

        detail.TimeTrackingSettings.ShouldNotBeNull();
        detail.TimeTrackingSettings.TimesheetRequired.ShouldBeTrue();
        detail.TimeTrackingSettings.CanSubmitUnder40.ShouldBeFalse();
        detail.TimeTrackingSettings.IncludeInPayroll.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    public async Task Get_AsAnyoneBelowSuperAdmin_OmitsTimeTrackingSettings(string? role)
    {
        // AC-10 is Super-Admin-ONLY. Compass Admin, Ops and Sales are elevated for every other row of
        // the BR-1 matrix and not for this one — the cell most likely to be got wrong.
        var json = await GetRawJson(role is null ? _factory.AsBaselineEdjer() : _factory.AsRoles(role), OtherRecordId);

        json.ShouldNotContain("timeTrackingSettings", Case.Insensitive);
        json.ShouldNotContain("timesheetRequired", Case.Insensitive);
    }

    // ------------------------------------------------------------------ AC-11 — SOWs and notes

    [Fact]
    public async Task Get_AsBaseline_AnotherEdjersSowsAndNotesAreAbsentFromTheResponse()
    {
        // Not merely unrendered — ABSENT (FR-005). Asserted against raw JSON because a nullable DTO
        // property cannot distinguish "absent" from "present and null".
        var json = await GetRawJson(_factory.AsBaselineEdjer(), OtherRecordId);

        json.ShouldNotContain("rateIncrease", Case.Insensitive);
        json.ShouldNotContain("Confidential SOW note", Case.Insensitive);
        json.ShouldNotContain("Confidential assignment note", Case.Insensitive);
        json.ShouldNotContain("sows", Case.Insensitive);
    }

    [Fact]
    public async Task Get_AsBaseline_OwnRecordIncludesTheirSows()
    {
        // The positive branch of AC-11, and the one a fail-closed bug leaves indistinguishable from
        // correct behaviour. It is only reachable because ViewerEmail is seeded.
        var detail = await GetDetail(_factory.AsBaselineEdjer(), OwnRecordId);

        detail.AssignmentHistory.SelectMany(a => a.Sows ?? []).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Get_AsBaseline_OwnRecordStillOmitsNotesAndTheRateIncreaseIndicator()
    {
        // The asymmetry AC-11 draws: a regular EDJEr gets SOWs on their own record, NOT notes and not
        // the rate-increase indicator — even there.
        var json = await GetRawJson(_factory.AsBaselineEdjer(), OwnRecordId);

        json.ShouldNotContain("rateIncrease", Case.Insensitive);
        json.ShouldNotContain("Confidential SOW note", Case.Insensitive);
        json.ShouldNotContain("Confidential assignment note", Case.Insensitive);
    }

    [Fact]
    public async Task Get_AsBaseline_WhoseEmailMatchesNoRecord_SeesNoSowsAnywhere()
    {
        // Fail closed (FR-018a). The DevBypass identity is exactly this shape against a directory of
        // @example.test addresses.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.NoPrivilegesHeader, "true");
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailOverrideHeader, "nobody@leadingedje.com");

        var response = await client.GetAsync(Route(OwnRecordId), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        json.ShouldNotContain("sows", Case.Insensitive);
    }

    [Theory]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    [InlineData(RolePolicy.CompassSuperAdminRole)]
    public async Task Get_AsAnyElevatedRole_SeesAllSowsNotesAndTheRateIncreaseIndicator(string role)
    {
        var detail = await GetDetail(_factory.AsRoles(role), OtherRecordId);

        var assignment = detail.AssignmentHistory.First(a => a.Sows is { Count: > 0 });
        assignment.Note.ShouldBe("Confidential assignment note");
        assignment.Sows.ShouldNotBeNull();
        assignment.Sows.ShouldContain(s => s.RateIncrease == true);
        assignment.Sows.ShouldContain(s => s.Note == "Confidential SOW note");
    }

    [Fact]
    public async Task Get_AsCompassAdmin_SeesElevatedSectionsDespiteBeingReadOnly()
    {
        // AC-44 makes Compass Admin read-only; BR-1 makes its VISIBILITY fully elevated. Capability
        // and visibility are independent axes.
        var detail = await GetDetail(_factory.AsRoles(RolePolicy.CompassAdminRole), OtherRecordId);

        detail.AssignmentHistory.ShouldContain(a => a.Note != null);
        detail.TimeTrackingSettings.ShouldBeNull("Compass Admin is elevated, but AC-10 is Super-Admin-only");
    }

    // ------------------------------------------------------------------ AC-16/FR-025 — View-assignment

    [Theory]
    [InlineData(RolePolicy.CompassAdminRole)]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    [InlineData(RolePolicy.CompassSuperAdminRole)]
    public async Task Get_AsAnyElevatedRole_GrantsTheViewAssignmentAffordance(string role)
    {
        var detail = await GetDetail(_factory.AsRoles(role), OtherRecordId);

        detail.AssignmentHistory.ShouldAllBe(a => a.CanViewAssignment == true);
    }

    [Fact]
    public async Task Get_AsBaseline_WithholdsTheViewAssignmentAffordance()
    {
        // Modelled as an entitlement the SERVER grants, not a UI conditional — an affordance the
        // server did not grant must not be renderable, matching CanViewSow on the client view.
        var json = await GetRawJson(_factory.AsBaselineEdjer(), OtherRecordId);

        json.ShouldNotContain("canViewAssignment", Case.Insensitive);
    }

    // ------------------------------------------------------------------ Helpers

    private static async Task<EmployeeDetailDto> GetDetail(HttpClient client, int id)
    {
        var response = await client.GetAsync(Route(id), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<string> GetRawJson(HttpClient client, int id)
    {
        var response = await client.GetAsync(Route(id), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }


    // --------------------------------------------- AC-20 / #223 — a status per assignment row

    /// <summary>
    /// Each assignment row reports its own status, derived from BR-7's "current" predicate.
    /// </summary>
    /// <remarks>
    /// The EDJEr assignment-history panel (AC-20) shows every client the EDJEr has been assigned to,
    /// and a start/end pair alone does not answer "is this one live?" without the reader doing date
    /// arithmetic in their head — which is exactly the comparison BR-11's single-implementation rule
    /// exists to keep out of consumers.
    /// </remarks>
    [Fact]
    public async Task Get_AssignmentHistory_ReportsAStatusPerRow()
    {
        var detail = await GetDetail(_factory.AsBaselineEdjer(), StatusCasesId);

        var openEnded = detail.AssignmentHistory.Single(a => a.EndDate == null);
        openEnded.Status.ShouldBe("Active", "an assignment with no end date is current (BR-7)");
    }

    [Fact]
    public async Task Get_AssignmentHistory_TreatsAFutureEndDateAsStillCurrent()
    {
        // BR-7's comparison is INCLUSIVE and forward-looking: an end date that has not arrived yet
        // does not end anything. Anchored relative to the business date in the fixture, never to a
        // literal, so this cannot start passing for the wrong reason next month (SC-005).
        var detail = await GetDetail(_factory.AsBaselineEdjer(), StatusCasesId);

        var future = detail.AssignmentHistory.Single(a => a.EndDate > new DateOnly(2024, 1, 1));
        future.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Get_AssignmentHistory_ReportsALongEndedAssignmentAsInactive()
    {
        var detail = await GetDetail(_factory.AsBaselineEdjer(), StatusCasesId);

        var ended = detail.AssignmentHistory.Single(a => a.EndDate == new DateOnly(2023, 6, 30));
        ended.Status.ShouldBe("Inactive");
    }

    [Fact]
    public async Task Get_AssignmentHistory_ReportsEveryRowWithNoThirdState()
    {
        // Totality, on the same grounds FR-031 demands it of client status: an empty string or a null
        // here would be a third state the panel has no way to render, and it is what a mis-joined
        // status lookup would produce rather than an exception.
        var detail = await GetDetail(_factory.AsBaselineEdjer(), StatusCasesId);

        detail.AssignmentHistory.Count.ShouldBe(3);
        detail
            .AssignmentHistory.Select(a => a.Status)
            .Distinct()
            .ShouldBeSubsetOf(["Active", "Inactive"]);
        detail.AssignmentHistory.ShouldAllBe(a => a.Status.Length > 0);
    }

    /// <summary>
    /// The status is visible to a BASELINE viewer, unlike notes and other people's SOWs.
    /// </summary>
    /// <remarks>
    /// Worth pinning rather than assuming: this file's other rules are all about withholding, so the
    /// safe-looking instinct is to gate a new field too. Status is not sensitive — the start and end
    /// dates it summarises are already on the row for every viewer (AC-8), so withholding only the
    /// summary would make the panel harder to read while disclosing exactly as much.
    /// </remarks>
    [Fact]
    public async Task Get_AssignmentStatus_IsVisibleToABaselineViewer_UnlikeNotes()
    {
        var detail = await GetDetail(_factory.AsBaselineEdjer(), StatusCasesId);

        detail.AssignmentHistory.ShouldAllBe(a => a.Status.Length > 0);
        detail.AssignmentHistory.ShouldAllBe(a => a.Note == null);
    }

    private static void Seed(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (context.Set<Employee>().Any())
        {
            return;
        }

        context.Set<EmployeeType>().Add(new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });
        context.Set<Client>().AddRange(
            new Client { Id = 1, ClientName = "Client One" },
            new Client { Id = 2, ClientName = "Client Two" });

        context.Set<Employee>().AddRange(
            new Employee
            {
                Id = 9,
                FirstName = "Cody",
                LastName = "Coach",
                Email = "cody.coach@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = 1,
                HireDate = new DateOnly(2015, 3, 1),
                IsActive = true,
            },
            // The own-record fixture: its email is the one the test identity authenticates as.
            new Employee
            {
                Id = OwnRecordId,
                FirstName = "Test",
                LastName = "User",
                Email = ViewerEmail,
                StateOfResidence = "OH",
                EmployeeTypeId = 1,
                CoachEmployeeId = 9,
                HireDate = new DateOnly(2020, 1, 15),
                IsActive = true,
            },
            new Employee
            {
                Id = OtherRecordId,
                FirstName = "Otto",
                LastName = "Other",
                Email = "otto.other@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = 1,
                CoachEmployeeId = 9,
                HireDate = new DateOnly(2021, 2, 1),
                IsActive = true,
                TimesheetRequired = true,
                CanSubmitUnder40 = false,
                IncludeInPayroll = true,
            },
            new Employee
            {
                Id = StatusCasesId,
                FirstName = "Fiona",
                LastName = "Future",
                Email = "fiona.future@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = 1,
                HireDate = new DateOnly(2021, 1, 1),
                IsActive = true,
            },
            new Employee
            {
                Id = InactiveId,
                FirstName = "Ivor",
                LastName = "Inactive",
                Email = "ivor.inactive@example.test",
                StateOfResidence = "CA",
                EmployeeTypeId = 1,
                CoachEmployeeId = 9,
                HireDate = new DateOnly(2016, 9, 1),
                IsActive = false,
            },
            new Employee
            {
                Id = SortOrderId,
                FirstName = "Sasha",
                LastName = "Sorted",
                Email = "sasha.sorted@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = 1,
                HireDate = new DateOnly(2019, 1, 1),
                IsActive = true,
            });

        // The business date the ROUTES judge status against, never DateTime.Today: a fixture anchored
        // to the machine clock disagrees with the application either side of the Eastern/UTC boundary,
        // and CompassBusinessDateTests exists because that shipped once already.
        var today = scope.ServiceProvider.GetRequiredService<ICompassBusinessDate>().Today();

        context.Set<ClientAssignment>().AddRange(
            new ClientAssignment
            {
                Id = 1,
                EmployeeId = OwnRecordId,
                ClientId = 1,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = null,
                Note = "Confidential assignment note",
            },
            new ClientAssignment
            {
                Id = 2,
                EmployeeId = OtherRecordId,
                ClientId = 1,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = null,
                Note = "Confidential assignment note",
            },
            new ClientAssignment
            {
                Id = 3,
                EmployeeId = OtherRecordId,
                ClientId = 2,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = new DateOnly(2023, 6, 30),
            },

            // The three status shapes BR-7 distinguishes. The two live ones are RELATIVE to the
            // business date; only the long-past one is a literal, because "ended in 2023" cannot rot.
            new ClientAssignment
            {
                Id = 4,
                EmployeeId = StatusCasesId,
                ClientId = 1,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = null,
            },
            new ClientAssignment
            {
                Id = 5,
                EmployeeId = StatusCasesId,
                ClientId = 2,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = today.AddDays(30),
            },
            new ClientAssignment
            {
                Id = 6,
                EmployeeId = StatusCasesId,
                ClientId = 2,
                StartDate = new DateOnly(2021, 1, 1),
                EndDate = new DateOnly(2023, 6, 30),
            },

            // Issue #450: seeded deliberately OUT of start-date order — id 7 is the oldest and id 9
            // the newest — so a passing sort assertion cannot be explained by insertion order alone.
            new ClientAssignment
            {
                Id = 7,
                EmployeeId = SortOrderId,
                ClientId = 1,
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2020, 12, 31),
            },
            new ClientAssignment
            {
                Id = 8,
                EmployeeId = SortOrderId,
                ClientId = 2,
                StartDate = new DateOnly(2022, 6, 15),
                EndDate = null,
            },
            new ClientAssignment
            {
                Id = 9,
                EmployeeId = SortOrderId,
                ClientId = 1,
                StartDate = new DateOnly(2021, 3, 10),
                EndDate = new DateOnly(2022, 1, 1),
            });

        context.Set<Sow>().AddRange(
            new Sow
            {
                Id = 1,
                ClientAssignmentId = 1,
                SowType = SowType.InitialContract,
                SowStartDate = new DateOnly(2022, 1, 1),
                SowEndDate = new DateOnly(2023, 1, 1),
                RateIncrease = false,
                Note = "Confidential SOW note",
            },
            new Sow
            {
                Id = 2,
                ClientAssignmentId = 2,
                SowType = SowType.SowExtension,
                SowStartDate = new DateOnly(2023, 1, 2),
                SowEndDate = new DateOnly(2024, 1, 1),
                RateIncrease = true,
                Note = "Confidential SOW note",
            });

        context.SaveChanges();
    }
}
