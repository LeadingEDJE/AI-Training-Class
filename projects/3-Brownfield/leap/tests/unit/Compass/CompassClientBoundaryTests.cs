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
/// The published Compass Directory boundary's client read, with a status DERIVED at read time (spec
/// 009 Slice 3; FR-005).
/// </summary>
/// <remarks>
/// Hits <c>/api/compass/v1/clients/{id}</c> over <see cref="TestWebApplicationFactory"/>, the same
/// HTTP surface a real out-of-process consumer would call. Status is never stored on
/// <c>compass.client</c> — it is computed from the client's assignments through the single shared
/// <c>IClientStatusDerivation</c> (spec 004 BR-11) — so a client with no assignments must still come
/// back successfully, with status "Inactive" rather than a 404 or an error (spec 004 FR-034).
/// </remarks>
public class CompassClientBoundaryTests : IClassFixture<TestWebApplicationFactory>
{
    private const int InvoiceFrequencyTypeId = 1;
    private const int EmployeeTypeId = 1;
    private const int EmployeeId = 1;
    private const int ActiveClientId = 1;
    private const int ZeroAssignmentClientId = 2;

    private const int ActiveCategoryId = 1;
    private const int InactiveCategoryId = 2;
    private const int OtherClientCategoryId = 3;

    private readonly TestWebApplicationFactory _factory;

    public CompassClientBoundaryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        Seed(factory);
    }

    private static string Route(int id) => $"/api/compass/v1/clients/{id}";

    private static string BillableCategoriesRoute(int id) => $"/api/compass/v1/clients/{id}/billable-categories";

    [Fact]
    public async Task Get_ReturnsNameDatesInternalFlagInvoiceFrequencyNameAndDerivedStatus()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(Route(ActiveClientId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassClientDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.Id.ShouldBe(ActiveClientId);
        dto.ClientName.ShouldBe("Acme Corp");
        dto.MsaSignedDate.ShouldBe(new DateOnly(2022, 1, 1));
        dto.NdaSignedDate.ShouldBe(new DateOnly(2022, 1, 2));
        dto.IsInternal.ShouldBeFalse();

        // The resolved NAME, not the raw InvoiceFrequencyTypeId foreign key (data-model.md § 2).
        dto.InvoiceFrequency.ShouldBe("Monthly");

        // Holds a current, open-ended assignment -- Active.
        dto.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Get_WithNoInvoiceFrequencySet_ReturnsNullInvoiceFrequency()
    {
        // Act -- "none set" is a real state, not withheld data (data-model.md § 2).
        var response = await _factory.AsCompassAdmin()
            .GetAsync(Route(ZeroAssignmentClientId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassClientDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.InvoiceFrequency.ShouldBeNull();
    }

    [Fact]
    public async Task Get_WithZeroAssignments_ReturnsSuccessfully_WithInactiveStatus()
    {
        // Arrange -- status restricts nothing (spec 004 FR-034): a brand-new client with no
        // assignments is Inactive by the derivation's own case 1, and must still be returnable.

        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(Route(ZeroAssignmentClientId), TestContext.Current.CancellationToken);

        // Assert -- 200, never 404 or an error, and status is exactly "Inactive".
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassClientDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.Status.ShouldBe("Inactive");
    }

    [Fact]
    public async Task Get_WhenClientDoesNotExist_Returns404()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(Route(9_999), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Spec 009 Slice 4 (FR-009, read family 6): the billable-categories read returns EVERY category
    /// for the requested client -- active and inactive, each carrying its own flag -- and none
    /// belonging to a different client. Two clients are seeded with their own categories so the
    /// scoping is proven, not merely the shape.
    /// </summary>
    [Fact]
    public async Task GetBillableCategories_ReturnsEveryCategoryForThatClient_ActiveAndInactive_ScopedToTheClient()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(BillableCategoriesRoute(ActiveClientId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<CompassBillableCategoryDto>>(
            TestContext.Current.CancellationToken);

        dtos.ShouldNotBeNull();
        dtos.Count.ShouldBe(2);
        dtos.ShouldAllBe(dto => dto.ClientId == ActiveClientId);

        // Both active AND inactive rows come back, each with its OWN flag (the deliberate asymmetry
        // with CompassInvoiceFrequencyDto).
        dtos.ShouldContain(dto => dto.Id == ActiveCategoryId && dto.CategoryName == "Development" && dto.IsActive);
        dtos.ShouldContain(dto => dto.Id == InactiveCategoryId && dto.CategoryName == "Legacy Support" && !dto.IsActive);

        // Proves the scoping: the other client's category never leaks into this response.
        dtos.ShouldNotContain(dto => dto.Id == OtherClientCategoryId);
    }

    private static void Seed(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (context.Set<Client>().Any())
        {
            return;
        }

        context.Set<InvoiceFrequencyType>().Add(
            new InvoiceFrequencyType { Id = InvoiceFrequencyTypeId, TypeName = "Monthly", IsActive = true });

        context.Set<EmployeeType>().Add(
            new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });

        context.Set<Employee>().Add(new Employee
        {
            Id = EmployeeId,
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada.lovelace@example.test",
            StateOfResidence = "OH",
            EmployeeTypeId = EmployeeTypeId,
            HireDate = new DateOnly(2020, 1, 1),
            IsActive = true,
        });

        context.Set<Client>().AddRange(
            new Client
            {
                Id = ActiveClientId,
                ClientName = "Acme Corp",
                MsaSignedDate = new DateOnly(2022, 1, 1),
                NdaSignedDate = new DateOnly(2022, 1, 2),
                IsInternal = false,
                InvoiceFrequencyTypeId = InvoiceFrequencyTypeId,
            },
            new Client
            {
                Id = ZeroAssignmentClientId,
                ClientName = "Brand New Client",
                IsInternal = false,
            });

        context.SaveChanges();

        // Only the first client holds an assignment -- the second proves the zero-assignment case.
        context.Set<ClientAssignment>().Add(new ClientAssignment
        {
            Id = 1,
            ClientId = ActiveClientId,
            EmployeeId = EmployeeId,
            StartDate = new DateOnly(2022, 1, 1),
            EndDate = null,
        });

        // Two categories on the active client -- one active, one retired -- plus one on the OTHER
        // client, so the billable-categories read can be proven scoped rather than merely shaped.
        context.Set<BillableTimeCategory>().AddRange(
            new BillableTimeCategory
            {
                Id = ActiveCategoryId,
                ClientId = ActiveClientId,
                CategoryName = "Development",
                IsActive = true,
            },
            new BillableTimeCategory
            {
                Id = InactiveCategoryId,
                ClientId = ActiveClientId,
                CategoryName = "Legacy Support",
                IsActive = false,
            },
            new BillableTimeCategory
            {
                Id = OtherClientCategoryId,
                ClientId = ZeroAssignmentClientId,
                CategoryName = "Other Client Category",
                IsActive = true,
            });

        context.SaveChanges();
    }
}
