using System.Net;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The published Compass Directory boundary writes no audit entry for a read (spec 009 US2 T076;
/// FR-030, SC-008).
/// </summary>
/// <remarks>
/// <para>
/// The unit-level, HTTP-exercised counterpart to
/// <c>tests/integration/Compass/CompassAuditTests.CompassRead_ProducesNoAuditEntry</c>, which proves
/// the same rule for the in-process <c>IDirectory.GetEmployeeAsync</c> call only — not the eight
/// published HTTP routes this feature adds. Read auditing is explicitly out of scope for v1 (ADR-005)
/// and must not creep in; the audit trail covers writes only.
/// </para>
/// <para>
/// Every request below is made as a Compass Super Admin and every request SUCCEEDS (200).
/// A denied (401/403) request never reaches the handler an audit call would live in, and reading a
/// random/nonexistent id returns a not-found before the success branch either — both would let this
/// assertion pass vacuously even after someone added read auditing to the success path, which is
/// exactly the creep this file guards against. The seeded ids are real, so every one of the eight
/// routes returns 200, and that is asserted explicitly before the audit-count comparison is trusted.
/// </para>
/// </remarks>
public class CompassBoundaryAuditTests : IClassFixture<TestWebApplicationFactory>
{
    private const int EmployeeId = 1;

    private const int ClientId = 1;

    private const int AssignmentId = 1;

    private readonly TestWebApplicationFactory _factory;

    public CompassBoundaryAuditTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        Seed(factory);
    }

    private static readonly string[] AllRoutes =
    [
        $"/api/compass/v1/employees/{EmployeeId}",
        $"/api/compass/v1/employees/{EmployeeId}/assignments",
        $"/api/compass/v1/clients/{ClientId}",
        $"/api/compass/v1/clients/{ClientId}/billable-categories",
        $"/api/compass/v1/clients/{ClientId}/assignments",
        $"/api/compass/v1/assignments/{AssignmentId}",
        $"/api/compass/v1/assignments/{AssignmentId}/sows",
        "/api/compass/v1/invoice-frequencies",
    ];

    [Fact]
    public async Task ExercisingEveryPublishedBoundaryRoute_ProducesNoAuditLogEntry()
    {
        // Arrange -- non-vacuity guard: an empty route array would let this pass without exercising
        // anything.
        AllRoutes.Length.ShouldBe(
            8, "the published boundary has exactly eight routes; a shorter list here silently stops "
            + "exercising some of them");
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        int before = await context.AuditLogs.CountAsync(TestContext.Current.CancellationToken);

        // Act -- Compass Super Admin sees every field and every seeded id is real, so every one of the
        // eight requests reaches the success branch rather than a denial or a not-found.
        var client = _factory.AsCompassSuperAdmin();
        foreach (var route in AllRoutes)
        {
            var response = await client.GetAsync(route, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                $"{route} must actually succeed, or the audit-silence assertion below proves nothing");
        }

        // Assert
        int after = await context.AuditLogs.CountAsync(TestContext.Current.CancellationToken);
        after.ShouldBe(
            before, "a Compass Directory boundary read must write no audit entry (FR-030, SC-008)");
    }

    private static void Seed(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (!context.Set<Employee>().Any())
        {
            context.Set<EmployeeType>().Add(new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });
            context.Set<Employee>().Add(new Employee
            {
                Id = EmployeeId,
                FirstName = "Grace",
                LastName = "Hopper",
                Email = "grace.hopper@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = 1,
                HireDate = new DateOnly(2020, 1, 1),
                IsActive = true,
            });

            context.SaveChanges();
        }

        if (!context.Set<InvoiceFrequencyType>().Any())
        {
            context.Set<InvoiceFrequencyType>().Add(
                new InvoiceFrequencyType { Id = 1, TypeName = "Monthly", IsActive = true });

            context.SaveChanges();
        }

        if (!context.Set<Client>().Any())
        {
            context.Set<Client>().Add(new Client
            {
                Id = ClientId,
                ClientName = "Acme Corp",
                IsInternal = false,
            });

            context.SaveChanges();
        }

        if (!context.Set<ClientAssignment>().Any())
        {
            context.Set<ClientAssignment>().Add(new ClientAssignment
            {
                Id = AssignmentId,
                EmployeeId = EmployeeId,
                ClientId = ClientId,
                StartDate = new DateOnly(2022, 1, 1),
                EndDate = null,
            });

            context.SaveChanges();
        }
    }
}
