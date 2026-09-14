using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// QA checkpoint CP1 (<c>#90</c>), bullet 4: derived client status agrees across every consuming
/// surface, including the end-date-falling-exactly-today edge (AC-42, BR-11).
/// </summary>
/// <remarks>
/// <para>
/// What this adds that nothing else asserts. <c>ClientStatusSingleDerivationTests</c> fences the
/// single implementation — it fails the build if a second file compares an end date against a
/// business date. That is a source scan, and it can only prove no rival derivation was written.
/// It cannot prove that each consumer actually calls the shared one, that the surfaces resolve
/// the same business date within one request, or that a projection somewhere drops the value on the
/// floor. Only reading the surfaces and comparing their answers does that, and until this file existed
/// exactly two of the five were ever compared against each other
/// (<c>CompassClientDirectoryEndpointsTests.AClientEndingToday_IsStillActive_...</c>).
/// </para>
/// <para>
/// Integration, not unit — mandatory here. The predicates run in the DATABASE. The EF Core
/// InMemory provider translates nothing; it evaluates the same expression tree in .NET, so a shape
/// Npgsql cannot render passes there and answers 500 here.
/// That exact defect shipped in this module already.
/// </para>
/// <para>
/// Every fixture date is RELATIVE to <see cref="ICompassBusinessDate"/>, never
/// <c>DateTime.Today</c> and never a literal. The application judges currency in
/// <c>America/New_York</c>, and a fixture pinned to a literal date passes on the day it is written and
/// rots afterwards — which is precisely the failure AC-42 says the exactly-today case hides.
/// </para>
/// <para>
/// The five-client fixture is the table #90's bullet 4 names as its demonstration. Client D is the named risk.
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class ClientStatusCrossSurfaceAgreementTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public ClientStatusCrossSurfaceAgreementTests(IntegrationTestFactory factory)
        : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const int EmployeeTypeId = 1;
    private const int EmployeeId = 1;

    /// <summary>
    /// No assignments at all — Inactive, and the state of every newly created client.
    /// </summary>
    /// <remarks>
    /// Since issue #274 this is the ONLY case that reads Inactive. Read against
    /// <see cref="ClientE"/>, the pair is the whole of the Inactive/Former split, on every surface at
    /// once — which is the property that makes the split worth asserting here rather than only in the
    /// derivation's own unit tests.
    /// </remarks>
    private const int ClientA = 101;

    /// <summary>One assignment, no end date — Active.</summary>
    private const int ClientB = 102;

    /// <summary>One assignment ending in 30 days — Active.</summary>
    private const int ClientC = 103;

    /// <summary>One assignment ending exactly today — Active. The case AC-42 names.</summary>
    private const int ClientD = 104;

    /// <summary>
    /// Every assignment ended yesterday or earlier — Former since issue #274.
    /// </summary>
    /// <remarks>
    /// This client read <c>Inactive</c> until #274, which is what the issue asked to fix: it and
    /// <see cref="ClientA"/> gave the same word for two states the business distinguishes. The fixture
    /// needed no new client — E and A were already the two sides of the split.
    /// </remarks>
    private const int ClientE = 105;

    /// <summary>The whole fixture, and its expected verdict on every surface.</summary>
    private static readonly (int ClientId, string Expected)[] Fixture =
    [
        (ClientA, "Inactive"),
        (ClientB, "Active"),
        (ClientC, "Active"),
        (ClientD, "Active"),
        (ClientE, "Former"),
    ];

    /// <summary>
    /// The clients holding a current, started assignment — B, C and D. The dashboard's tile counts
    /// active CLIENT ASSIGNMENTS (issue #633), and the fixture gives each of these exactly one such
    /// assignment, each also carrying one SOW mirroring its own dates.
    /// </summary>
    private static readonly int[] ActiveClientIds = [ClientB, ClientC, ClientD];

    /// <summary>
    /// How many surfaces publish a client-status word. The Sales Dashboard is deliberately not
    /// among them — it publishes a count, and has its own test below.
    /// </summary>
    private const int ExpectedSurfaceCount = 6;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------ The agreement

    /// <summary>
    /// Every status-bearing surface returns the same verdict for all five derivation cases.
    /// </summary>
    /// <remarks>
    /// One test rather than one per surface, deliberately: the property is agreement, and a
    /// per-surface test can only tell you a surface is wrong in isolation — never that two of them
    /// disagree, which is the failure AC-42 actually describes.
    /// </remarks>
    [Fact]
    public async Task EverySurfaceAgrees_OnAllFiveDerivationCases()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act — read every surface once, then compare. Reading them all up front means a disagreement
        // reports as a diff between surfaces rather than as one surface's assertion failure.
        var surfaces = await ReadEveryStatusBearingSurfaceAsync();

        // Assert — the guard FIRST. A loop over zero surfaces passes, which is the fail-open shape
        // docs/platform/adding-a-module.md records finding eight times across Phases 48-49.
        surfaces.Count.ShouldBe(
            ExpectedSurfaceCount,
            "if this number changed, a status-bearing surface was added or removed and the new one "
                + "is unverified"
        );

        foreach (var (clientId, expected) in Fixture)
        {
            foreach (var (surfaceName, statusOf) in surfaces)
            {
                statusOf(clientId)
                    .ShouldBe(
                        expected,
                        $"'{surfaceName}' disagrees about client {clientId}. Every surface must read "
                            + "the SAME single derivation (BR-11) — two screens contradicting each "
                            + "other about one client is the AC-42 failure."
                    );
            }
        }
    }

    /// <summary>
    /// The boundary the whole component exists for, read on every surface at once.
    /// </summary>
    /// <remarks>
    /// Separated from the sweep above so the failure names itself in the log. AC-42 calls this the case
    /// independent implementations get wrong, and the contract records the owner
    /// decision the PRD wording contradicts: a client is Active for the WHOLE of its final calendar day.
    /// Writing <c>&gt;</c> instead of <c>&gt;=</c> is silent and is the likeliest defect.
    /// </remarks>
    [Fact]
    public async Task AClientEndingExactlyToday_ReadsActive_OnEverySurface()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var surfaces = await ReadEveryStatusBearingSurfaceAsync();

        // Assert
        surfaces.Count.ShouldBe(
            ExpectedSurfaceCount,
            "the sweep must cover every surface, or this proves less than it reads"
        );

        foreach (var (surfaceName, statusOf) in surfaces)
        {
            statusOf(ClientD)
                .ShouldBe(
                    "Active",
                    $"'{surfaceName}' ended client D's assignment a day early. The comparison is "
                        + "inclusive of today (>=, not >) — an EDJEr is assigned for the whole of "
                        + "their final working day."
                );
        }
    }

    /// <summary>
    /// The dashboard's active-assignments tile faces the same inclusive-today boundary the other
    /// surfaces do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Sales Dashboard is the one consumer that publishes a count rather than a status word,
    /// so it cannot join the sweep above — but its active-assignments tile (issue #459, #633) is
    /// driven by <see cref="IClientStatusDerivation.IsCurrent"/>, whose end boundary is inclusive of
    /// today exactly as it is for the status-bearing surfaces, and can disagree in the same way. The
    /// fixture gives each of B, C and D one current, started assignment (D's ending EXACTLY today) and
    /// E two ended ones, so the count is exactly B + C + D.
    /// </para>
    /// <para>
    /// Client D is what makes this worth asserting. A tile that dropped the inclusive boundary
    /// would answer 2, and no other surface in this file would notice.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheDashboardActiveSows_ShareTheInclusiveTodayBoundaryTheOtherSurfacesUse()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var dashboard = await GetAsync<SalesDashboardDto>("/api/compass/dashboard", _factory.AsCompassSuperAdmin());
        var breakdown = await GetAsync<IReadOnlyList<DashboardBreakdownRowDto>>(
            "/api/compass/dashboard/breakdown/active-sows",
            _factory.AsCompassSuperAdmin()
        );

        // Assert
        dashboard.ActiveSowCount.ShouldBe(
            ActiveClientIds.Length,
            "B, C and D each hold one active assignment (D's ending today); E's have ended and A has none"
        );

        breakdown
            .SelectMany(row => row.Clients)
            .Select(client => client.Id)
            .ShouldBe(
                ActiveClientIds,
                ignoreOrder: true,
                "the tile's breakdown must name the SAME clients the directory calls Active — "
                    + "including D, whose SOW ends today"
            );
    }

    /// <summary>
    /// Neither Inactive nor Former gates anything: clients A and E are offered and fully readable.
    /// </summary>
    /// <remarks>
    /// The non-gating property is what makes the binary rule safe. A brand-new client has no
    /// assignments and is therefore Inactive from the moment it exists, so any surface that filtered on
    /// status would make a new client impossible to use — AC-42's own words. Asserted here alongside the
    /// agreement rather than only in <c>CompassAssignmentPickerTests</c>, because the picker is one of
    /// the six surfaces this file sweeps and CP1 bullet 4 demands both properties of it at once.
    /// </remarks>
    [Fact]
    public async Task AnInactiveOrFormerClient_IsStillOffered_ByEverySurfaceThatListsClients()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var directory = await GetDirectoryRowsAsync();
        var picker = await GetPickerRowsAsync();

        // Assert
        directory
            .Select(row => row.Id)
            .ShouldBe([ClientA, ClientB, ClientC, ClientD, ClientE], ignoreOrder: true,
                "the directory lists every client — Inactive (A) and Former (E) included");

        picker
            .Select(row => row.Id)
            .ShouldBe([ClientA, ClientB, ClientC, ClientD, ClientE], ignoreOrder: true,
                "the assignment picker is NEVER filtered by derived status (FR-040, named regression O6). "
                    + "Issue #274 added a value, not a gate: re-engaging a Former client is creating an "
                    + "assignment for it, so hiding it here would make that impossible");
    }

    // ------------------------------------------------------------------ Reading the surfaces

    /// <summary>
    /// Reads every surface that publishes a client-status word, once, and returns a lookup per surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keep this list in step with the consumers of <see cref="IClientStatusDerivation"/>.
    /// <c>ClientStatusSingleDerivationTests.EveryClientStatusConsumer_IsCoveredByTheCrossSurfaceAgreementTest</c>
    /// fails when a new one appears, and names this method.
    /// </para>
    /// <para>
    /// The two history panels read their status from an assignment row rather than the client, so
    /// their entry projects the client's own assignment back up to a client verdict: a client is Active
    /// exactly when it holds at least one current assignment (BR-11 and BR-7 are the same predicate,
    /// existentially quantified). Client A has no assignments and so has no row — which IS Inactive, and
    /// the mapping says so rather than throwing. Client E has rows, none of them current, which is
    /// Former — the distinction the row COUNT carries and #274 turned into a third word.
    /// </para>
    /// <para>
    /// The fold is an approximation, and the gap is INTENDED — do not "fix" it. Client-level
    /// status is <c>Any(IsCurrent)</c>; an assignment ROW's status is
    /// <c>IsCurrent &amp;&amp; HasStarted</c>. So the two levels answer different questions on purpose:
    /// a client whose only assignment starts in the future is Active at client level (it holds a
    /// not-yet-ended assignment) while its single history row reads Inactive (nobody is there yet), and
    /// this fold would therefore say Former. Confirmed correct as implemented by the business owner,
    /// 2026-08-25 — it is not a divergence of the kind AC-42 forbids, because it is not two
    /// implementations of ONE question; it is one implementation of each of two questions.
    /// </para>
    /// <para>
    /// Consequently: do not add a future-dated assignment to this fixture. Every assignment here
    /// starts two years back precisely so the fold coincides with client-level status and this file
    /// keeps asserting the property it is named for — agreement about the SAME question. A future-dated
    /// fixture client would fail this test while nothing was wrong, and the natural reading of that
    /// failure ("the surfaces disagree") is the one thing it would not mean. Assignment-level currency
    /// and the <c>HasStarted</c> boundary are covered by <c>CompassClientAssignmentStatusTests</c> and
    /// <c>ClientStatusDerivationTests.HasStarted_IsNotImpliedByIsCurrent_AFutureStartIsCurrentButNotActive</c>.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<(string Name, Func<int, string> StatusOf)>> ReadEveryStatusBearingSurfaceAsync()
    {
        var directory = await GetDirectoryRowsAsync();
        var picker = await GetPickerRowsAsync();

        var views = new Dictionary<int, ClientViewDto>();
        foreach (var (clientId, _) in Fixture)
        {
            views[clientId] = await GetAsync<ClientViewDto>(
                $"/api/compass/client-directory/{clientId}",
                _factory.AsCompassSuperAdmin()
            );
        }

        var boundaryClients = new Dictionary<int, CompassClientDto>();
        foreach (var (clientId, _) in Fixture)
        {
            boundaryClients[clientId] = await GetAsync<CompassClientDto>(
                $"/api/compass/v1/clients/{clientId}",
                _factory.AsCompassSuperAdmin()
            );
        }

        var employeeDetail = await GetAsync<EmployeeDetailDto>(
            $"/api/compass/team-directory/{EmployeeId}",
            _factory.AsCompassSuperAdmin()
        );

        return
        [
            ("Client Directory list (ClientDirectoryRowDto.Status)",
                clientId => directory.Single(row => row.Id == clientId).Status),

            ("client view (ClientViewDto.Status)",
                clientId => views[clientId].Status),

            ("Compass Directory boundary client read (CompassClientDto.Status)",
                clientId => boundaryClients[clientId].Status),

            ("assignment client picker (ClientPickerRowDto.DerivedStatus)",
                clientId => picker.Single(row => row.Id == clientId).DerivedStatus),

            ("client record's EDJEr assignment history panel, AC-24",
                clientId => FromAssignmentRows(views[clientId].AssignmentHistory.Select(a => a.Status))),

            ("EDJEr record's client assignment history panel, AC-20",
                clientId => FromAssignmentRows(
                    employeeDetail.AssignmentHistory
                        .Where(a => a.ClientId == clientId)
                        .Select(a => a.Status))),
        ];
    }

    /// <summary>
    /// A client's verdict, folded up from its assignment rows (BR-11 and issue #274).
    /// </summary>
    /// <remarks>
    /// A client with ROWS but none current is Former, and no row at all is Inactive — case 1 of the
    /// derivation, and the state of a brand-new client (issue #274). Written as a fold over the words
    /// the SERVER sent, never by re-deriving from the dates beside them; re-deriving here would make
    /// this file the second implementation it exists to rule out.
    /// </remarks>
    private static string FromAssignmentRows(IEnumerable<string> statuses)
    {
        var rows = statuses.ToList();

        // Three arms since issue #274, and the fold mirrors StatusOfClient's own precedence exactly:
        // any current row wins, then the mere EXISTENCE of rows distinguishes Former from Inactive.
        // Still a fold over the words the SERVER sent and never a re-derivation from the dates beside
        // them — doing that here would make this file the second implementation it exists to rule out.
        return rows.Any(status => status == "Active") ? "Active"
            : rows.Count > 0 ? "Former"
            : "Inactive";
    }

    private async Task<IReadOnlyList<ClientDirectoryRowDto>> GetDirectoryRowsAsync() =>
        await GetAsync<IReadOnlyList<ClientDirectoryRowDto>>(
            "/api/compass/client-directory",
            _factory.AsCompassSuperAdmin()
        );

    private async Task<IReadOnlyList<ClientPickerRowDto>> GetPickerRowsAsync() =>
        await GetAsync<IReadOnlyList<ClientPickerRowDto>>(
            "/api/compass/assignments/pickers/clients",
            _factory.AsCompassSuperAdmin()
        );

    /// <summary>GETs a route and fails with the route in the message rather than a bare null-ref.</summary>
    private static async Task<T> GetAsync<T>(string route, HttpClient client)
        where T : class
    {
        var response = await client.GetAsync(route, Token);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"GET {route} must answer 200 — a status predicate that does not translate answers 500 here "
                + "and passes every unit test"
        );

        var payload = await response.Content.ReadFromJsonAsync<T>(Token);
        payload.ShouldNotBeNull($"GET {route} returned no body");
        return payload;
    }

    // ------------------------------------------------------------------ The fixture

    /// <summary>
    /// Seeds quickstart §3.4's five clients and the one EDJEr who holds all of their assignments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One EDJEr on purpose. The EDJEr-record panel is keyed on the employee, so a single EDJEr
    /// spanning all four assigned clients puts every derivation case into ONE response — which is what
    /// lets that surface join the sweep instead of needing its own fixture.
    /// </para>
    /// <para>
    /// Every client is external (<c>IsInternal = false</c>), so none of them lands on the beach tile and
    /// <c>ActiveSowCount</c> is a clean count of B + C + D (one active assignment each).
    /// </para>
    /// </remarks>
    /// <returns>The business date the fixture is anchored to.</returns>
    private async Task<DateOnly> SeedAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<ICompassBusinessDate>().Today();

        context.Set<EmployeeType>()
            .Add(new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });

        context.Set<Client>()
            .AddRange(
                new Client { Id = ClientA, ClientName = "A — never assigned", IsInternal = false },
                new Client { Id = ClientB, ClientName = "B — open ended", IsInternal = false },
                new Client { Id = ClientC, ClientName = "C — ends in 30 days", IsInternal = false },
                new Client { Id = ClientD, ClientName = "D — ends today", IsInternal = false },
                new Client { Id = ClientE, ClientName = "E — all ended", IsInternal = false }
            );

        context.Set<Employee>()
            .Add(
                new Employee
                {
                    Id = EmployeeId,
                    FirstName = "Ada",
                    LastName = "Lovelace",
                    Email = "ada.lovelace@example.test",
                    StateOfResidence = "OH",
                    EmployeeTypeId = EmployeeTypeId,
                    HireDate = new DateOnly(2020, 1, 6),
                    IsActive = true,
                }
            );

        // A start date safely in the past for every row: HasStarted is inclusive of today, but a fixture
        // that started TODAY would conflate two boundaries in one row and mask which one broke.
        var started = today.AddYears(-2);

        context.Set<ClientAssignment>()
            .AddRange(
                new ClientAssignment
                {
                    Id = 1,
                    EmployeeId = EmployeeId,
                    ClientId = ClientB,
                    StartDate = started,
                    EndDate = null,
                },
                new ClientAssignment
                {
                    Id = 2,
                    EmployeeId = EmployeeId,
                    ClientId = ClientC,
                    StartDate = started,
                    EndDate = today.AddDays(30),
                },
                // The named risk. Relative to the business date, so it is the boundary on every run.
                new ClientAssignment
                {
                    Id = 3,
                    EmployeeId = EmployeeId,
                    ClientId = ClientD,
                    StartDate = started,
                    EndDate = today,
                },
                // Two rows, so "all ended" is genuinely a fold over more than one and not a single-row
                // special case: yesterday, and long ago.
                new ClientAssignment
                {
                    Id = 4,
                    EmployeeId = EmployeeId,
                    ClientId = ClientE,
                    StartDate = started,
                    EndDate = today.AddDays(-1),
                },
                new ClientAssignment
                {
                    Id = 5,
                    EmployeeId = EmployeeId,
                    ClientId = ClientE,
                    StartDate = started.AddYears(-1),
                    EndDate = started,
                }
            );

        // One SOW per assignment, its end date mirroring the assignment's — incidental to the
        // active-assignments tile's own count (issue #633, which no longer reads SOW dates at all),
        // but it means D's SOW also ends EXACTLY today, so this fixture doubles as a check that the
        // ASSIGNMENT boundary (issue #459) faces the SAME inclusive-today rule the currency surfaces
        // do — a `>` where the derivation uses `>=` would drop D and answer 2.
        context.Set<Sow>()
            .AddRange(
                new Sow
                {
                    Id = 1,
                    ClientAssignmentId = 1,
                    SowStartDate = started,
                    SowEndDate = today.AddDays(365),
                    HasPassedApplicationValidation = true,
                },
                new Sow
                {
                    Id = 2,
                    ClientAssignmentId = 2,
                    SowStartDate = started,
                    SowEndDate = today.AddDays(30),
                    HasPassedApplicationValidation = true,
                },
                new Sow
                {
                    Id = 3,
                    ClientAssignmentId = 3,
                    SowStartDate = started,
                    SowEndDate = today,
                    HasPassedApplicationValidation = true,
                },
                new Sow
                {
                    Id = 4,
                    ClientAssignmentId = 4,
                    SowStartDate = started,
                    SowEndDate = today.AddDays(-1),
                    HasPassedApplicationValidation = true,
                },
                new Sow
                {
                    Id = 5,
                    ClientAssignmentId = 5,
                    SowStartDate = started.AddYears(-1),
                    SowEndDate = started,
                    HasPassedApplicationValidation = true,
                }
            );

        await context.SaveChangesAsync(Token);
        return today;
    }
}
