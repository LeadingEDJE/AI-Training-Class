using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// Proves spec 009 Slice 1's grown profile read TRANSLATES against real PostgreSQL (FR-033, SC-009) —
/// the <c>EmployeeType</c> and self-referencing <c>Coach</c> navigation the repository now includes.
/// </summary>
/// <remarks>
/// This is the one class of defect the unit suite structurally cannot catch: the InMemory
/// provider evaluates a query as ordinary
/// LINQ-to-objects regardless of whether Npgsql could ever generate the equivalent SQL. A two-level
/// <c>Include</c> — a lookup table, then a self-join through a NULLABLE foreign key — is exactly the
/// shape most likely to translate differently than it evaluates, so this proves it against a real
/// database rather than trusting <c>CompassEmployeeBoundaryTests</c>' green run.
/// </remarks>
public class CompassDirectoryBoundaryTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private readonly IntegrationTestFactory _factory = factory;

    private const string BaseUrl = "/api/compass/v1/employees";
    private const int EmployeeTypeId = 1;
    private const int CoachId = 1;
    private const int EmployeeId = 2;

    private HttpClient CreateClientWithPrivileges(params string[] privileges)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.PrivilegeOverrideHeader, string.Join(',', privileges));
        return client;
    }

    private async Task SeedEmployeeWithCoachAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        db.Set<EmployeeType>().Add(
            new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        db.Set<Employee>().AddRange(
            new Employee
            {
                Id = CoachId,
                FirstName = "Cody",
                LastName = "Coach",
                Email = "cody.coach@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = EmployeeTypeId,
                HireDate = new DateOnly(2015, 3, 1),
                IsActive = true,
            },
            new Employee
            {
                Id = EmployeeId,
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada.lovelace@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = EmployeeTypeId,
                CoachEmployeeId = CoachId,
                HireDate = new DateOnly(2020, 1, 15),
                IsActive = true,
                TimesheetRequired = true,
                CanSubmitUnder40 = false,
                IncludeInPayroll = true,
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetById_TranslatesTheEmployeeTypeAndCoachIncludes_AgainstRealPostgres()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedEmployeeWithCoachAsync();
        var client = CreateClientWithPrivileges(RolePolicy.CompassSuperAdminRole);

        // Act — the translation proof: EF must generate SQL for the two-level Include (EmployeeType,
        // then the self-referencing Coach), not merely evaluate it as LINQ-to-objects.
        var response = await client.GetAsync(
            $"{BaseUrl}/{EmployeeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassEmployeeDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.EmployeeType.ShouldBe("Full Time");
        dto.Coach.ShouldNotBeNull();
        dto.Coach.Id.ShouldBe(CoachId);
        dto.Coach.DisplayName.ShouldBe("Cody Coach");
        dto.TimeTracking.ShouldNotBeNull();
        dto.TimeTracking.TimesheetRequired.ShouldBeTrue();
    }

    [Fact]
    public async Task GetById_WhenTheEmployeeHasNoCoach_ReturnsNullCoach_NotAFailedJoin()
    {
        // Arrange — CoachEmployeeId is nullable. An inner-join-shaped translation would silently drop
        // a coachless employee's row instead of returning it with Coach absent (issue #245's own
        // failure mode, one join earlier).
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        db.Set<EmployeeType>().Add(
            new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        db.Set<Employee>().Add(new Employee
        {
            Id = EmployeeId,
            FirstName = "Solo",
            LastName = "NoCoach",
            Email = "solo.nocoach@example.test",
            StateOfResidence = "OH",
            EmployeeTypeId = EmployeeTypeId,
            HireDate = new DateOnly(2021, 1, 1),
            IsActive = true,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"{BaseUrl}/{EmployeeId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassEmployeeDto>(
            TestContext.Current.CancellationToken);
        dto.ShouldNotBeNull();
        dto.Coach.ShouldBeNull();
    }

    [Fact]
    public async Task GetInvoiceFrequencies_TranslatesTheIsActiveFilter_AgainstRealPostgres()
    {
        // Arrange -- spec 009 Slice 2 (FR-010, SC-009). The unit suite's InMemory provider evaluates
        // `.Where(t => t.IsActive)` as ordinary LINQ-to-objects regardless of whether Npgsql could ever
        // translate it; this proves the filter against a real database.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        db.Set<InvoiceFrequencyType>().AddRange(
            new InvoiceFrequencyType { Id = 1, TypeName = "Monthly", IsActive = true },
            new InvoiceFrequencyType { Id = 2, TypeName = "Retired", IsActive = false });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            "/api/compass/v1/invoice-frequencies", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassInvoiceFrequencyDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.ShouldHaveSingleItem();
        dtos[0].Id.ShouldBe(1);
        dtos[0].TypeName.ShouldBe("Monthly");
    }

    [Fact]
    public async Task GetClient_TranslatesTheDerivedStatusSubqueryAndTheInvoiceFrequencyInclude_AgainstRealPostgres()
    {
        // Arrange -- spec 009 Slice 3 (FR-005, FR-033, SC-009). The derived-status path is a
        // correlated AnyAsync subquery -- exactly the shape most likely to translate differently than
        // it evaluates against the InMemory provider, so this proves it against real PostgreSQL rather
        // than trusting CompassClientBoundaryTests' green run.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        const int invoiceFrequencyTypeId = 1;
        const int employeeTypeId = 1;
        const int employeeId = 1;
        const int activeClientId = 1;

        db.Set<InvoiceFrequencyType>().Add(
            new InvoiceFrequencyType { Id = invoiceFrequencyTypeId, TypeName = "Monthly", IsActive = true });
        db.Set<EmployeeType>().Add(
            new EmployeeType { Id = employeeTypeId, TypeName = "Full Time", IsActive = true });
        db.Set<Employee>().Add(new Employee
        {
            Id = employeeId,
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada.lovelace@example.test",
            StateOfResidence = "OH",
            EmployeeTypeId = employeeTypeId,
            HireDate = new DateOnly(2020, 1, 1),
            IsActive = true,
        });
        db.Set<Client>().Add(new Client
        {
            Id = activeClientId,
            ClientName = "Acme Corp",
            MsaSignedDate = new DateOnly(2022, 1, 1),
            NdaSignedDate = new DateOnly(2022, 1, 2),
            IsInternal = false,
            InvoiceFrequencyTypeId = invoiceFrequencyTypeId,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Set<ClientAssignment>().Add(new ClientAssignment
        {
            Id = 1,
            ClientId = activeClientId,
            EmployeeId = employeeId,
            StartDate = new DateOnly(2022, 1, 1),
            EndDate = null,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act -- proves both the Include(InvoiceFrequencyType) translates and the AnyAsync subquery
        // over ClientAssignments translates.
        var response = await client.GetAsync(
            $"/api/compass/v1/clients/{activeClientId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassClientDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.ClientName.ShouldBe("Acme Corp");
        dto.InvoiceFrequency.ShouldBe("Monthly");
        dto.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task GetClient_WithZeroAssignments_TranslatesTheSubqueryToInactive_AgainstRealPostgres()
    {
        // Arrange -- the zero-assignment case is exactly where a translation failure would hide: an
        // AnyAsync over an empty related set must translate to "false", not throw or drop the row.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        const int zeroAssignmentClientId = 2;

        db.Set<Client>().Add(new Client
        {
            Id = zeroAssignmentClientId,
            ClientName = "Brand New Client",
            IsInternal = false,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"/api/compass/v1/clients/{zeroAssignmentClientId}", TestContext.Current.CancellationToken);

        // Assert -- 200 with Status "Inactive", never a 404 or an error (spec 004 FR-034).
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassClientDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.Status.ShouldBe("Inactive");
        dto.InvoiceFrequency.ShouldBeNull();
    }

    [Fact]
    public async Task GetBillableCategories_TranslatesTheClientIdFilter_AgainstRealPostgres()
    {
        // Arrange -- spec 009 Slice 4 (FR-009, FR-033, SC-009). The unit suite's InMemory provider
        // evaluates `.Where(c => c.ClientId == clientId)` as ordinary LINQ-to-objects regardless of
        // whether Npgsql could ever translate it; this proves the filter against a real database, with
        // TWO clients each holding categories so the scoping is proven rather than merely the shape.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        const int firstClientId = 1;
        const int secondClientId = 2;

        db.Set<Client>().AddRange(
            new Client { Id = firstClientId, ClientName = "Acme Corp", IsInternal = false },
            new Client { Id = secondClientId, ClientName = "Globex Corp", IsInternal = false });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Set<BillableTimeCategory>().AddRange(
            new BillableTimeCategory
            {
                Id = 1,
                ClientId = firstClientId,
                CategoryName = "Development",
                IsActive = true,
            },
            new BillableTimeCategory
            {
                Id = 2,
                ClientId = firstClientId,
                CategoryName = "Legacy Support",
                IsActive = false,
            },
            new BillableTimeCategory
            {
                Id = 3,
                ClientId = secondClientId,
                CategoryName = "Other Client Category",
                IsActive = true,
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"/api/compass/v1/clients/{firstClientId}/billable-categories",
            TestContext.Current.CancellationToken);

        // Assert -- both the active AND inactive rows for the requested client come back, and the
        // OTHER client's category never leaks in.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassBillableCategoryDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.Count.ShouldBe(2);
        dtos.ShouldAllBe(dto => dto.ClientId == firstClientId);
        dtos.ShouldContain(dto => dto.CategoryName == "Development" && dto.IsActive);
        dtos.ShouldContain(dto => dto.CategoryName == "Legacy Support" && !dto.IsActive);
        dtos.ShouldNotContain(dto => dto.CategoryName == "Other Client Category");
    }

    /// <summary>
    /// Seeds the fixture spec 009 Slice 5's three assignment lookups share: one employee with two
    /// assignments (one at a client with a default cadence, one at a client with none), and one other
    /// employee whose single assignment carries its OWN override. This one seed exercises all three
    /// <c>EffectiveInvoiceFrequency</c> precedence cases (FR-012) and both by-employee/by-client
    /// scoping in the tests below.
    /// </summary>
    private async Task<(int EmployeeId, int OtherEmployeeId, int ClientWithDefaultId, int ClientWithNoDefaultId,
        int AssignmentWithOverrideId, int AssignmentUsingClientDefaultId, int AssignmentWithNoFrequencyId)>
        SeedAssignmentFixtureAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        const int employeeTypeId = 1;
        const int employeeId = 1;
        const int otherEmployeeId = 2;
        const int monthlyInvoiceFrequencyTypeId = 1;
        const int quarterlyInvoiceFrequencyTypeId = 2;
        const int clientWithDefaultId = 1;
        const int clientWithNoDefaultId = 2;
        const int assignmentWithOverrideId = 1;
        const int assignmentUsingClientDefaultId = 2;
        const int assignmentWithNoFrequencyId = 3;

        db.Set<InvoiceFrequencyType>().AddRange(
            new InvoiceFrequencyType
            { Id = monthlyInvoiceFrequencyTypeId, TypeName = "Monthly", IsActive = true },
            new InvoiceFrequencyType
            { Id = quarterlyInvoiceFrequencyTypeId, TypeName = "Quarterly", IsActive = true });

        db.Set<EmployeeType>().Add(
            new EmployeeType { Id = employeeTypeId, TypeName = "Full Time", IsActive = true });

        db.Set<Employee>().AddRange(
            new Employee
            {
                Id = employeeId,
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada.lovelace@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = employeeTypeId,
                HireDate = new DateOnly(2020, 1, 1),
                IsActive = true,
            },
            new Employee
            {
                Id = otherEmployeeId,
                FirstName = "Grace",
                LastName = "Hopper",
                Email = "grace.hopper@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = employeeTypeId,
                HireDate = new DateOnly(2020, 1, 1),
                IsActive = true,
            });

        db.Set<Client>().AddRange(
            new Client
            {
                Id = clientWithDefaultId,
                ClientName = "Acme Corp",
                IsInternal = false,
                InvoiceFrequencyTypeId = monthlyInvoiceFrequencyTypeId,
            },
            new Client
            {
                Id = clientWithNoDefaultId,
                ClientName = "Globex Corp",
                IsInternal = false,
            });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Set<ClientAssignment>().AddRange(
            new ClientAssignment
            {
                Id = assignmentWithOverrideId,
                EmployeeId = employeeId,
                ClientId = clientWithDefaultId,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = null,
                Note = "Key account",
                InvoiceFrequencyTypeId = quarterlyInvoiceFrequencyTypeId,
            },
            new ClientAssignment
            {
                Id = assignmentUsingClientDefaultId,
                EmployeeId = otherEmployeeId,
                ClientId = clientWithDefaultId,
                StartDate = new DateOnly(2022, 2, 1),
                EndDate = new DateOnly(2022, 12, 31),
            },
            new ClientAssignment
            {
                Id = assignmentWithNoFrequencyId,
                EmployeeId = employeeId,
                ClientId = clientWithNoDefaultId,
                StartDate = new DateOnly(2023, 1, 1),
                EndDate = null,
            });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (employeeId, otherEmployeeId, clientWithDefaultId, clientWithNoDefaultId,
            assignmentWithOverrideId, assignmentUsingClientDefaultId, assignmentWithNoFrequencyId);
    }

    /// <summary>
    /// Query shape 1 of 3 (spec 009 Slice 5, FR-006, FR-012, FR-033, SC-009): <c>GET /assignments/{id}</c>.
    /// Proves BOTH sides of the <c>EffectiveInvoiceFrequency</c> precedence translate against real
    /// PostgreSQL in one round trip — the assignment's OWN <c>InvoiceFrequencyType</c> include (the
    /// override) and the client's <c>InvoiceFrequencyType</c> include (the fall-through default) — the
    /// exact shape flagged as highest-risk for the projected-member trap: the InMemory provider
    /// used by the unit suite evaluates
    /// this as ordinary LINQ-to-objects regardless of whether Npgsql could ever translate it.
    /// </summary>
    [Fact]
    public async Task GetAssignmentById_TranslatesBothInvoiceFrequencyIncludes_AgainstRealPostgres()
    {
        // Arrange
        await ResetDatabaseAsync();
        var (_, _, _, _, assignmentWithOverrideId, _, _) = await SeedAssignmentFixtureAsync();
        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"/api/compass/v1/assignments/{assignmentWithOverrideId}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassAssignmentDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.ClientName.ShouldBe("Acme Corp");
        dto.Note.ShouldBe("Key account");

        // The override wins over the client's own default -- proves the assignment-level Include
        // translates and is preferred over the client-level one in the same query.
        dto.EffectiveInvoiceFrequency.ShouldBe("Quarterly");
    }

    /// <summary>
    /// Query shape 1 continued: the fall-through half of FR-012's precedence, proven separately so a
    /// translation that only handles the override case (e.g. an inner join that drops a row with no
    /// override) cannot hide behind the case above.
    /// </summary>
    [Fact]
    public async Task GetAssignmentById_WithNoOverride_TranslatesTheFallThroughToTheClientDefault_AgainstRealPostgres()
    {
        // Arrange
        await ResetDatabaseAsync();
        var (_, _, _, _, _, assignmentUsingClientDefaultId, _) = await SeedAssignmentFixtureAsync();
        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"/api/compass/v1/assignments/{assignmentUsingClientDefaultId}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassAssignmentDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.EffectiveInvoiceFrequency.ShouldBe("Monthly");
    }

    /// <summary>
    /// Query shape 1 continued: neither the assignment nor its client has a frequency set. Proves the
    /// double-nullable-navigation chain translates to <c>null</c>, not an error and not a dropped row.
    /// </summary>
    [Fact]
    public async Task GetAssignmentById_WhenNeitherIsSet_TranslatesToNull_AgainstRealPostgres()
    {
        // Arrange
        await ResetDatabaseAsync();
        var (_, _, _, _, _, _, assignmentWithNoFrequencyId) = await SeedAssignmentFixtureAsync();
        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"/api/compass/v1/assignments/{assignmentWithNoFrequencyId}",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassAssignmentDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.EffectiveInvoiceFrequency.ShouldBeNull();
    }

    /// <summary>
    /// Query shape 2 of 3: <c>GET /employees/{id}/assignments</c>. Proves the <c>Where(EmployeeId)</c>
    /// filter AND the ordering by the entity's OWN <c>StartDate</c> column both translate, scoped to
    /// only that employee's assignments.
    /// </summary>
    [Fact]
    public async Task GetAssignmentsByEmployee_TranslatesTheEmployeeFilterAndOrdering_AgainstRealPostgres()
    {
        // Arrange
        await ResetDatabaseAsync();
        var (employeeId, _, _, _, assignmentWithOverrideId, _, assignmentWithNoFrequencyId) =
            await SeedAssignmentFixtureAsync();
        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"/api/compass/v1/employees/{employeeId}/assignments", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassAssignmentDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.Count.ShouldBe(2);
        dtos.ShouldAllBe(dto => dto.EmployeeId == employeeId);
        dtos.ShouldContain(dto => dto.Id == assignmentWithOverrideId);
        dtos.ShouldContain(dto => dto.Id == assignmentWithNoFrequencyId);

        // Ordered by StartDate: the override assignment (2022-01-01) comes before the no-frequency one
        // (2023-01-01).
        dtos[0].Id.ShouldBe(assignmentWithOverrideId);
        dtos[1].Id.ShouldBe(assignmentWithNoFrequencyId);
    }

    /// <summary>
    /// Query shape 3 of 3: <c>GET /clients/{id}/assignments</c>. Proves the <c>Where(ClientId)</c>
    /// filter translates, scoped to only that client's assignments, and that the same
    /// <c>EffectiveInvoiceFrequency</c> precedence resolves correctly for two DIFFERENT employees at
    /// the same client -- one with an override, one without.
    /// </summary>
    [Fact]
    public async Task GetAssignmentsByClient_TranslatesTheClientFilter_AgainstRealPostgres()
    {
        // Arrange
        await ResetDatabaseAsync();
        var (_, _, clientWithDefaultId, _, assignmentWithOverrideId, assignmentUsingClientDefaultId, _) =
            await SeedAssignmentFixtureAsync();
        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"/api/compass/v1/clients/{clientWithDefaultId}/assignments",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassAssignmentDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.Count.ShouldBe(2);
        dtos.ShouldAllBe(dto => dto.ClientId == clientWithDefaultId);
        dtos.ShouldContain(dto => dto.Id == assignmentWithOverrideId && dto.EffectiveInvoiceFrequency == "Quarterly");
        dtos.ShouldContain(dto => dto.Id == assignmentUsingClientDefaultId && dto.EffectiveInvoiceFrequency == "Monthly");
    }

    /// <summary>
    /// Query shape 4 of 4 (spec 009 Slice 6, FR-007, FR-033, SC-009): <c>GET /assignments/{id}/sows</c>.
    /// Proves the <c>Where(ClientAssignmentId)</c> filter AND the ordering by the entity's OWN
    /// <c>SowStartDate</c> column both translate against real PostgreSQL, with all three
    /// <see cref="SowType"/> values represented in one assignment.
    /// </summary>
    [Fact]
    public async Task GetSowsByAssignment_TranslatesTheAssignmentFilterAndOrdering_AgainstRealPostgres()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        const int employeeTypeId = 1;
        const int employeeId = 1;
        const int clientId = 1;
        const int assignmentId = 1;
        const int otherAssignmentId = 2;

        db.Set<EmployeeType>().Add(
            new EmployeeType { Id = employeeTypeId, TypeName = "Full Time", IsActive = true });
        db.Set<Employee>().Add(new Employee
        {
            Id = employeeId,
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada.lovelace@example.test",
            StateOfResidence = "OH",
            EmployeeTypeId = employeeTypeId,
            HireDate = new DateOnly(2020, 1, 1),
            IsActive = true,
        });
        db.Set<Client>().Add(new Client { Id = clientId, ClientName = "Acme Corp", IsInternal = false });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Set<ClientAssignment>().AddRange(
            new ClientAssignment
            {
                Id = assignmentId,
                EmployeeId = employeeId,
                ClientId = clientId,
                StartDate = new DateOnly(2018, 1, 1),
                EndDate = null,
            },
            new ClientAssignment
            {
                Id = otherAssignmentId,
                EmployeeId = employeeId,
                ClientId = clientId,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = null,
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Deliberately inserted OUT of date order, so a passing OrderBy(SowStartDate) is proven rather
        // than a coincidental insertion order.
        db.Set<Sow>().AddRange(
            new Sow
            {
                Id = 1,
                ClientAssignmentId = assignmentId,
                SowType = SowType.SowExtension,
                SowStartDate = new DateOnly(2021, 1, 1),
                SowEndDate = new DateOnly(2021, 12, 31),
                RateIncrease = true,
                Note = "Renewal note",
                HasPassedApplicationValidation = true,
            },
            new Sow
            {
                Id = 2,
                ClientAssignmentId = assignmentId,
                SowType = SowType.InitialContract,
                SowStartDate = new DateOnly(2020, 1, 1),
                SowEndDate = new DateOnly(2020, 12, 31),
                RateIncrease = false,
                Note = null,
                HasPassedApplicationValidation = true,
            },
            new Sow
            {
                Id = 3,
                ClientAssignmentId = assignmentId,
                SowType = SowType.LegacyMigrated,
                SowStartDate = new DateOnly(2018, 1, 1),
                SowEndDate = new DateOnly(2018, 12, 31),
                RateIncrease = false,
                Note = null,
                HasPassedApplicationValidation = false,
            },
            // Belongs to a DIFFERENT assignment -- must never leak into the result below.
            new Sow
            {
                Id = 4,
                ClientAssignmentId = otherAssignmentId,
                SowType = SowType.InitialContract,
                SowStartDate = new DateOnly(2022, 1, 1),
                SowEndDate = new DateOnly(2022, 12, 31),
                RateIncrease = false,
                Note = null,
                HasPassedApplicationValidation = true,
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"/api/compass/v1/assignments/{assignmentId}/sows", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassDirectorySowDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.Count.ShouldBe(3);
        dtos.ShouldAllBe(dto => dto.ClientAssignmentId == assignmentId);
        dtos.ShouldNotContain(dto => dto.Id == 4);

        // Ordered by SowStartDate ascending, regardless of insertion order: LegacyMigrated (2018) ->
        // InitialContract (2020) -> SowExtension (2021).
        dtos[0].Id.ShouldBe(3);
        dtos[1].Id.ShouldBe(2);
        dtos[2].Id.ShouldBe(1);

        // CompassAdmin is the Elevated tier -- RateIncrease and Note are present.
        var extension = dtos.Single(dto => dto.Id == 1);
        extension.SowType.ShouldBe(nameof(SowType.SowExtension));
        extension.RateIncrease.ShouldBe(true);
        extension.Note.ShouldBe("Renewal note");
    }

    /// <summary>
    /// The empty-result half of query shape 4: an assignment with zero SOWs returns 200 with an empty
    /// list, never an error and never a 404 -- proving the filter translates to "no rows" rather than
    /// throwing when the related set is empty (the same zero-assignment shape
    /// <c>GetClient_WithZeroAssignments_TranslatesTheSubqueryToInactive_AgainstRealPostgres</c> proves
    /// for the client's derived status subquery).
    /// </summary>
    [Fact]
    public async Task GetSowsByAssignment_WhenAssignmentHasNoSows_TranslatesToAnEmptyList_AgainstRealPostgres()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        const int employeeTypeId = 1;
        const int employeeId = 1;
        const int clientId = 1;
        const int assignmentWithNoSowsId = 1;

        db.Set<EmployeeType>().Add(
            new EmployeeType { Id = employeeTypeId, TypeName = "Full Time", IsActive = true });
        db.Set<Employee>().Add(new Employee
        {
            Id = employeeId,
            FirstName = "Grace",
            LastName = "Hopper",
            Email = "grace.hopper@example.test",
            StateOfResidence = "OH",
            EmployeeTypeId = employeeTypeId,
            HireDate = new DateOnly(2020, 1, 1),
            IsActive = true,
        });
        db.Set<Client>().Add(new Client { Id = clientId, ClientName = "Acme Corp", IsInternal = false });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Set<ClientAssignment>().Add(new ClientAssignment
        {
            Id = assignmentWithNoSowsId,
            EmployeeId = employeeId,
            ClientId = clientId,
            StartDate = new DateOnly(2022, 1, 1),
            EndDate = null,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var client = CreateClientWithPrivileges(RolePolicy.CompassAdminRole);

        // Act
        var response = await client.GetAsync(
            $"/api/compass/v1/assignments/{assignmentWithNoSowsId}/sows",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassDirectorySowDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.ShouldBeEmpty();
    }
}
