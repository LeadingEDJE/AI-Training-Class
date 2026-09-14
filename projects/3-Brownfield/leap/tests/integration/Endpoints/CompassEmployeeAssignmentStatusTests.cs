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
/// AC-20's per-assignment status on the EDJEr record, against real PostgreSQL (issue #223).
/// </summary>
/// <remarks>
/// <para>
/// This exists because the status predicate runs in the DATABASE. The unit-level assertions in
/// <c>tests/unit/Endpoints/CompassEmployeeDetailEndpointsTests</c> run on the InMemory provider, which
/// translates nothing and evaluates the same expression tree in .NET — so a shape Npgsql cannot render
/// passes there and answers 500 here. That is not hypothetical in this module:
/// <c>CompassReadRepository</c> already carries a comment recording the ownership predicate failing
/// exactly that way, and US2's <c>GetOpenAssignmentsAsync</c> shipped a projection that only real SQL
/// rejected.
/// </para>
/// <para>
/// Every live fixture date is RELATIVE to the business date, resolved through
/// <see cref="ICompassBusinessDate"/> rather than <c>DateTime.Today</c> — the application judges
/// "current" in <c>America/New_York</c>, and a fixture anchored to the machine clock disagrees with it
/// every evening (SC-005, and the defect <c>CompassBusinessDateTests</c> now fences).
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassEmployeeAssignmentStatusTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassEmployeeAssignmentStatusTests(IntegrationTestFactory factory)
        : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const int EdjerId = 1;

    /// <summary>A client seeded with <c>IsInternal = true</c> — 1 and 2 from <c>SeedAsync</c> are not.</summary>
    private const int InternalClientId = 3;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Route(int id) => $"/api/compass/team-directory/{id}";

    [Fact]
    public async Task AssignmentHistory_DerivesEachRowsStatus_FromRealSql()
    {
        // Arrange — the three shapes BR-7 distinguishes, all on one EDJEr so one response carries all
        // three and a mis-joined lookup cannot hide behind a single-row fixture.
        await ResetDatabaseAsync();
        var today = await SeedAsync();

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(EdjerId), Token);

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "the IsCurrent predicate must translate to SQL, not fall back to client evaluation"
        );

        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();
        detail.AssignmentHistory.Count.ShouldBe(3);

        StatusOf(detail, 1).ShouldBe("Active", "no end date means current");
        StatusOf(detail, 2).ShouldBe("Active", $"an end date of {today.AddDays(30):O} has not arrived");
        StatusOf(detail, 3).ShouldBe("Inactive", "this one ended in 2023");
    }

    /// <summary>
    /// The boundary the whole component exists for: an assignment ending TODAY is still current.
    /// </summary>
    /// <remarks>
    /// AC-42 names this as the case independent implementations get wrong, and it is the one a fixture
    /// pinned to a literal date can never test twice. Asserted here against real SQL because the
    /// comparison is <c>&gt;=</c> in the database, and an off-by-one there is invisible in .NET.
    /// </remarks>
    [Fact]
    public async Task AnAssignmentEndingExactlyToday_IsStillCurrent()
    {
        // Arrange
        await ResetDatabaseAsync();
        var today = await SeedAsync();
        await AddAssignmentAsync(id: 4, clientId: 1, endDate: today);

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(EdjerId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();

        StatusOf(detail, 4)
            .ShouldBe(
                "Active",
                "an assignment is current for the WHOLE of its end date — the owner decision the "
                    + "contract records, and the case AC-42 says diverging implementations differ on"
            );
    }

    /// <summary>
    /// Newest assignment first (issue #450), against real PostgreSQL.
    /// </summary>
    /// <remarks>
    /// <c>CompassEmployeeDetailEndpointsTests.Get_AssignmentHistory_IsOrderedNewestStartDateFirst</c>
    /// proves the ordering against the InMemory provider, which — per this file's own header comment —
    /// proves ordering, not translatability. The sort here is applied to the entity's own
    /// <c>StartDate</c> column BEFORE the projection into <c>EmployeeAssignmentDto</c>, which
    /// is the translatable shape; this pins that it
    /// actually is, rather than trusting the doc comment.
    /// </remarks>
    [Fact]
    public async Task AssignmentHistory_IsOrderedNewestFirst_AgainstRealSql()
    {
        // Arrange — seeded OUT of start-date order, so a passing assertion cannot be explained by
        // insertion order coincidentally matching chronological order.
        await ResetDatabaseAsync();
        await SeedAsync(withAssignments: false);
        await AddAssignmentAsync(id: 1, clientId: 1, startDate: new DateOnly(2020, 1, 1), endDate: new DateOnly(2020, 12, 31));
        await AddAssignmentAsync(id: 2, clientId: 2, startDate: new DateOnly(2022, 6, 15), endDate: null);
        await AddAssignmentAsync(id: 3, clientId: 1, startDate: new DateOnly(2021, 3, 10), endDate: new DateOnly(2022, 1, 1));

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(EdjerId), Token);

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "ordering a raw column ahead of the projection must translate to SQL"
        );
        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();
        detail.AssignmentHistory.Select(a => a.AssignmentId).ShouldBe([2, 3, 1]);
    }

    [Fact]
    public async Task AnEdjerWithNoAssignments_ReturnsAnEmptyHistory_NotAnError()
    {
        // Arrange — the status pass must cope with nothing to apply it to. A foreach over an empty
        // list is fine; a Single() or a First() would not be, and that is the kind of thing that only
        // shows up on the record nobody thinks to open.
        await ResetDatabaseAsync();
        await SeedAsync(withAssignments: false);

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(EdjerId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();
        detail.AssignmentHistory.ShouldBeEmpty();
    }

    /// <summary>
    /// Issue #518 — each history row carries ITS OWN client's internal-EDJE ("beach") flag.
    /// </summary>
    /// <remarks>
    /// Per-row rather than per-record because an EDJEr's history legitimately MIXES the two: a beach
    /// allocation sits beside real engagements, so the screen has to decide the "View SOWs" affordance
    /// one row at a time. Asserted against real SQL for this file's standing reason — the flag is read
    /// inside a projection the InMemory provider evaluates in .NET rather than translating.
    /// </remarks>
    [Fact]
    public async Task AssignmentHistory_CarriesEachRowsOwnClientInternalFlag()
    {
        // Arrange — one external row and one internal, on the same EDJEr, so a projection that
        // hard-coded either answer fails on the other.
        await ResetDatabaseAsync();
        await SeedAsync(withAssignments: false);
        await AddInternalClientAsync();
        await AddAssignmentAsync(id: 1, clientId: 1, endDate: null);
        await AddAssignmentAsync(id: 2, clientId: InternalClientId, endDate: null);

        // Act
        var response = await _factory.AsCompassSuperAdmin().GetAsync(Route(EdjerId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();

        InternalOf(detail, 1).ShouldBeFalse("client 1 is an ordinary client");
        InternalOf(detail, 2).ShouldBeTrue("client 3 is seeded IsInternal = true");
    }

    // ------------------------------------------------------------------ Helpers

    private static string StatusOf(EmployeeDetailDto detail, int assignmentId) =>
        detail.AssignmentHistory.Single(a => a.AssignmentId == assignmentId).Status;

    private static bool InternalOf(EmployeeDetailDto detail, int assignmentId) =>
        detail.AssignmentHistory.Single(a => a.AssignmentId == assignmentId).IsInternal;

    /// <summary>Seeds one EDJEr and, unless told otherwise, the three status shapes.</summary>
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
                new Client { Id = 1, ClientName = "Client One" },
                new Client { Id = 2, ClientName = "Client Two" }
            );
        context.Set<Employee>()
            .Add(
                new Employee
                {
                    Id = EdjerId,
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
                        EmployeeId = EdjerId,
                        ClientId = 1,
                        StartDate = new DateOnly(2022, 1, 1),
                        EndDate = null,
                    },
                    // Ends in the future — relative to the business date, never a literal.
                    new ClientAssignment
                    {
                        Id = 2,
                        EmployeeId = EdjerId,
                        ClientId = 2,
                        StartDate = new DateOnly(2022, 1, 1),
                        EndDate = today.AddDays(30),
                    },
                    // Long ended. A literal is safe here: 2023 cannot become the future.
                    new ClientAssignment
                    {
                        Id = 3,
                        EmployeeId = EdjerId,
                        ClientId = 2,
                        StartDate = new DateOnly(2021, 1, 1),
                        EndDate = new DateOnly(2023, 6, 30),
                    }
                );
        }

        await context.SaveChangesAsync(Token);
        return today;
    }

    private async Task AddAssignmentAsync(
        int id,
        int clientId,
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
                    EmployeeId = EdjerId,
                    ClientId = clientId,
                    StartDate = startDate ?? new DateOnly(2022, 1, 1),
                    EndDate = endDate,
                }
            );

        await context.SaveChangesAsync(Token);
    }

    /// <summary>Seeds one INTERNAL client (issue #518) alongside the two ordinary ones.</summary>
    private async Task AddInternalClientAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<Client>()
            .Add(new Client { Id = InternalClientId, ClientName = "EDJE", IsInternal = true });

        await context.SaveChangesAsync(Token);
    }
}
