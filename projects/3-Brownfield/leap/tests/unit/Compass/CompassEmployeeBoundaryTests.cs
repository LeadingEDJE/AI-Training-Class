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
/// The published Compass Directory boundary's profile read grows past the three members it shipped
/// with (spec 009 Slice 1; FR-004, FR-008).
/// </summary>
/// <remarks>
/// Hits <c>/api/compass/v1/employees/{id}</c> over <see cref="TestWebApplicationFactory"/>, the same
/// HTTP surface a real out-of-process consumer would call — this is the boundary's own transport, not
/// the application read surface <c>CompassEmployeeDetailEndpointsTests</c> exercises.
/// </remarks>
public class CompassEmployeeBoundaryTests : IClassFixture<TestWebApplicationFactory>
{
    private const int CoachId = 1;
    private const int EmployeeId = 2;

    private readonly TestWebApplicationFactory _factory;

    public CompassEmployeeBoundaryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        Seed(factory);
    }

    private static string Route(int id) => $"/api/compass/v1/employees/{id}";

    [Fact]
    public async Task Get_ReturnsHireDateEmployeeTypeStateOfResidenceAndTheResolvedCoach()
    {
        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(Route(EmployeeId), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompassEmployeeDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.HireDate.ShouldBe(new DateOnly(2020, 1, 15));
        dto.EmployeeType.ShouldBe("Full Time");
        dto.StateOfResidence.ShouldBe("OH");

        // The coach is id + display name only -- not a nested profile (FR-008), or the coach's own
        // coach would recurse.
        dto.Coach.ShouldNotBeNull();
        dto.Coach.Id.ShouldBe(CoachId);
        dto.Coach.DisplayName.ShouldBe("Cody Coach");
    }

    [Fact]
    public async Task Get_ReturnsTimeTrackingSettings_ForSuperAdminOnly()
    {
        // Act
        var response = await _factory.AsCompassSuperAdmin()
            .GetAsync(Route(EmployeeId), TestContext.Current.CancellationToken);

        // Assert
        var dto = await response.Content.ReadFromJsonAsync<CompassEmployeeDto>(
            TestContext.Current.CancellationToken);

        dto.ShouldNotBeNull();
        dto.TimeTracking.ShouldNotBeNull();
        dto.TimeTracking.TimesheetRequired.ShouldBeTrue();
        dto.TimeTracking.CanSubmitUnder40.ShouldBeFalse();
        dto.TimeTracking.IncludeInPayroll.ShouldBeTrue();
    }

    [Fact]
    public async Task Get_WithholdsTimeTrackingSettings_FromTheElevatedTier()
    {
        // Arrange -- CompassAdmin is the Elevated tier (AC-44 makes it read-only, not un-elevated)
        // and the only role below Super Admin that can reach this payload at all: AsBaselineEdjer
        // holds no Compass role and is refused with 403 before the handler runs
        // (CompassBoundaryAuthorizationTests), so it has nothing to prove about DTO-level
        // withholding -- asserting against its body here was vacuous, since a 403 body never
        // contains the field either way.

        // Act
        var response = await _factory.AsCompassAdmin()
            .GetAsync(Route(EmployeeId), TestContext.Current.CancellationToken);

        // Assert -- must actually reach the handler, or the payload assertion below is vacuous.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // ABSENT from the payload (SC-006), not present-and-null: a null would still
        // tell an under-privileged caller the field exists.
        json.ShouldNotContain("timeTracking", Case.Insensitive);
    }

    private static void Seed(TestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (context.Set<Employee>().Any())
        {
            return;
        }

        context.Set<EmployeeType>().Add(new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });

        context.Set<Employee>().AddRange(
            new Employee
            {
                Id = CoachId,
                FirstName = "Cody",
                LastName = "Coach",
                Email = "cody.coach@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = 1,
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
                EmployeeTypeId = 1,
                CoachEmployeeId = CoachId,
                HireDate = new DateOnly(2020, 1, 15),
                IsActive = true,
                TimesheetRequired = true,
                CanSubmitUnder40 = false,
                IncludeInPayroll = true,
            });

        context.SaveChanges();
    }
}
