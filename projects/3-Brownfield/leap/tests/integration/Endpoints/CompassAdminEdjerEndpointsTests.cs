using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

// Compass's own entities. Aliased rather than imported bare, matching CompassAuditTests: this project
// has a `.Compass` namespace, so an unqualified import reads as though it referred to that.
using CompassClient = LeadingEDJE.Leap.Api.Modules.Compass.Client;
using CompassClientAssignment = LeadingEDJE.Leap.Api.Modules.Compass.ClientAssignment;
using CompassEmployee = LeadingEDJE.Leap.Api.Modules.Compass.Employee;
using CompassEmployeeType = LeadingEDJE.Leap.Api.Modules.Compass.EmployeeType;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// EDJEr configuration against real PostgreSQL, over the real HTTP pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This file asserts only what the in-memory provider structurally cannot. Status codes, payload
/// shapes and the validation rules are <c>tests/unit/Endpoints/CompassAdminEdjerEndpointsTests</c>'s and
/// <c>tests/unit/Services/CompassEmployeeServiceTests</c>'s, and duplicating them here would buy a slower
/// second copy. What needs a real database: SQL <c>lower()</c> collisions through the actual unique index,
/// real foreign keys, audit rows landing in <c>public.audit_logs</c> with a real <c>text[]</c>
/// effective-roles column, and the deactivation guard reading real <c>compass.client_assignment</c> rows.
/// </para>
/// <para>
/// Ordering deviation, stated plainly: written AFTER the implementation. RED-first driving was done
/// by the unit-level suites in bc7c8bee (55 failing assertions observed and committed before any
/// behaviour). These add the assertions that could not have been red for the right reason beforehand — an
/// index and an audit table cannot be exercised through a service that throws. US1's T019 recorded the
/// same deviation for the same reason.
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassAdminEdjerEndpointsTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassAdminEdjerEndpointsTests(IntegrationTestFactory factory)
        : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string Route = "/api/compass/v1/admin/edjers";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string UniqueEmail() => $"edjer.{Guid.NewGuid():N}@example.test";

    private static CompassEdjerRequest Request(
        int employeeTypeId,
        string? email = null,
        bool isActive = true,
        int? coachEmployeeId = null,
        string firstName = "Ada",
        string lastName = "Lovelace",
        string state = "OH",
        bool timesheetRequired = true,
        string? timezone = "America/New_York"
    ) =>
        new(
            firstName,
            lastName,
            new DateOnly(2020, 1, 6),
            email ?? UniqueEmail(),
            employeeTypeId,
            coachEmployeeId,
            state,
            isActive,
            timesheetRequired,
            CanSubmitUnder40: false,
            IncludeInPayroll: true,
            Timezone: timezone
        );

    // ------------------------------------------------------------- the reads, as SQL really runs them

    /// <summary>
    /// The list route against real SQL.
    /// </summary>
    /// <remarks>
    /// This is not a duplicate of the unit-level list test. Every read in this surface is a LINQ
    /// query that has to TRANSLATE, and the in-memory provider translates nothing — it evaluates the same
    /// expression tree in .NET, so a shape Npgsql cannot render passes there and answers 500 here. That is
    /// not hypothetical: <c>GetOpenAssignmentsAsync</c> shipped exactly such a shape and this file caught
    /// it on the first run. So each read gets one real-SQL case whose job is simply "it translates".
    /// </remarks>
    [Fact]
    public async Task GetAll_TranslatesToSql_AndResolvesTheClassificationName()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        await CreateEdjerAsync(client, Request(employeeTypeId, lastName: "Zeta"));
        await CreateEdjerAsync(client, Request(employeeTypeId, lastName: "Alpha"));

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var edjers = await response.Content.ReadFromJsonAsync<List<CompassEdjerSummaryDto>>(Token);
        edjers.ShouldNotBeNull();
        edjers.Count.ShouldBe(2);
        edjers[0].LastName.ShouldBe("Alpha", "ordered by family name in SQL, not in memory afterwards");
        edjers[0].EmployeeTypeName.ShouldNotBeNullOrWhiteSpace(
            "the classification name is resolved by the join, so an untranslatable join would surface here"
        );
    }

    [Fact]
    public async Task GetAll_ReportsHireDateStateAndCoachName_FromRealData()
    {
        // Arrange — issue #659: the admin list adopts Team Directory's Hire Date, State and Coach
        // columns. The coach's name is resolved from the same already-materialised read, so this is
        // also the one case proving that lookup against a real row rather than the in-memory double.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        var coach = await CreateEdjerAsync(
            client,
            Request(employeeTypeId, firstName: "Jordan", lastName: "Wells")
        );
        await CreateEdjerAsync(
            client,
            Request(employeeTypeId, firstName: "Ada", lastName: "Lovelace", coachEmployeeId: coach.Id)
                with
            {
                HireDate = new DateOnly(2019, 3, 14),
                StateOfResidence = "CA",
            }
        );

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edjers = await response.Content.ReadFromJsonAsync<List<CompassEdjerSummaryDto>>(Token);
        edjers.ShouldNotBeNull();

        var ada = edjers.Single(edjer => edjer.FirstName == "Ada");
        ada.HireDate.ShouldBe(new DateOnly(2019, 3, 14));
        ada.StateOfResidence.ShouldBe("CA");
        ada.CoachName.ShouldBe("Jordan Wells");

        edjers.Single(edjer => edjer.FirstName == "Jordan").CoachName.ShouldBeNull();
    }

    [Fact]
    public async Task GetById_TranslatesToSql_AndReturnsTheFullRecord()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();
        var created = await CreateEdjerAsync(client, Request(employeeTypeId));

        // Act
        var response = await client.GetAsync($"{Route}/{created.Id}", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched = await response.Content.ReadFromJsonAsync<CompassEdjerDto>(Token);
        fetched.ShouldNotBeNull();
        fetched.Email.ShouldBe(created.Email);
        fetched.HireDate.ShouldBe(
            new DateOnly(2020, 1, 6),
            "DateOnly maps to a native Postgres date with no converter"
        );
    }

    // -------------------------------------------------- the email rule, as the DATABASE enforces it

    [Theory]
    [InlineData(true, "Ada.Lovelace@example.test", "differing only by case, against an ACTIVE EDJEr")]
    [InlineData(false, "ADA.LOVELACE@EXAMPLE.TEST", "upper-cased, against an INACTIVE EDJEr")]
    [InlineData(true, "  ada.lovelace@example.test  ", "surrounded by whitespace")]
    [InlineData(false, " Ada.Lovelace@example.test ", "case AND whitespace, against an INACTIVE EDJEr")]
    public async Task Post_WithAnEmailThatNormalisesToAnExistingOne_Returns409(
        bool counterpartIsActive,
        string submitted,
        string because
    )
    {
        // Arrange — this is the assertion the in-memory provider cannot make. Its string comparison is
        // .NET's, not Postgres's, so a passing in-memory test says nothing about whether SQL lower()
        // agrees. Here the query really is translated and ux_employee_email_ci really is consulted.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        var seeded = await client.PostAsJsonAsync(
            Route,
            Request(employeeTypeId, "ada.lovelace@example.test", isActive: counterpartIsActive),
            Token
        );
        seeded.StatusCode.ShouldBe(HttpStatusCode.Created, "the arrangement itself must succeed");

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(employeeTypeId, submitted),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, because);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var stored = await db.Set<CompassEmployee>().CountAsync(Token);
        stored.ShouldBe(1, "no second EDJEr may exist");
    }

    [Fact]
    public async Task Post_StoresTheNormalisedEmail_SoTheStoredFormMatchesTheIndexedOne()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(employeeTypeId, "  Ada.Lovelace@Example.test  "),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var stored = await db.Set<CompassEmployee>().AsNoTracking().SingleAsync(Token);
        stored.Email.ShouldBe("ada.lovelace@example.test");
    }

    [Fact]
    public async Task Post_WritesToTheCompassSchema_NotToTheLegacyDirectoryTable()
    {
        // Arrange — spec Q2: Stream 2 writes Compass's OWN compass.employee, which is the store the
        // directory boundary already serves since feature 003. Writing public.employees instead would be
        // a second copy of the directory alongside it, and every gate but this one would still pass.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(Route, Request(employeeTypeId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(Token);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM compass.employee;";
        Convert.ToInt32(await command.ExecuteScalarAsync(Token)).ShouldBe(1);

        // The legacy public.employees table was dropped by DropLegacyDirectoryTables, so the write
        // can only have landed in the Compass schema asserted above.
    }

    // -------------------------------------------------------------- FR-019: the deactivation guard

    [Fact]
    public async Task Put_DeactivatingAnEdjerWithARealOpenAssignment_Returns422AndNamesTheBlockers()
    {
        // Arrange — the guard reads real compass.client_assignment rows, so the NULL end_date that means
        // "open-ended" is a real NULL and the client's name comes from a real join.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        var created = await CreateEdjerAsync(client, Request(employeeTypeId));
        var clientId = await SeedClientAsync("Buckeye Mutual");
        await SeedAssignmentAsync(created.Id, clientId, endDate: null);

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            Request(employeeTypeId, created.Email, isActive: false),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadAsStringAsync(Token);
        body.ShouldContain("Buckeye Mutual", Case.Insensitive, "the refusal must be actionable");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var stored = await db.Set<CompassEmployee>()
            .AsNoTracking()
            .SingleAsync(employee => employee.Id == created.Id, Token);
        stored.IsActive.ShouldBeTrue("the EDJEr must remain active");

        var assignment = await db.Set<CompassClientAssignment>().AsNoTracking().SingleAsync(Token);
        assignment.EndDate.ShouldBeNull("no assignment is ever auto-ended (AC-19)");
    }

    [Fact]
    public async Task Put_DeactivatingAnEdjerWhoseAssignmentsAreAllEnded_Succeeds()
    {
        // Arrange — US2 scenario 11. An end-dated assignment must not block, which is the other half of
        // the guard and the half a too-broad query would break.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        var created = await CreateEdjerAsync(client, Request(employeeTypeId));
        var clientId = await SeedClientAsync("Granville Retail Group");

        // After the start date, or ck_client_assignment_end_on_or_after_start rejects the arrangement
        // itself — which is the constraint doing its job, not a hint about the guard.
        await SeedAssignmentAsync(created.Id, clientId, endDate: new DateOnly(2024, 9, 30));

        // Act
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            Request(employeeTypeId, created.Email, isActive: false),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var stored = await db.Set<CompassEmployee>()
            .AsNoTracking()
            .SingleAsync(employee => employee.Id == created.Id, Token);
        stored.IsActive.ShouldBeFalse();
    }

    // ----------------------------------------------------------------------- FR-017: the audit trail

    [Fact]
    public async Task Post_WritesAnAuditRow_CarryingTheActorTheChangesAndTheEffectiveRoles()
    {
        // Arrange — asserted against the real audit_logs table, because effective roles are a Postgres
        // text[] and the normalising setter plus the array mapping are only exercised for real here.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var created = await CreateEdjerAsync(client, Request(employeeTypeId));

        // Assert
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var entry = await db.Set<AuditLog>()
            .AsNoTracking()
            .Where(log => log.EntityType == "CompassEmployee")
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
        entry.Changes.ShouldContain("email", Case.Insensitive);
    }

    [Fact]
    public async Task Put_WritesASecondAuditRow_RecordingOnlyWhatChanged()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();
        var created = await CreateEdjerAsync(client, Request(employeeTypeId));

        // Act — change one profile field and one time-tracking flag.
        var response = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            Request(employeeTypeId, created.Email, firstName: "Augusta", timesheetRequired: false),
            Token
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var entries = await db.Set<AuditLog>()
            .AsNoTracking()
            .Where(log => log.EntityType == "CompassEmployee")
            .OrderBy(log => log.Id)
            .ToListAsync(Token);

        entries.Count.ShouldBe(2, "a create and an update are two attributable events");

        var update = entries[1];
        update.Action.ShouldBe("update");
        update.Changes.ShouldNotBeNull();
        update.Changes.ShouldContain("FirstName");
        update.Changes.ShouldContain("TimesheetRequired");
        update.Changes.ShouldNotContain(
            "LastName",
            Case.Sensitive,
            "an unchanged field in the change list makes a real change harder to find"
        );
        update.EffectiveRoles.ShouldNotBeNull();
    }

    [Fact]
    public async Task ARefusedWrite_WritesNoAuditRow()
    {
        // Arrange — an audit row for something that did not happen is worse than no row.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act — a state outside the 50 plus DC.
        var response = await client.PostAsJsonAsync(
            Route,
            Request(employeeTypeId, state: "ZZ"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var audited = await db.Set<AuditLog>()
            .AsNoTracking()
            .CountAsync(log => log.EntityType == "CompassEmployee", Token);
        audited.ShouldBe(0);
    }

    // -------------------------------------------------------------------- real referential integrity

    [Fact]
    public async Task Post_NamingACoachWhoDoesNotExist_Returns400RatherThanAForeignKeyFailure()
    {
        // Arrange — without the service's own check this would reach Postgres and surface as a 500 from a
        // foreign-key violation. The in-memory provider enforces no FK, so only this side can tell the
        // difference between "validated" and "happened to work".
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(employeeTypeId, coachEmployeeId: 999999),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_NamingARetiredEmployeeType_Returns400RatherThanStoringIt()
    {
        // Arrange — FR-005 against a real row that really is inactive.
        await ResetDatabaseAsync();
        var retiredTypeId = await SeedEmployeeTypeAsync(isActive: false);
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(Route, Request(retiredTypeId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<CompassEmployee>().CountAsync(Token)).ShouldBe(0);
    }

    [Fact]
    public async Task EveryWriteRoute_RefusesACompassAdminPrincipal()
    {
        // Arrange — SC-006 through the real pipeline, with the interface bypassed entirely. The unit
        // suite asserts the same thing; this one proves it survives real authentication and the real
        // CompositeAuthorizationResolver, including its additive user_roles fallback.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassAdmin();

        // Act
        var post = await client.PostAsJsonAsync(Route, Request(employeeTypeId), Token);
        var put = await client.PutAsJsonAsync($"{Route}/1", Request(employeeTypeId), Token);

        // Assert
        post.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        put.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ARealConcurrentEmailRace_ProducesExactlyOneCreatedAndOneConflict()
    {
        // Arrange — the losing writer's real failure, raced rather than simulated. The pre-check is a
        // check-then-act, so both callers can pass it and the unique index rejects one. Unhandled, that
        // is a 500; handled, it is a 409 identical to the sequential case. The in-memory provider cannot
        // stage this at all — it enforces no unique index.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var email = UniqueEmail();

        var first = _factory.AsCompassSuperAdmin();
        var second = _factory.AsCompassSuperAdmin();

        // Act
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync(Route, Request(employeeTypeId, email), Token),
            second.PostAsJsonAsync(Route, Request(employeeTypeId, email), Token)
        );

        // Assert
        var codes = responses.Select(response => response.StatusCode).OrderBy(code => code).ToList();
        codes.ShouldBe(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            $"expected exactly one Created and one Conflict, got {string.Join(", ", codes)}"
        );

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<CompassEmployee>().CountAsync(Token)).ShouldBe(1);
    }

    // -------------------------------------------------------- FR-8.1: the timezone, against real SQL

    /// <summary>
    /// A chosen zone survives the round trip through <c>compass.employee.timezone</c>.
    /// </summary>
    /// <remarks>
    /// What only a real database can say here is that the column actually holds the submitted
    /// identifier. It is <c>varchar(64)</c> with a <c>DEFAULT 'America/New_York'</c> and no CHECK, so
    /// the two failures worth ruling out are a value silently truncated by the width and a value
    /// silently replaced by the default because the write never sent one — neither of which the EF
    /// InMemory provider models. <c>Pacific/Honolulu</c> rather than Eastern precisely so a fall-back
    /// to the default is visible rather than coincidentally correct.
    /// </remarks>
    [Fact]
    public async Task Create_WithATimezone_StoresTheIanaIdAndReadsItBack()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var created = await CreateEdjerAsync(
            client,
            Request(employeeTypeId, timezone: "Pacific/Honolulu")
        );

        // Assert — the response, the re-read, and the row itself.
        created.Timezone.ShouldBe("Pacific/Honolulu");

        var reread = await client.GetFromJsonAsync<CompassEdjerDto>(
            $"{Route}/{created.Id}",
            Token
        );
        reread.ShouldNotBeNull();
        reread.Timezone.ShouldBe("Pacific/Honolulu");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var stored = await db.Set<CompassEmployee>().SingleAsync(Token);
        stored.Timezone.ShouldBe("Pacific/Honolulu");
    }

    /// <summary>
    /// An omitted timezone takes the Eastern default rather than being refused.
    /// </summary>
    /// <remarks>
    /// The additive-only contract's obligation (FR-082): <c>/api/compass/v1</c> gained this property,
    /// so a caller that predates it must still be able to create an EDJEr. Asserted against the real
    /// column because the value has two possible sources that must agree — the service's explicit
    /// fallback and the column's own <c>DEFAULT</c> — and a row is the only place to see which arrived.
    /// </remarks>
    [Fact]
    public async Task Create_WithNoTimezone_IsAcceptedAndStoresEastern()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var created = await CreateEdjerAsync(client, Request(employeeTypeId, timezone: null));

        // Assert
        created.Timezone.ShouldBe("America/New_York");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<CompassEmployee>().SingleAsync(Token)).Timezone.ShouldBe("America/New_York");
    }

    /// <summary>A zone outside the six is refused, and writes nothing.</summary>
    /// <remarks>
    /// The unit suite covers the rule; this covers the consequence of it ever being removed. The
    /// column carries NO CHECK constraint — deliberately, so the six can widen without a migration
    /// (see <c>UsTimeZones</c>) — so validation is the ONLY thing standing between an unusable value
    /// and the database. There is no backstop to catch it here, which is exactly why the refusal is
    /// asserted against the real one.
    /// </remarks>
    [Theory]
    [InlineData("Europe/London", "a real IANA zone outside the six")]
    [InlineData("", "blank — the form was submitted with nothing chosen")]
    public async Task Create_WithAnUnsupportedTimezone_IsRefusedAndWritesNothing(
        string timezone,
        string because
    )
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(employeeTypeId, timezone: timezone),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, because);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<CompassEmployee>().CountAsync(Token)).ShouldBe(
            0,
            "a refused request must not have written a row"
        );
    }

    // ------------------------------------------------------------------------------------- helpers

    // ------------------------------------------------ FR-014: state of residence, against real SQL

    /// <summary>
    /// A state of residence is REQUIRED, and the refusal happens before any write.
    /// </summary>
    /// <remarks>
    /// Kept as an integration case rather than left to the unit suite because the backstop is a
    /// database CHECK — <c>ck_employee_state_of_residence_us</c> — and the EF InMemory provider has
    /// no constraints at all. If validation ever stopped refusing an empty state, the unit suite
    /// would still pass and this would surface the 500 the column would actually produce.
    /// </remarks>
    [Theory]
    [InlineData("", "blank")]
    [InlineData("   ", "whitespace only")]
    public async Task Create_WithNoStateOfResidence_IsRefused(string state, string because)
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(employeeTypeId, state: state),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, because);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        (await db.Set<CompassEmployee>().CountAsync(Token)).ShouldBe(
            0,
            "a refused request must not have written a row"
        );
    }

    /// <summary>A supplied state outside the 50 plus DC is refused.</summary>
    [Fact]
    public async Task Create_WithASuppliedStateOutsideTheFiftyPlusDC_IsStillRefused()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            Route,
            Request(employeeTypeId, state: "PR"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<int> SeedEmployeeTypeAsync(bool isActive = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employeeType = new CompassEmployeeType
        {
            TypeName = $"Type-{Guid.NewGuid():N}"[..20],
            IsActive = isActive,
        };
        db.Set<CompassEmployeeType>().Add(employeeType);
        await db.SaveChangesAsync(Token);

        return employeeType.Id;
    }

    private async Task<int> SeedClientAsync(string clientName)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var client = new CompassClient { ClientName = clientName, IsInternal = false };
        db.Set<CompassClient>().Add(client);
        await db.SaveChangesAsync(Token);

        return client.Id;
    }

    /// <summary>
    /// Inserts an assignment row directly.
    /// </summary>
    /// <remarks>
    /// Assignment management is Stream 3's surface (spec A-6), so there is no endpoint to arrange this
    /// through. Inserting the row is what makes the guard testable now rather than in a later stream —
    /// which is the whole basis of spec A-4's decision to implement the guard here.
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

    private static async Task<CompassEdjerDto> CreateEdjerAsync(
        HttpClient client,
        CompassEdjerRequest request
    )
    {
        var response = await client.PostAsJsonAsync(Route, request, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, "the arrangement itself must succeed");

        var created = await response.Content.ReadFromJsonAsync<CompassEdjerDto>(Token);
        created.ShouldNotBeNull();
        return created;
    }
}
