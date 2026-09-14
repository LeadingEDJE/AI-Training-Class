using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// The delivery-team flag on the EDJEr detail read, against real PostgreSQL (FR-004, SC-012).
/// </summary>
/// <remarks>
/// Nothing in the tree pins the exact members of <c>EmployeeDetailDto</c>, so a member that landed
/// inside the Super-Admin-only time-tracking group would compile, serialise and pass every other
/// suite while being invisible to every lesser tier. These tests are that guard: they read as the
/// baseline tier, which is the floor FR-004 promises.
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassEdjerDeliveryTeamReadTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassEdjerDeliveryTeamReadTests(IntegrationTestFactory factory) : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const int OnDeliveryId = 1;
    private const int OffDeliveryId = 2;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Route(int id) => $"/api/compass/team-directory/{id}";

    /// <summary>
    /// A baseline viewer reads the stored value, both ways round.
    /// </summary>
    /// <remarks>
    /// Both values, deliberately. The entity defaults to <c>true</c> and the DTO member to
    /// <c>false</c>, so a projection that never populates it answers <c>false</c> for everyone — the
    /// <c>false</c> case passes vacuously against it and only the <c>true</c> case can go red. One
    /// case alone would be a test that cannot fail.
    /// </remarks>
    [Theory]
    [InlineData(OnDeliveryId, true)]
    [InlineData(OffDeliveryId, false)]
    public async Task ABaselineViewer_ReadsTheStoredDeliveryTeamFlag(int employeeId, bool expected)
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsBaseEdjErOnly().GetAsync(Route(employeeId), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
        detail.ShouldNotBeNull();
        detail.IsDeliveryTeam.ShouldBe(
            expected,
            "the delivery-team flag must reach the baseline tier from the detail read (FR-004)");
    }

    /// <summary>
    /// The detail projection still translates to SQL, and carries the flag, at every tier.
    /// </summary>
    /// <remarks>
    /// The only job here is that the query runs. The in-memory provider in <c>tests/unit</c>
    /// evaluates the projection as ordinary LINQ-to-Objects whatever Npgsql can render, so an
    /// untranslatable shape passes every unit test and answers 500 in the wild. All three tiers,
    /// because the time-tracking branch compiles a different projection and only one of them is
    /// exercised by a baseline read -- and the flag is asserted here, not only in the baseline
    /// theory, because a projection correct at the floor and inverted above it would ship green.
    /// </remarks>
    [Fact]
    public async Task TheEdjerDetailRead_Translates_AtEveryTier()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        HttpClient[] viewers =
        [
            _factory.AsBaseEdjErOnly(),
            _factory.AsCompassAdmin(),
            _factory.AsCompassSuperAdmin(),
        ];

        // Act / Assert
        foreach (var viewer in viewers)
        {
            var response = await viewer.GetAsync(Route(OnDeliveryId), Token);

            response.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                "the employee detail projection must render to SQL -- a 500 here is the "
                + "untranslatable-shape defect");
            var detail = await response.Content.ReadFromJsonAsync<EmployeeDetailDto>(Token);
            detail.ShouldNotBeNull();
            detail.Id.ShouldBe(OnDeliveryId);
            detail.IsDeliveryTeam.ShouldBeTrue(
                "every tier that can open the record reads the stored flag, not just the "
                    + "baseline (FR-004)");
        }
    }

    /// <summary>One EDJEr on the delivery team, one off it, both active so a baseline viewer can open them.</summary>
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        context.Set<EmployeeType>().Add(new EmployeeType { Id = 1, TypeName = "Full Time", IsActive = true });
        context.Set<Employee>().AddRange(
            new Employee
            {
                Id = OnDeliveryId,
                FirstName = "Dee",
                LastName = "Livery",
                Email = "dee.livery@example.test",
                StateOfResidence = "OH",
                EmployeeTypeId = 1,
                HireDate = new DateOnly(2021, 3, 1),
                IsActive = true,
                IsDeliveryTeam = true,
            },
            new Employee
            {
                Id = OffDeliveryId,
                FirstName = "Bea",
                LastName = "Ackoffice",
                Email = "bea.ackoffice@example.test",
                StateOfResidence = "CA",
                EmployeeTypeId = 1,
                HireDate = new DateOnly(2019, 7, 15),
                IsActive = true,
                IsDeliveryTeam = false,
            });

        await context.SaveChangesAsync(Token);
    }
}
