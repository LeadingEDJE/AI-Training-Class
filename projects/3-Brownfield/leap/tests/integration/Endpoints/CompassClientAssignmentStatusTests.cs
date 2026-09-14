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
/// AC-24's per-assignment status on the CLIENT record, against real PostgreSQL (issue #224).
/// </summary>
/// <remarks>
/// <para>
/// The client-side twin of <c>CompassEmployeeAssignmentStatusTests</c> (issue #223), and it exists
/// for the same reason: the status predicate runs in the DATABASE. Unit-level assertions run on the EF
/// Core InMemory provider, which translates nothing — it evaluates the same expression tree in .NET, so
/// a shape Npgsql cannot render passes there and answers 500 here. That is not hypothetical in
/// this module: <c>CompassReadRepository</c> carries a comment recording the ownership predicate failing
/// exactly that way, and feature 004 US2 shipped a projection only real SQL rejected.
/// </para>
/// <para>
/// The lookup joins on a DIFFERENT key from the sibling's — <c>ClientId</c>, not
/// <c>EmployeeId</c> — which is precisely the copy-paste error a test written from the sibling has to be
/// able to catch. So every case below seeds more than one client and asserts the other client's
/// assignments do not leak into this one's status set.
/// </para>
/// <para>
/// Every live fixture date is RELATIVE to the business date, resolved through
/// <see cref="ICompassBusinessDate"/> rather than <c>DateTime.Today</c> — the application judges
/// "current" in <c>America/New_York</c>, and a fixture anchored to the machine clock disagrees with it
/// every evening (<c>CompassBusinessDateTests</c>).
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassClientAssignmentStatusTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassClientAssignmentStatusTests(IntegrationTestFactory factory)
        : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const int ClientId = 1;
    private const int OtherClientId = 2;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Route(int id) => $"/api/compass/client-directory/{id}";

    [Fact]
    public async Task AssignmentHistory_DerivesEachRowsStatus_FromRealSql()
    {
        // Arrange — the three shapes BR-7 distinguishes, all on ONE client so a single response carries
        // all three and a mis-joined lookup cannot hide behind a one-row fixture.
        await ResetDatabaseAsync();
        var today = await SeedAsync();

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(ClientId), Token);

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "the IsCurrent predicate must translate to SQL, not fall back to client evaluation"
        );

        var view = await response.Content.ReadFromJsonAsync<ClientViewDto>(Token);
        view.ShouldNotBeNull();
        view.AssignmentHistory.Count.ShouldBe(3);

        StatusOf(view, 1).ShouldBe("Active", "no end date means current");
        StatusOf(view, 2).ShouldBe("Active", $"an end date of {today.AddDays(30):O} has not arrived");
        StatusOf(view, 3).ShouldBe("Inactive", "this one ended in 2023");
    }

    /// <summary>
    /// The boundary the whole component exists for: an assignment ending TODAY is still current.
    /// </summary>
    /// <remarks>
    /// AC-42 names this as the case independent implementations get wrong, and it is the one a fixture
    /// pinned to a literal date can never test twice. Asserted against real SQL because the comparison
    /// is <c>&gt;=</c> in the database, and an off-by-one there is invisible in .NET.
    /// </remarks>
    [Fact]
    public async Task AnAssignmentEndingExactlyToday_IsStillCurrent()
    {
        // Arrange
        await ResetDatabaseAsync();
        var today = await SeedAsync();
        await AddAssignmentAsync(id: 4, clientId: ClientId, employeeId: 1, endDate: today);

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(ClientId), Token);

        // Assert
        var view = await response.Content.ReadFromJsonAsync<ClientViewDto>(Token);
        view.ShouldNotBeNull();

        StatusOf(view, 4)
            .ShouldBe(
                "Active",
                "an assignment is current for the WHOLE of its end date — the owner decision the "
                    + "contract records, and the case AC-42 says diverging implementations differ on"
            );
    }

    /// <summary>
    /// The status set must be scoped to THIS client — the join key that differs from the sibling's.
    /// </summary>
    /// <remarks>
    /// A <c>Where(a =&gt; a.EmployeeId == id)</c> copied from <c>CompassEmployeeAssignmentStatusTests</c>'s
    /// subject would still pass the three-shape test above, because employee 1 holds all three. It fails
    /// here: employee 1 also holds a CURRENT assignment on another client, so an employee-keyed lookup
    /// would report this client's long-ended row as Active.
    /// </remarks>
    [Fact]
    public async Task TheStatusLookupIsScopedToThisClient_NotToTheEdjer()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Employee 1 holds an OPEN-ENDED assignment on the other client. Its currency must not colour
        // any row on this client's record.
        await AddAssignmentAsync(id: 5, clientId: OtherClientId, employeeId: 1, endDate: null);

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(ClientId), Token);

        // Assert
        var view = await response.Content.ReadFromJsonAsync<ClientViewDto>(Token);
        view.ShouldNotBeNull();

        view.AssignmentHistory.Select(a => a.AssignmentId)
            .ShouldBe([1, 2, 3], ignoreOrder: true, "the other client's assignment is not this client's history");

        StatusOf(view, 3)
            .ShouldBe(
                "Inactive",
                "assignment 3 ended in 2023. If this reads Active, the status lookup is keyed on the "
                    + "EDJEr rather than the client — the copy-paste error this case exists to catch"
            );
    }

    /// <summary>
    /// BR-16: business records are never purged, and AC-24 must return history older than the audit
    /// window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRD v11 makes the case in AC-24's own paragraph: business records are never purged "because
    /// AC-24 and AC-39 depend on history older than two years and would silently return incomplete
    /// results if business data were aged out." Constitution Principle VIII names AC-24 by number for
    /// the same reason.
    /// </para>
    /// <para>
    /// This passes today, and that is the point. No truncation exists to catch — the retention job
    /// is unbuilt (Known Gap #145). It is a regression pin, not a bug hunt: it fails the day someone adds
    /// a window, a <c>Take</c>, or a date floor to this read. Nothing here asserts retention IS enforced.
    /// </para>
    /// <para>
    /// The departed EDJEr rides along deliberately: an aged row is the likeliest one to belong to someone
    /// who has left, so the two conditions co-occur in reality and are worth asserting together.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task HistoryOlderThanTheAuditWindow_IsStillReturned()
    {
        // Arrange
        await ResetDatabaseAsync();
        var today = await SeedAsync();

        // Three years back — beyond ADR-007's two-year audit retention — and held by a DEPARTED EDJEr.
        var longAgo = today.AddYears(-3);
        await AddDepartedEdjerAsync(employeeId: 9);
        await AddAssignmentAsync(
            id: 6,
            clientId: ClientId,
            employeeId: 9,
            startDate: longAgo.AddYears(-1),
            endDate: longAgo
        );

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(ClientId), Token);

        // Assert
        var view = await response.Content.ReadFromJsonAsync<ClientViewDto>(Token);
        view.ShouldNotBeNull();

        var aged = view.AssignmentHistory.SingleOrDefault(a => a.AssignmentId == 6);
        aged.ShouldNotBeNull(
            "an assignment that ended three years ago is a business record, not a log line (BR-16)"
        );
        aged.Status.ShouldBe("Inactive");
        aged.EmployeeIsActive.ShouldBe(
            false,
            "AC-15 — a Super Admin sees the departed EDJEr, and the row is marked as theirs"
        );
    }

    /// <summary>
    /// Newest assignment first (issue #456), against real PostgreSQL.
    /// </summary>
    /// <remarks>
    /// <c>CompassClientViewEndpointsTests.Get_AssignmentHistory_IsOrderedNewestStartDateFirst</c> proves
    /// the ordering against the InMemory provider, which — per this file's own header comment — proves
    /// ordering, not translatability. The sort here is applied to the entity's own <c>StartDate</c>
    /// column BEFORE the projection into <c>ClientAssignmentHistoryDto</c>, which
    /// is the translatable shape; this pins that it
    /// actually is, rather than trusting the doc comment. The EDJEr-side twin of this pin shipped as
    /// issue #450's <c>AssignmentHistory_IsOrderedNewestFirst_AgainstRealSql</c>.
    /// </remarks>
    [Fact]
    public async Task AssignmentHistory_IsOrderedNewestFirst_AgainstRealSql()
    {
        // Arrange — seeded OUT of start-date order, so a passing assertion cannot be explained by
        // insertion order coincidentally matching chronological order.
        await ResetDatabaseAsync();
        await SeedAsync(withAssignments: false);
        await AddAssignmentAsync(id: 1, clientId: ClientId, employeeId: 1, startDate: new DateOnly(2020, 1, 1), endDate: new DateOnly(2020, 12, 31));
        await AddAssignmentAsync(id: 2, clientId: ClientId, employeeId: 1, startDate: new DateOnly(2022, 6, 15), endDate: null);
        await AddAssignmentAsync(id: 3, clientId: ClientId, employeeId: 1, startDate: new DateOnly(2021, 3, 10), endDate: new DateOnly(2022, 1, 1));

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(ClientId), Token);

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "ordering a raw column ahead of the projection must translate to SQL"
        );
        var view = await response.Content.ReadFromJsonAsync<ClientViewDto>(Token);
        view.ShouldNotBeNull();
        view.AssignmentHistory.Select(a => a.AssignmentId).ShouldBe([2, 3, 1]);
    }

    [Fact]
    public async Task AClientWithNoAssignments_ReturnsAnEmptyHistory_NotAnError()
    {
        // Arrange — the status pass must cope with nothing to apply it to. A foreach over an empty list
        // is fine; a Single() or a First() would not be, and that is the kind of thing that only shows
        // up on the record nobody thinks to open. A brand-new client is exactly this case.
        await ResetDatabaseAsync();
        await SeedAsync(withAssignments: false);

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(ClientId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await response.Content.ReadFromJsonAsync<ClientViewDto>(Token);
        view.ShouldNotBeNull();
        view.AssignmentHistory.ShouldBeEmpty();
        view.Status.ShouldBe("Inactive", "no assignments means inactive, and status gates nothing");
    }

    // ------------------------------------------------------------------ Helpers

    private static string StatusOf(ClientViewDto view, int assignmentId) =>
        view.AssignmentHistory.Single(a => a.AssignmentId == assignmentId).Status;

    /// <summary>Seeds two clients, one EDJEr, and — unless told otherwise — the three status shapes.</summary>
    /// <returns>The business date the fixtures are anchored to.</returns>
    private async Task<DateOnly> SeedAsync(bool withAssignments = true)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<ICompassBusinessDate>().Today();

        context.Set<EmployeeType>()
            .Add(new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });
        context.Set<Client>()
            .AddRange(
                new Client { Id = ClientId, ClientName = "Buckeye Mutual" },
                new Client { Id = OtherClientId, ClientName = "Other Client" }
            );
        context.Set<Employee>()
            .Add(
                new Employee
                {
                    Id = 1,
                    FirstName = "Ada",
                    LastName = "Lovelace",
                    Email = "ada.lovelace@example.test",
                    StateOfResidence = "OH",
                    EmployeeTypeId = 1,
                    HireDate = new DateOnly(2020, 1, 6),
                    IsActive = true,
                }
            );

        if (withAssignments)
        {
            context.Set<ClientAssignment>()
                .AddRange(
                    // Open-ended.
                    new ClientAssignment
                    {
                        Id = 1,
                        EmployeeId = 1,
                        ClientId = ClientId,
                        StartDate = new DateOnly(2022, 1, 1),
                        EndDate = null,
                    },
                    // Ends in the future — relative to the business date, never a literal.
                    new ClientAssignment
                    {
                        Id = 2,
                        EmployeeId = 1,
                        ClientId = ClientId,
                        StartDate = new DateOnly(2022, 1, 1),
                        EndDate = today.AddDays(30),
                    },
                    // Long ended. A literal is safe here: 2023 cannot become the future.
                    new ClientAssignment
                    {
                        Id = 3,
                        EmployeeId = 1,
                        ClientId = ClientId,
                        StartDate = new DateOnly(2021, 1, 1),
                        EndDate = new DateOnly(2023, 6, 30),
                    }
                );
        }

        await context.SaveChangesAsync(Token);
        return today;
    }

    private async Task AddDepartedEdjerAsync(int employeeId)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<Employee>()
            .Add(
                new Employee
                {
                    Id = employeeId,
                    FirstName = "Chris",
                    LastName = "Doyle",
                    Email = "chris.doyle@example.test",
                    StateOfResidence = "OH",
                    EmployeeTypeId = 1,
                    HireDate = new DateOnly(2018, 3, 5),
                    IsActive = false,
                }
            );

        await context.SaveChangesAsync(Token);
    }

    private async Task AddAssignmentAsync(
        int id,
        int clientId,
        int employeeId,
        DateOnly? endDate,
        DateOnly? startDate = null
    )
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<ClientAssignment>()
            .Add(
                new ClientAssignment
                {
                    Id = id,
                    EmployeeId = employeeId,
                    ClientId = clientId,
                    StartDate = startDate ?? new DateOnly(2022, 1, 1),
                    EndDate = endDate,
                }
            );

        await context.SaveChangesAsync(Token);
    }
}
