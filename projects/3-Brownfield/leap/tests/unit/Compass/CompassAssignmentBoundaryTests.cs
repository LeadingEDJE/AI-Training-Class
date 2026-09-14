using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The published Compass Directory boundary's assignment reads — three lookups over one contract
/// (spec 009 Slice 5; FR-006, FR-012).
/// </summary>
/// <remarks>
/// <para>
/// Hits <c>/api/compass/v1/assignments/{id}</c>, <c>/api/compass/v1/employees/{id}/assignments</c> and
/// <c>/api/compass/v1/clients/{id}/assignments</c> over <see cref="TestWebApplicationFactory"/>, the
/// same HTTP surface a real out-of-process consumer would call.
/// </para>
/// <para>
/// The seed is built to exercise all three <c>EffectiveInvoiceFrequency</c> precedence cases in one
/// fixture (FR-012): an assignment-level override, a fall-through to the client's default, and
/// neither set. The unit suite's InMemory provider cannot prove these queries TRANSLATE — that is
/// <c>CompassDirectoryBoundaryTests</c>' job at the integration layer (T052) — this file only proves
/// the shape and the precedence rule evaluate correctly.
/// </para>
/// </remarks>
public class CompassAssignmentBoundaryTests : IClassFixture<TestWebApplicationFactory>
{
    private const int EmployeeTypeId = 1;
    private const int EmployeeWithTwoAssignmentsId = 1;
    private const int OtherEmployeeId = 2;

    private const int MonthlyInvoiceFrequencyTypeId = 1;
    private const int QuarterlyInvoiceFrequencyTypeId = 2;

    private const int ClientWithDefaultId = 1;
    private const int ClientWithNoDefaultId = 2;

    /// <summary>Employee 1 @ Client 1, override = Quarterly. Case (a): override wins.</summary>
    private const int AssignmentWithOverrideId = 1;

    /// <summary>Employee 2 @ Client 1, no override. Case (b): falls through to the client default.</summary>
    private const int AssignmentUsingClientDefaultId = 2;

    /// <summary>Employee 1 @ Client 2 (no default), no override. Case (c): neither set, null.</summary>
    private const int AssignmentWithNoFrequencyId = 3;

    private readonly TestWebApplicationFactory _factory;

    public CompassAssignmentBoundaryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        Seed(factory);
    }

    private static string ByIdRoute(int id) => $"/api/compass/v1/assignments/{id}";

    private static string ByEmployeeRoute(int employeeId) =>
        $"/api/compass/v1/employees/{employeeId}/assignments";

    private static string ByClientRoute(int clientId) => $"/api/compass/v1/clients/{clientId}/assignments";

    [Fact]
    public async Task GetById_ReturnsEmployeeClientDatesNoteAndEffectiveInvoiceFrequency()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(ByIdRoute(AssignmentWithOverrideId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassAssignmentDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.Id.ShouldBe(AssignmentWithOverrideId);
        dto.EmployeeId.ShouldBe(EmployeeWithTwoAssignmentsId);
        dto.ClientId.ShouldBe(ClientWithDefaultId);
        dto.ClientName.ShouldBe("Acme Corp");
        dto.StartDate.ShouldBe(new DateOnly(2022, 1, 1));
        dto.EndDate.ShouldBeNull();
        dto.Note.ShouldBe("Key account");
    }

    [Fact]
    public async Task GetById_WhenAssignmentDoesNotExist_Returns404()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(ByIdRoute(9_999), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetByEmployee_ReturnsOnlyThatEmployeesAssignments()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(
                ByEmployeeRoute(EmployeeWithTwoAssignmentsId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassAssignmentDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.Count.ShouldBe(2);
        dtos.ShouldAllBe(dto => dto.EmployeeId == EmployeeWithTwoAssignmentsId);
        dtos.ShouldContain(dto => dto.Id == AssignmentWithOverrideId);
        dtos.ShouldContain(dto => dto.Id == AssignmentWithNoFrequencyId);
        dtos.ShouldNotContain(dto => dto.Id == AssignmentUsingClientDefaultId);
    }

    [Fact]
    public async Task GetByEmployee_WhenEmployeeHasNoAssignments_ReturnsEmptyList_NotAnError()
    {
        // Act -- no not-found case: an unknown/assignment-less employee just has zero assignments,
        // matching the collection-GET precedent from Slices 2/4.
        var response = await _factory.AsCompassAdmin()
            .GetAsync(ByEmployeeRoute(9_999), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassAssignmentDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetByClient_ReturnsOnlyThatClientsAssignments()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(ByClientRoute(ClientWithDefaultId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassAssignmentDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.Count.ShouldBe(2);
        dtos.ShouldAllBe(dto => dto.ClientId == ClientWithDefaultId);
        dtos.ShouldContain(dto => dto.Id == AssignmentWithOverrideId);
        dtos.ShouldContain(dto => dto.Id == AssignmentUsingClientDefaultId);
        dtos.ShouldNotContain(dto => dto.Id == AssignmentWithNoFrequencyId);
    }

    [Fact]
    public async Task GetByClient_WhenClientHasNoAssignments_ReturnsEmptyList_NotAnError()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(ByClientRoute(9_999), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassAssignmentDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.ShouldBeEmpty();
    }

    /// <summary>
    /// FR-012 precedence case (a): an assignment-level override wins over the client's default.
    /// </summary>
    [Fact]
    public async Task EffectiveInvoiceFrequency_WhenAssignmentHasAnOverride_ReturnsTheOverridesName()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(ByIdRoute(AssignmentWithOverrideId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassAssignmentDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.EffectiveInvoiceFrequency.ShouldBe("Quarterly");
    }

    /// <summary>
    /// FR-012 precedence case (b): no override set, so the client's default name is used.
    /// </summary>
    [Fact]
    public async Task EffectiveInvoiceFrequency_WhenNoOverride_FallsThroughToTheClientsDefault()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(ByIdRoute(AssignmentUsingClientDefaultId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassAssignmentDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.EffectiveInvoiceFrequency.ShouldBe("Monthly");
    }

    /// <summary>
    /// FR-012 precedence case (c): neither the assignment nor its client has a frequency set. This is
    /// reported data (`null` present in the payload), never a withheld field and never an invented
    /// house default.
    /// </summary>
    [Fact]
    public async Task EffectiveInvoiceFrequency_WhenNeitherIsSet_IsNull()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(ByIdRoute(AssignmentWithNoFrequencyId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassAssignmentDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.EffectiveInvoiceFrequency.ShouldBeNull();
    }

    private static void Seed(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (context.Set<ClientAssignment>().Any())
        {
            return;
        }

        context.Set<InvoiceFrequencyType>().AddRange(
            new InvoiceFrequencyType
            { Id = MonthlyInvoiceFrequencyTypeId, TypeName = "Monthly", IsActive = true },
            new InvoiceFrequencyType
            { Id = QuarterlyInvoiceFrequencyTypeId, TypeName = "Quarterly", IsActive = true });

        context.Set<EmployeeType>().Add(
            new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });

        context.Set<Employee>().AddRange(
            new Employee
            {
                Id = EmployeeWithTwoAssignmentsId,
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada.lovelace@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = EmployeeTypeId,
                HireDate = new DateOnly(2020, 1, 1),
                IsActive = true,
            },
            new Employee
            {
                Id = OtherEmployeeId,
                FirstName = "Grace",
                LastName = "Hopper",
                Email = "grace.hopper@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = EmployeeTypeId,
                HireDate = new DateOnly(2020, 1, 1),
                IsActive = true,
            });

        context.Set<Client>().AddRange(
            new Client
            {
                Id = ClientWithDefaultId,
                ClientName = "Acme Corp",
                IsInternal = false,
                InvoiceFrequencyTypeId = MonthlyInvoiceFrequencyTypeId,
            },
            new Client
            {
                Id = ClientWithNoDefaultId,
                ClientName = "Globex Corp",
                IsInternal = false,
            });

        context.SaveChanges();

        context.Set<ClientAssignment>().AddRange(
            new ClientAssignment
            {
                Id = AssignmentWithOverrideId,
                EmployeeId = EmployeeWithTwoAssignmentsId,
                ClientId = ClientWithDefaultId,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = null,
                Note = "Key account",
                InvoiceFrequencyTypeId = QuarterlyInvoiceFrequencyTypeId,
            },
            new ClientAssignment
            {
                Id = AssignmentUsingClientDefaultId,
                EmployeeId = OtherEmployeeId,
                ClientId = ClientWithDefaultId,
                StartDate = new DateOnly(2022, 2, 1),
                EndDate = new DateOnly(2022, 12, 31),
                Note = null,
                InvoiceFrequencyTypeId = null,
            },
            new ClientAssignment
            {
                Id = AssignmentWithNoFrequencyId,
                EmployeeId = EmployeeWithTwoAssignmentsId,
                ClientId = ClientWithNoDefaultId,
                StartDate = new DateOnly(2023, 1, 1),
                EndDate = null,
                Note = null,
                InvoiceFrequencyTypeId = null,
            });

        context.SaveChanges();
    }
}
