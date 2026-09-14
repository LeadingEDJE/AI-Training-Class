using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
// Compass's own entities. Aliased rather than imported bare, matching CompassAdminEdjerEndpointsTests:
// this project has a `.Compass` namespace, so an unqualified import reads as though it referred to that.
using CompassBillableTimeCategory = LeadingEDJE.Leap.Api.Modules.Compass.BillableTimeCategory;
using CompassClient = LeadingEDJE.Leap.Api.Modules.Compass.Client;
using CompassClientAssignment = LeadingEDJE.Leap.Api.Modules.Compass.ClientAssignment;
using CompassEmployee = LeadingEDJE.Leap.Api.Modules.Compass.Employee;
using CompassEmployeeType = LeadingEDJE.Leap.Api.Modules.Compass.EmployeeType;
using CompassInvoiceFrequencyType = LeadingEDJE.Leap.Api.Modules.Compass.InvoiceFrequencyType;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// Client configuration against real PostgreSQL, over the real HTTP pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This file asserts only what the in-memory provider structurally cannot. Status codes, payload
/// shapes and the validation rules are <c>tests/unit/Endpoints/CompassAdminClientEndpointsTests</c>'s and
/// <c>tests/unit/Services/CompassClientServiceTests</c>'s, and duplicating them here would buy a slower
/// second copy. What needs a real database: the COMPOSITE unique index that makes category names unique
/// per client rather than globally, real foreign keys, LINQ that has to translate, and audit rows landing
/// in <c>public.audit_logs</c> with a real <c>text[]</c> effective-roles column.
/// </para>
/// <para>
/// The composite index is the reason this file matters more than usual. A single-column unique index on
/// <c>category_name</c> would pass every unit test that seeds one client, and would break the ordinary
/// case — two clients both offering "Development" — the first time it met real data.
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassAdminClientEndpointsTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassAdminClientEndpointsTests(IntegrationTestFactory factory)
        : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string Route = "/api/compass/v1/admin/clients";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string UniqueName() => $"Client-{Guid.NewGuid():N}"[..24];

    private static CompassClientRequest Request(
        string? clientName = null,
        int? invoiceFrequencyTypeId = null,
        bool isInternal = false,
        DateOnly? msaSignedDate = null,
        DateOnly? ndaSignedDate = null
    ) =>
        new(
            clientName ?? UniqueName(),
            msaSignedDate,
            ndaSignedDate,
            isInternal,
            invoiceFrequencyTypeId
        );

    // ------------------------------------------------------------- the reads, as SQL really runs them

    /// <summary>
    /// The list route against real SQL.
    /// </summary>
    /// <remarks>
    /// Every read in this surface is a LINQ query that has to TRANSLATE, and the in-memory provider
    /// translates nothing — it evaluates the same expression tree in .NET, so a shape Npgsql cannot render
    /// passes there and answers 500 here. <c>GetOpenAssignmentsAsync</c> shipped exactly such a shape in
    /// US2 and the equivalent file caught it on the first run, which is why the cadence rule
    /// ("Project into a named type LAST") exists.
    /// </remarks>
    [Fact]
    public async Task GetAll_TranslatesToSql_AndResolvesTheInvoiceFrequencyName()
    {
        // Arrange
        await ResetDatabaseAsync();
        var frequencyId = await SeedInvoiceFrequencyTypeAsync("Monthly");
        var client = _factory.AsCompassSuperAdmin();

        await CreateClientAsync(client, Request(clientName: "Zeta Corp", invoiceFrequencyTypeId: frequencyId));
        await CreateClientAsync(client, Request(clientName: "Alpha Corp"));

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var clients = await response.Content.ReadFromJsonAsync<List<ClientSummaryDto>>(Token);
        clients.ShouldNotBeNull();
        clients.Count.ShouldBe(2);
        clients[0]
            .ClientName.ShouldBe("Alpha Corp", "ordered by name in SQL, not in memory afterwards");
        clients.Single(row => row.ClientName == "Zeta Corp")
            .InvoiceFrequencyTypeName.ShouldBe(
                "Monthly",
                "the cadence name is resolved by the join, so an untranslatable join surfaces here"
            );
        clients.Single(row => row.ClientName == "Alpha Corp")
            .InvoiceFrequencyTypeName.ShouldBeNull("the default is optional and a LEFT join must hold");
    }

    [Fact]
    public async Task GetById_TranslatesToSql_AndReturnsTheCategoriesWithIt()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(
            client,
            Request(msaSignedDate: new DateOnly(2024, 3, 1))
        );
        await AddCategoryAsync(client, created.Id, "Development");
        await AddCategoryAsync(client, created.Id, "Support");

        // Act
        var response = await client.GetAsync($"{Route}/{created.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched = await response.Content.ReadFromJsonAsync<ClientDto>(Token);
        fetched.ShouldNotBeNull();
        fetched.MsaSignedDate.ShouldBe(
            new DateOnly(2024, 3, 1),
            "DateOnly maps to a native Postgres date with no converter"
        );
        fetched.BillableTimeCategories.Count.ShouldBe(2, "the categories load with their client");
    }

    // ------------------------------- FR-025 / FR-006, as the COMPOSITE index actually enforces them

    [Fact]
    public async Task PostCategory_WithTheSameNameOnADifferentClient_IsAccepted()
    {
        // Arrange — the case a single-column unique index would break, and the reason this assertion
        // belongs against a real index rather than a fake that was written to agree with it.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var contoso = await CreateClientAsync(client, Request());
        var fabrikam = await CreateClientAsync(client, Request());

        await AddCategoryAsync(client, contoso.Id, "Development");

        // Act
        var response = await client.PostAsJsonAsync(
            $"{Route}/{fabrikam.Id}/billable-time-categories",
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<CompassBillableTimeCategory>().CountAsync(Token)).ShouldBe(2);
    }

    [Fact]
    public async Task PostCategory_WithADuplicateNameOnTheSameClient_IsRejectedByTheIndex()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client, Request());
        await AddCategoryAsync(client, created.Id, "Development");

        // Act
        var response = await client.PostAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories",
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<CompassBillableTimeCategory>().CountAsync(Token)).ShouldBe(1);
    }

    [Fact]
    public async Task Post_WithADuplicateClientName_IsRejectedByTheIndex()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var name = UniqueName();
        await CreateClientAsync(client, Request(clientName: name));

        // Act
        var response = await client.PostAsJsonAsync(Route, Request(clientName: name), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<CompassClient>().CountAsync(Token)).ShouldBe(1);
    }

    [Fact]
    public async Task Post_NamingAnInvoiceFrequencyTypeThatDoesNotExist_IsRefusedNotAForeignKeyFailure()
    {
        // Arrange — the difference matters: a 400 naming the problem is actionable, while an unhandled
        // FK violation is a 500 that tells the administrator nothing.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(invoiceFrequencyTypeId: 999_999),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Deactivating_ACategoryInUse_LeavesTheRowAndItsReferencesInPlace()
    {
        // Arrange — FR-007's shape for categories: retiring a value must not remove it or rewrite what
        // points at it. Asserted against real rows because "the row is still there" is a database fact.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client, Request());
        var category = await AddCategoryAsync(client, created.Id, "Development");

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories/{category.Id}",
            new UpdateBillableTimeCategoryRequest("Development", IsActive: false),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var stored = await db.Set<CompassBillableTimeCategory>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == category.Id, Token);

        stored.IsActive.ShouldBeFalse();
        stored.CategoryName.ShouldBe("Development", "deactivating is not renaming");
        stored.ClientId.ShouldBe(created.Id, "nor is it detaching");
    }

    // --------------------------------------------------- FR-027: the audit trail, and its FULL scope

    [Fact]
    public async Task Post_WritesAnAuditRow_CarryingTheActorTheChangesAndTheEffectiveRoles()
    {
        // Arrange — asserted against the real audit_logs table, because effective roles are a Postgres
        // text[] and the normalising setter plus the array mapping are only exercised for real here.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var created = await CreateClientAsync(client, Request());

        // Assert
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var entry = await db.Set<AuditLog>()
            .AsNoTracking()
            .Where(log => log.EntityType == "CompassClient")
            .SingleAsync(Token);

        entry.EntityId.ShouldBe(created.Id.ToString());
        entry.EntityId.ShouldNotBe("0", "an audit row pointing at id 0 is attributable to nothing");
        entry.Action.ShouldBe("create");
        entry.Actor.ShouldBe(CompassPrincipalFactory.TestEdjeId.ToString());
        entry.Reason.ShouldNotBeNullOrWhiteSpace();
        entry.Timestamp.ShouldNotBe(default);

        entry.EffectiveRoles.ShouldNotBeNull(
            "NULL means NOT CAPTURED, which for an audited write is a Principle VIII failure"
        );
        entry.EffectiveRoles.ShouldContain("Compass Super Admin");
        entry.Changes.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// AC-NFR-3, verbatim: the audited scope includes the invoice-frequency default.
    /// </summary>
    [Fact]
    public async Task Put_ChangingTheInvoiceDefault_IsInsideTheAuditedScope()
    {
        // Arrange
        await ResetDatabaseAsync();
        var frequencyId = await SeedInvoiceFrequencyTypeAsync("Monthly");
        var client = _factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client, Request());

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            Request(clientName: created.ClientName, invoiceFrequencyTypeId: frequencyId),
            Token
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var entries = await db.Set<AuditLog>()
            .AsNoTracking()
            .Where(log => log.EntityType == "CompassClient")
            .OrderBy(log => log.Id)
            .ToListAsync(Token);

        entries.Count.ShouldBe(2, "a create and an update are two attributable events");
        entries[1].Action.ShouldBe("update");
        entries[1].Changes.ShouldNotBeNull();
        entries[1].Changes.ShouldContain("InvoiceFrequencyTypeId");
        entries[1].EffectiveRoles.ShouldNotBeNull();
    }

    /// <summary>
    /// AC-NFR-3, verbatim: the audited scope includes the client's billable time categories.
    /// </summary>
    /// <remarks>
    /// Both the add and the deactivation are asserted, and both must be attributed to the CLIENT's
    /// record — that is what makes "what changed about this client" answerable from one query instead of
    /// two.
    /// </remarks>
    [Fact]
    public async Task CategoryWrites_AreAudited_AgainstTheOwningClientRecord()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client, Request());

        // Act
        var category = await AddCategoryAsync(client, created.Id, "Development");
        var deactivated = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories/{category.Id}",
            new UpdateBillableTimeCategoryRequest("Development", IsActive: false),
            Token
        );
        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var entries = await db.Set<AuditLog>()
            .AsNoTracking()
            .Where(log => log.EntityType == "CompassClient")
            .OrderBy(log => log.Id)
            .ToListAsync(Token);

        entries.Count.ShouldBe(
            3,
            "the client's creation, its category's addition, and that category's deactivation"
        );
        entries.ShouldAllBe(entry => entry.EntityId == created.Id.ToString());
        entries[1].Changes.ShouldContain("Development");
        entries[2].Changes.ShouldContain("IsActive");
        entries.ShouldAllBe(entry => entry.EffectiveRoles != null);
    }

    [Fact]
    public async Task ARefusedWrite_WritesNoAuditRow()
    {
        // Arrange — an audit row for something that did not happen is worse than no row.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var name = UniqueName();
        await CreateClientAsync(client, Request(clientName: name));

        // Act
        var response = await client.PostAsJsonAsync(Route, Request(clientName: name), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Assert
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var entries = await db.Set<AuditLog>()
            .AsNoTracking()
            .Where(log => log.EntityType == "CompassClient")
            .ToListAsync(Token);

        entries.Count.ShouldBe(1, "only the successful creation is attributable");
    }

    // ------------------------------------------------------------------------------------- helpers

    private async Task<int> SeedInvoiceFrequencyTypeAsync(string typeName, bool isActive = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var frequencyType = new CompassInvoiceFrequencyType
        {
            TypeName = typeName,
            IsActive = isActive,
        };
        db.Set<CompassInvoiceFrequencyType>().Add(frequencyType);
        await db.SaveChangesAsync(Token);

        return frequencyType.Id;
    }

    private static async Task<ClientDto> CreateClientAsync(
        HttpClient client,
        CompassClientRequest request
    )
    {
        var response = await client.PostAsJsonAsync(Route, request, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, "the arrangement itself must succeed");

        var created = await response.Content.ReadFromJsonAsync<ClientDto>(Token);
        created.ShouldNotBeNull();
        return created;
    }

    private static async Task<BillableTimeCategoryDto> AddCategoryAsync(
        HttpClient client,
        int clientId,
        string categoryName
    )
    {
        var response = await client.PostAsJsonAsync(
            $"{Route}/{clientId}/billable-time-categories",
            new CreateBillableTimeCategoryRequest(categoryName),
            Token
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Created, "the arrangement itself must succeed");

        var created = await response.Content.ReadFromJsonAsync<BillableTimeCategoryDto>(Token);
        created.ShouldNotBeNull();
        return created;
    }

    // ------------------------------------- US4 T101 / T106: derived status, as real SQL derives it

    [Fact]
    public async Task GetAll_DerivesStatusFromRealAssignmentRows()
    {
        // Arrange — the three contract cases that can differ in SQL: open-ended, future end date, and
        // all-ended. The exactly-today boundary is ClientStatusDerivationTests' subject; what needs a
        // real database here is that the derivation TRANSLATES and joins the right rows.
        await ResetDatabaseAsync();
        var today = BusinessDate();
        var employeeId = await SeedEmployeeAsync();

        var client = _factory.AsCompassSuperAdmin();
        var openEnded = await CreateClientAsync(client, Request());
        var futureEnd = await CreateClientAsync(client, Request());
        var allEnded = await CreateClientAsync(client, Request());
        var never = await CreateClientAsync(client, Request());

        await SeedAssignmentAsync(employeeId, openEnded.Id, endDate: null);
        await SeedAssignmentAsync(employeeId, futureEnd.Id, today.AddDays(30));
        await SeedAssignmentAsync(employeeId, allEnded.Id, today.AddDays(-1));

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "the derivation must translate to SQL");
        var rows = await response.Content.ReadFromJsonAsync<List<ClientSummaryDto>>(Token);
        rows.ShouldNotBeNull();

        Status(rows, openEnded.Id).ShouldBe("Active", "an open-ended assignment is current");
        Status(rows, futureEnd.Id).ShouldBe("Active", "an end date in the future is current");
        Status(rows, allEnded.Id).ShouldBe(
            "Former",
            "every assignment has ended — issue #274's third value, on the admin list the issue was "
                + "filed against");
        Status(rows, never.Id).ShouldBe(
            "Inactive",
            "no assignments at all (FR-034). The pair above and here is the whole of #274's split, and "
                + "these two rows read the SAME word before it");
    }

    [Fact]
    public async Task GetById_DerivesStatusWithoutLoadingTheAssignments()
    {
        // Arrange — the detail route asks the database whether ANY current assignment exists, rather
        // than loading the collection and evaluating in memory. That matters twice: it does not drag a
        // client's whole assignment history into a configuration read, and it cannot silently answer
        // Inactive because a navigation property was not Included — which is the failure mode of the
        // per-client shape when its caller forgets the Include.
        await ResetDatabaseAsync();
        var today = BusinessDate();
        var employeeId = await SeedEmployeeAsync();

        var client = _factory.AsCompassSuperAdmin();
        var engaged = await CreateClientAsync(client, Request());
        await SeedAssignmentAsync(employeeId, engaged.Id, endDate: null);

        // Act
        var response = await client.GetAsync($"{Route}/{engaged.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched = await response.Content.ReadFromJsonAsync<ClientDto>(Token);
        fetched.ShouldNotBeNull();
        fetched.Status.ShouldBe("Active");
    }

    /// <summary>
    /// T106: a zero-assignment client is Inactive and that limits NOTHING (FR-034, FR-037).
    /// </summary>
    /// <remarks>
    /// The half feature 005 could not assert, because there was no client write route to be refused
    /// by. Every client this suite creates is in exactly this state, so a status-driven guard would
    /// have to fail here.
    /// </remarks>
    [Fact]
    public async Task AZeroAssignmentClient_ReadsInactive_AndStaysFullyEditable()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client, Request());

        // Act — every write path this surface offers, against a client the derivation calls Inactive.
        var edited = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            Request(clientName: created.ClientName, isInternal: true),
            Token
        );
        var categoryAdded = await client.PostAsJsonAsync(
            $"{Route}/{created.Id}/billable-time-categories",
            new CreateBillableTimeCategoryRequest("Development"),
            Token
        );

        // Assert
        var listed = await client.GetAsync(Route, Token);
        var rows = await listed.Content.ReadFromJsonAsync<List<ClientSummaryDto>>(Token);
        rows.ShouldNotBeNull();
        Status(rows, created.Id).ShouldBe("Inactive");

        edited.StatusCode.ShouldBe(HttpStatusCode.OK, "Inactive must not lock an edit (FR-037)");
        categoryAdded.StatusCode.ShouldBe(HttpStatusCode.Created);
        rows.ShouldContain(
            row => row.Id == created.Id,
            "Inactive must not hide the client from its own administration list"
        );
    }

    [Fact]
    public async Task AClientBecomesActiveByDerivationAlone_WithNoReactivationStep()
    {
        // Arrange — FR-039/FR-040: adding an assignment is the whole transition. There is no
        // reactivation action anywhere, and the client record itself is untouched by the change.
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync();
        var client = _factory.AsCompassSuperAdmin();
        var created = await CreateClientAsync(client, Request());

        var before = await ReadStatusAsync(client, created.Id);

        // Act — an assignment row appears. Nothing writes to the client.
        await SeedAssignmentAsync(employeeId, created.Id, endDate: null);

        // Assert
        before.ShouldBe("Inactive");
        (await ReadStatusAsync(client, created.Id)).ShouldBe("Active");
    }

    /// <summary>
    /// The admin list derives status SET-WISE, so its query count does not grow with the row count.
    /// </summary>
    /// <remarks>
    /// T101 requires this surface to reuse the shape <c>CompassReadRepository</c> proved, rather than
    /// deriving per row — contract § 2 names per-row derivation an N+1 against AC-NFR-4's p95. Counting
    /// commands for one client count proves nothing; the property is that the count is INDEPENDENT of
    /// how many clients exist, which is why this measures twice.
    /// </remarks>
    [Fact]
    public async Task AdminClientList_DerivesStatusSetWise_SoQueryCountDoesNotGrowWithClientCount()
    {
        // Arrange
        await ResetDatabaseAsync();
        var today = BusinessDate();
        await SeedClientsDirectlyAsync(count: 4);
        var few = await CountCommandsForAdminListAsync(today);

        await SeedClientsDirectlyAsync(count: 26);
        var many = await CountCommandsForAdminListAsync(today);

        // Assert
        many.Rows.ShouldBe(30, "the second measurement must really have more clients");
        many.Commands.ShouldBe(
            few.Commands,
            $"the listing issued {few.Commands} command(s) for 4 clients and {many.Commands} for 30. "
                + "Status must be derived SET-WISE — one query answering \"which of these are active\" "
                + "for the whole set. A count that grows with the row count is the N+1 the contract "
                + "forbids."
        );
    }

    // ------------------------------------------------------------------ US4 helpers

    private static string Status(IEnumerable<ClientSummaryDto> rows, int clientId) =>
        rows.Single(row => row.Id == clientId).Status;

    /// <summary>
    /// The business date the routes themselves use.
    /// </summary>
    /// <remarks>
    /// Resolved through <see cref="ICompassBusinessDate"/> rather than from <c>DateTime.Today</c>: a
    /// fixture anchored to the machine clock disagrees with the application either side of the
    /// Eastern-vs-UTC boundary, which is precisely the defect this component exists to prevent.
    /// </remarks>
    private DateOnly BusinessDate()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ICompassBusinessDate>().Today();
    }

    private async Task<string> ReadStatusAsync(HttpClient client, int clientId)
    {
        var response = await client.GetAsync($"{Route}/{clientId}", Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var fetched = await response.Content.ReadFromJsonAsync<ClientDto>(Token);
        fetched.ShouldNotBeNull();
        return fetched.Status;
    }

    /// <summary>An EDJEr to hang assignments from. Assignment management itself is Stream 3's (A-6).</summary>
    private async Task<int> SeedEmployeeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employeeType = new CompassEmployeeType
        {
            TypeName = $"Type-{Guid.NewGuid():N}"[..20],
            IsActive = true,
        };
        db.Set<CompassEmployeeType>().Add(employeeType);
        await db.SaveChangesAsync(Token);

        var employee = new CompassEmployee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            HireDate = new DateOnly(2020, 1, 6),
            Email = $"edjer.{Guid.NewGuid():N}@example.test",
            EmployeeTypeId = employeeType.Id,
            StateOfResidence = "OH",
            IsActive = true,
        };
        db.Set<CompassEmployee>().Add(employee);
        await db.SaveChangesAsync(Token);

        return employee.Id;
    }

    /// <summary>
    /// Inserts an assignment row directly.
    /// </summary>
    /// <remarks>
    /// Assignment management is Stream 3's surface (spec A-6), so there is no endpoint to arrange this
    /// through — the same reason US2's deactivation-guard tests insert rows.
    /// </remarks>
    private async Task SeedAssignmentAsync(int employeeId, int clientId, DateOnly? endDate)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        db.Set<CompassClientAssignment>()
            .Add(
                new CompassClientAssignment
                {
                    EmployeeId = employeeId,
                    ClientId = clientId,
                    StartDate = new DateOnly(2024, 4, 1),
                    EndDate = endDate,
                }
            );
        await db.SaveChangesAsync(Token);
    }

    /// <summary>Adds clients straight to the database — the count is the point, not the route.</summary>
    private async Task SeedClientsDirectlyAsync(int count)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        for (var i = 0; i < count; i++)
        {
            db.Set<CompassClient>()
                .Add(new CompassClient { ClientName = UniqueName(), IsInternal = false });
        }

        await db.SaveChangesAsync(Token);
    }

    /// <summary>
    /// Runs the admin listing against its own context and counts the commands it issued.
    /// </summary>
    /// <remarks>
    /// A separate <see cref="LeapDbContext"/> over the same connection string, so the interceptor sees
    /// only this call's commands. Mirrors <c>CompassClientDirectoryEndpointsTests</c>'s harness, which
    /// is the shape T101 requires this surface to reuse.
    /// </remarks>
    private async Task<(int Commands, int Rows)> CountCommandsForAdminListAsync(DateOnly today)
    {
        using var scope = Services.CreateScope();
        var hostContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var counter = new CommandCountingInterceptor();
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseNpgsql(hostContext.Database.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(counter)
            .Options;

        await using var context = new LeapDbContext(options);
        var repository = new CompassClientRepository(context, new ClientStatusDerivation());

        var rows = await repository.GetAllWithFrequencyNameAsync(today, Token);

        return (counter.Count, rows.Count);
    }

    /// <summary>Counts the SQL commands a block of work issues.</summary>
    private sealed class CommandCountingInterceptor : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Count++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
