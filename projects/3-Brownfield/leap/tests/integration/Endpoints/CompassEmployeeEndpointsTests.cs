using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// End-to-end authorization and behaviour of the versioned Compass employee endpoint.
/// </summary>
/// <remarks>
/// <para>
/// The 403 and 401 cases matter more here than the 200: they are half of the phase's cross-module
/// isolation criterion, proven at the HTTP layer rather than only at the policy layer.
/// </para>
/// <para>
/// Seeds Compass's own <see cref="Employee"/> and addresses it by <c>int</c>. Both changed in feature
/// 003: the boundary previously resolved against the legacy <c>public.employees</c> under a
/// <c>{id:guid}</c> route constraint.
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassEmployeeEndpointsTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassEmployeeEndpointsTests(IntegrationTestFactory factory) : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string BaseUrl = "/api/compass/v1/employees";

    private HttpClient CreateClientWithPrivileges(params string[] privileges)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.PrivilegeOverrideHeader, string.Join(',', privileges));
        return client;
    }

    private const int EmployeeTypeId = 1;
    private static int _nextEmployeeId = 1;

    private async Task<int> SeedEmployeeAsync(
        string firstName,
        string lastName,
        string timezone = "America/New_York",
        bool isDeliveryTeam = true)
    {
        var employeeId = Interlocked.Increment(ref _nextEmployeeId);
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (!await db.Set<EmployeeType>().AnyAsync(TestContext.Current.CancellationToken))
        {
            db.Set<EmployeeType>().Add(new EmployeeType
            {
                Id = EmployeeTypeId,
                TypeName = "Full Time",
                IsActive = true,
            });
        }

        db.Set<Employee>().Add(new Employee
        {
            Id = employeeId,
            FirstName = firstName,
            LastName = lastName,
            Email = $"compass-ep-{employeeId}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
            Timezone = timezone,
            IsDeliveryTeam = isDeliveryTeam,
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return employeeId;
    }

    [Fact]
    public async Task GetById_WithCompassAdminRole_ReturnsOkAndTheBoundaryDto()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync("Ada", "Lovelace");
        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"{BaseUrl}/{employeeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassEmployeeDto>(
            TestContext.Current.CancellationToken);
        dto.ShouldNotBeNull();
        dto.Id.ShouldBe(employeeId);
        dto.DisplayName.ShouldBe("Ada Lovelace");
        dto.IsActive.ShouldBeTrue();
    }

    /// <summary>
    /// The HTTP twin serializes the EDJEr's IANA timezone, unchanged and ungated (PRD v9 FR-8.4,
    /// issue #422).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The unit-level projection test proves the in-process transport. This is the twin's half, and it
    /// is the half that can fail on its own: the value crosses a JSON boundary here, so a serializer
    /// setting that dropped or rewrote it would be invisible to a reflection test over the DTO.
    /// </para>
    /// <para>
    /// Seeded as <c>America/Denver</c> rather than the Eastern default, and read as raw JSON rather than
    /// through <c>CompassEmployeeDto</c>, so the assertion cannot be satisfied by a field that
    /// round-trips into a default. Asserted under the Compass Admin role — the lower of the two
    /// tiers that can reach this route — because FR-8.4's consumer is not a Super Admin.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task GetById_ReturnsTheIanaTimezone_ToAnOrdinaryCompassAdmin()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync("Mary", "Jackson", timezone: "America/Denver");
        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"{BaseUrl}/{employeeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);

        document.RootElement.TryGetProperty("timezone", out var timezone).ShouldBeTrue(
            $"the published payload must carry the timezone; got: {json}");
        timezone.GetString().ShouldBe("America/Denver");

        // The tier really was the ordinary admin — without this, the assertion above would also hold
        // for a Super Admin and the "ungated" half of the claim would be untested.
        document.RootElement.TryGetProperty("timeTracking", out _).ShouldBeFalse(
            "TimeTracking is Super Admin only, so its absence confirms this caller is not one");
    }

    /// <summary>
    /// The HTTP twin serializes the delivery-team flag, including when it is <c>false</c>.
    /// </summary>
    /// <remarks>
    /// The reflection and in-process tests cannot see a serializer that drops the member, and the
    /// value at risk is the one carrying information: a global
    /// <c>DefaultIgnoreCondition = WhenWritingDefault</c> would omit <c>false</c> and publish only
    /// the EDJErs on the delivery team. Nothing sets that today, so this guards rather than repairs.
    /// Seeded <c>false</c> and read as raw JSON so a member round-tripping into a default cannot
    /// satisfy it, at the lower of the two tiers that reach this route.
    /// </remarks>
    [Fact]
    public async Task GetById_SerializesTheDeliveryTeamFlag_WhenItIsFalse()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync("Katherine", "Johnson", isDeliveryTeam: false);
        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"{BaseUrl}/{employeeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);

        document.RootElement.TryGetProperty("isDeliveryTeam", out var flag).ShouldBeTrue(
            $"the published payload must carry the flag even when false; got: {json}");
        flag.GetBoolean().ShouldBeFalse();

        document.RootElement.TryGetProperty("timeTracking", out _).ShouldBeFalse(
            "TimeTracking is Super Admin only, so its absence confirms this caller is not one");
    }

    [Fact]
    public async Task GetById_WithCompassSuperAdminRole_ReturnsOkAndTheBoundaryDto()
    {
        // Arrange — the Compass root satisfies the Admin policy too (universal access), proven here
        // through the real HTTP pipeline rather than only the policy-layer isolation matrix.
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync("Katherine", "Johnson");
        var client = CreateClientWithPrivileges(RolePolicy.CompassSuperAdminRole);

        // Act
        var response = await client.GetAsync(
            $"{BaseUrl}/{employeeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassEmployeeDto>(
            TestContext.Current.CancellationToken);
        dto.ShouldNotBeNull();
        dto.Id.ShouldBe(employeeId);
    }

    [Theory]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    public async Task GetById_WithNonAdminCompassRole_ReturnsForbidden(string compassRole)
    {
        // Arrange — this endpoint is gated to the Admin policy specifically; a Compass role outside
        // Admin/root must not read through it, even though it is a valid Compass role elsewhere.
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync("Grace", "Hopper");
        var client = CreateClientWithPrivileges(compassRole);

        // Act
        var response = await client.GetAsync(
            $"{BaseUrl}/{employeeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetById_WhenEmployeeDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"{BaseUrl}/999999", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_WithOnlyTimesheetPrivileges_ReturnsForbidden()
    {
        // Arrange — a timesheet root user holds NO Compass privilege. This is the direction that
        // fails open, asserted here at the HTTP layer.
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync("Grace", "Hopper");
        var client = CreateClientWithPrivileges("EDJEr", "Manager", "Admin", "SuperAdmin");

        // Act
        var response = await client.GetAsync(
            $"{BaseUrl}/{employeeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetById_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync("Alan", "Turing");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");

        // Act
        var response = await client.GetAsync(
            $"{BaseUrl}/{employeeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetById_WithANonIntegerSegment_DoesNotReachTheHandler()
    {
        // Arrange — the route constraint is {id:int}; a non-integer segment fails route binding
        // before the handler, so this is a routing 404 rather than a "no such employee" 404. A Guid
        // is used deliberately: it is what the route used to accept, so this also pins that the old
        // shape no longer binds.
        await ResetDatabaseAsync();
        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"{BaseUrl}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------ the delivery-team mapping gate (feature 016)
    //
    // Four assertions. Only the last discriminates the shipped mapping from the naive one; the first
    // three hold under either, and are here for the three writers the catalog default serves.

    /// <summary>
    /// A writer that predates the column omits it, and the stored row still reads <c>true</c>.
    /// </summary>
    /// <remarks>
    /// FR-002a's rollout writer: during a rolling update the previous build inserts EDJErs knowing
    /// nothing of this column, and only the catalog default keeps those inserts off a 23502. Written
    /// as raw SQL naming every column that existed before the migration, because that is the shape
    /// that writer emits. Passes under both candidate mappings, so it settles nothing about the
    /// choice.
    /// </remarks>
    [Fact]
    public async Task ARowWrittenWithoutTheDeliveryFlag_ReadsBackTrue()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var employeeTypeId = await SeedCompassEmployeeTypeAsync(db);
        const string email = "delivery-legacy-writer@example.test";

        // Act
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO compass.employee
                (first_name, last_name, hire_date, email, employee_type_id, is_active,
                 state_of_residence, timesheet_required, can_submit_under_40, include_in_payroll,
                 timezone)
            VALUES
                ('Legacy', 'Writer', DATE '2020-01-06', {0}, {1}, true,
                 'OH', true, false, true,
                 'America/New_York');
            """,
            [email, employeeTypeId],
            TestContext.Current.CancellationToken);

        // Assert
        var stored = await ReadStoredDeliveryFlagAsync(db, email);
        stored.ShouldBeTrue(
            "a row written by a build that predates the column must land on the delivery team");
    }

    /// <summary>
    /// A create whose JSON omits the field reads back <c>true</c> (SC-003).
    /// </summary>
    /// <remarks>
    /// FR-002b's service fallback, over the real pipeline so that binding an absent JSON property to
    /// <c>null</c> is part of what is asserted. The payload is an anonymous object rather than the
    /// request record: a record with the parameter defaulted serialises the property as <c>null</c>,
    /// which is a different wire shape from omitting it. Passes under both candidate mappings.
    /// </remarks>
    [Fact]
    public async Task CreatingAnEdjer_WithoutTheDeliveryFlag_StoresTrue()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var employeeTypeId = await SeedCompassEmployeeTypeAsync(db);
        const string email = "delivery-omitted@example.test";

        var payload = new
        {
            firstName = "Radia",
            lastName = "Perlman",
            hireDate = "2020-01-06",
            email,
            employeeTypeId,
            coachEmployeeId = (int?)null,
            stateOfResidence = "OH",
            isActive = true,
            timesheetRequired = true,
            canSubmitUnder40 = false,
            includeInPayroll = true,
            timezone = "America/New_York",
        };

        // The omission is the whole point, so prove the body really omits it rather than sending an
        // explicit null — otherwise this test survives someone adding the field to the payload.
        JsonSerializer.Serialize(payload).ShouldNotContain("isDeliveryTeam");

        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/compass/v1/admin/edjers", payload, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var stored = await ReadStoredDeliveryFlagAsync(db, email);
        stored.ShouldBeTrue("a create that says nothing about the field must resolve it to true");
    }

    /// <summary>
    /// A create whose JSON explicitly sends <c>false</c> stores <c>false</c> (SC-010).
    /// </summary>
    /// <remarks>
    /// Necessary but NOT discriminating: it passes under both candidate mappings, because the
    /// mapper's convention sets the sentinel to <c>true</c> for a bool whose store default is
    /// <c>true</c>, so an explicit <c>false</c> is written unaided. It sits outside the gate for
    /// that reason, and exists because the in-memory provider cannot establish it.
    /// </remarks>
    [Fact]
    public async Task CreatingAnEdjer_SendingFalse_StoresFalse()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var employeeTypeId = await SeedCompassEmployeeTypeAsync(db);
        const string email = "delivery-explicit-false@example.test";

        var payload = new
        {
            firstName = "Barbara",
            lastName = "Liskov",
            hireDate = "2020-01-06",
            email,
            employeeTypeId,
            coachEmployeeId = (int?)null,
            stateOfResidence = "OH",
            isActive = true,
            timesheetRequired = true,
            canSubmitUnder40 = false,
            includeInPayroll = true,
            timezone = "America/New_York",
            isDeliveryTeam = false,
        };

        // The mirror of the omission proof above: this assertion means nothing unless the body
        // really carries the property, so a rename that dropped it would fail here rather than pass.
        JsonSerializer.Serialize(payload).ShouldContain("isDeliveryTeam");

        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/compass/v1/admin/edjers", payload, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var stored = await ReadStoredDeliveryFlagAsync(db, email);
        stored.ShouldBeFalse("an explicit false must be stored, not discarded in favour of the default");
    }

    /// <summary>
    /// A PUT whose JSON omits the field leaves a correction to <c>false</c> alone (FR-006, SC-004).
    /// </summary>
    /// <remarks>
    /// The update-path twin of the create test above, and over the real pipeline for the same
    /// reason: the service-layer test cannot see JSON binding, and binding an absent property to
    /// <c>null</c> is the whole mechanism. No live caller omits it today — the form always sends a
    /// value and the migration tool sends an explicit null — so the shape under test is a client
    /// older than the field, as <see cref="CompassEdjerRequest"/>'s own doc comment frames it.
    /// </remarks>
    [Fact]
    public async Task UpdatingAnEdjer_WithoutTheDeliveryFlag_LeavesACorrectionAlone()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var employeeTypeId = await SeedCompassEmployeeTypeAsync(db);
        const string email = "delivery-corrected@example.test";
        var client = _factory.AsCompassSuperAdmin();

        // One field list, so a new required member cannot be added to only one of the two shapes.
        // An absent key serializes as an absent property, which is what the omit case needs.
        object Payload(string firstName, bool? isDeliveryTeam)
        {
            var payload = new Dictionary<string, object?>
            {
                ["firstName"] = firstName,
                ["lastName"] = "Hopper",
                ["hireDate"] = "2020-01-06",
                ["email"] = email,
                ["employeeTypeId"] = employeeTypeId,
                ["coachEmployeeId"] = null,
                ["stateOfResidence"] = "OH",
                ["isActive"] = true,
                ["timesheetRequired"] = true,
                ["canSubmitUnder40"] = false,
                ["includeInPayroll"] = true,
                ["timezone"] = "America/New_York",
            };

            if (isDeliveryTeam is { } flag)
            {
                payload["isDeliveryTeam"] = flag;
            }

            return payload;
        }

        var createResponse = await client.PostAsJsonAsync(
            "/api/compass/v1/admin/edjers",
            Payload("Grace", isDeliveryTeam: false),
            TestContext.Current.CancellationToken);
        createResponse.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await createResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var createdId = await db
            .Set<Employee>()
            .Where(e => e.Email == email)
            .Select(e => e.Id)
            .SingleAsync(TestContext.Current.CancellationToken);

        (await ReadStoredDeliveryFlagAsync(db, email))
            .ShouldBeFalse("the correction has to exist before the save can be shown to preserve it");

        var update = Payload("Grace Brewster", isDeliveryTeam: null);
        JsonSerializer.Serialize(update).ShouldNotContain("isDeliveryTeam");

        // Act — an unrelated edit, by a client that knows nothing about this field.
        var response = await client.PutAsJsonAsync(
            $"/api/compass/v1/admin/edjers/{createdId}",
            update,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var stored = await ReadStoredDeliveryFlagAsync(db, email);
        stored.ShouldBeFalse("a save that says nothing about the flag must not reset the correction");
    }

    /// <summary>
    /// The column carries <c>DEFAULT true</c> in the live catalog.
    /// </summary>
    /// <remarks>
    /// <c>HasDefaultValue</c> has to survive <c>ValueGeneratedNever()</c>: the two set different
    /// facets and the migration differ reads the annotation rather than the value-generation one.
    /// Asserted against <c>information_schema</c> rather than the generated migration file, so it is
    /// the database that answers. Passes under both candidate mappings.
    /// </remarks>
    [Fact]
    public async Task TheDeliveryFlagColumn_CarriesATrueDefaultInTheCatalog()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var defaults = await db
            .Database.SqlQueryRaw<string>(
                """
                SELECT COALESCE(column_default, '(no default)')::text AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'compass'
                  AND table_name = 'employee'
                  AND column_name = 'is_delivery_team'
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        defaults.Count.ShouldBe(
            1, "compass.employee must carry exactly one is_delivery_team column");
        defaults[0].ShouldBe("true");
    }

    /// <summary>
    /// THE DISCRIMINATOR: with the catalog default removed, an ordinary create still succeeds.
    /// </summary>
    /// <remarks>
    /// The only assertion in this gate that fails if <c>ValueGeneratedNever()</c> is ever dropped.
    /// Under it EF writes the column on every insert; under <c>HasDefaultValue</c> alone EF omits it
    /// whenever the resolved value is <c>true</c> — the overwhelmingly common create — and Postgres
    /// answers 23502. The DDL is rolled back, so the create has to share that connection, which is
    /// why the real service is constructed over this scope's context instead of driven over HTTP.
    /// </remarks>
    [Fact]
    public async Task CreatingAnEdjer_WritesTheDeliveryFlagItself_WithNoCatalogDefaultToLeanOn()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var employeeTypeId = await SeedCompassEmployeeTypeAsync(db);
        const string email = "delivery-discriminator@example.test";

        await using var transaction = await db.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE compass.employee ALTER COLUMN is_delivery_team DROP DEFAULT;",
            TestContext.Current.CancellationToken);

        // The real create path, with only the caller's identity stubbed: a bare DI scope has no HTTP
        // context, and driving this over HTTP would take a second connection that cannot see the
        // uncommitted DDL above. See NoHttpContextCurrentUser's remarks.
        var edjers = new CompassEmployeeService(
            scope.ServiceProvider.GetRequiredService<ICompassEmployeeRepository>(),
            scope.ServiceProvider.GetRequiredService<ICompassUnitOfWork>(),
            scope.ServiceProvider.GetRequiredService<IAuditService>(),
            new NoHttpContextCurrentUser(),
            scope.ServiceProvider.GetRequiredService<IEdjerDeactivationGuard>());

        // Act
        var result = await edjers.CreateEdjerAsync(
            new CompassEdjerRequest(
                FirstName: "Karen",
                LastName: "Sparck Jones",
                HireDate: new DateOnly(2020, 1, 6),
                Email: email,
                EmployeeTypeId: employeeTypeId,
                CoachEmployeeId: null,
                StateOfResidence: "OH",
                IsActive: true,
                TimesheetRequired: true,
                CanSubmitUnder40: false,
                IncludeInPayroll: true,
                Timezone: "America/New_York"),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(
            CompassWriteStatus.Success,
            $"the insert must not depend on the catalog default; got: {result.Error}");

        var stored = await ReadStoredDeliveryFlagAsync(db, email);
        stored.ShouldBeTrue("EF must have written the value itself, not fallen back to a default");

        await transaction.RollbackAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> SeedCompassEmployeeTypeAsync(LeapDbContext db)
    {
        var employeeType = new EmployeeType
        {
            TypeName = $"Type-{Guid.NewGuid():N}"[..20],
            IsActive = true,
        };
        db.Set<EmployeeType>().Add(employeeType);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return employeeType.Id;
    }

    private static async Task<bool> ReadStoredDeliveryFlagAsync(LeapDbContext db, string email) =>
        await db
            .Database.SqlQueryRaw<bool>(
                """SELECT is_delivery_team AS "Value" FROM compass.employee WHERE email = {0}""",
                email)
            .SingleAsync(TestContext.Current.CancellationToken);
}
