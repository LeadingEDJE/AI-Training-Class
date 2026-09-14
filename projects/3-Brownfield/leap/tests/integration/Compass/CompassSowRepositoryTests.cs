using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// Proves <see cref="CompassSowRepository"/> against a REAL Postgres row in <c>compass.sow</c> — no
/// service consumes this repository yet (see <see cref="ICompassSowRepository"/>'s remarks), so this
/// is currently the only coverage for its EF query bodies.
/// </summary>
public class CompassSowRepositoryTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private const int EmployeeTypeId = 1;

    private async Task<int> SeedAssignmentAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        if (!await db.Set<EmployeeType>().AnyAsync(TestContext.Current.CancellationToken))
        {
            db.Set<EmployeeType>().Add(new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        }

        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"ada-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        };
        var client = new Client { ClientName = $"Acme-{Guid.NewGuid():N}", IsInternal = false };
        db.Set<Employee>().Add(employee);
        db.Set<Client>().Add(client);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var assignment = new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = new DateOnly(2026, 1, 1),
        };
        db.Set<ClientAssignment>().Add(assignment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return assignment.Id;
    }

    private async Task<Sow> SeedSowAsync(int assignmentId, DateOnly startDate, DateOnly endDate)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var sow = new Sow
        {
            ClientAssignmentId = assignmentId,
            SowStartDate = startDate,
            SowEndDate = endDate,
            HasPassedApplicationValidation = true,
        };
        db.Set<Sow>().Add(sow);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return sow;
    }

    [Fact]
    public async Task GetByAssignmentIdAsync_ReturnsOnlyThatAssignmentsSows()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var otherAssignmentId = await SeedAssignmentAsync();
        var sow = await SeedSowAsync(assignmentId, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await SeedSowAsync(otherAssignmentId, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        using var scope = Services.CreateScope();
        var repository = new CompassSowRepository(scope.ServiceProvider.GetRequiredService<LeapDbContext>());

        // Act
        var rows = await repository.GetByAssignmentIdAsync(assignmentId, TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem();
        rows[0].Id.ShouldBe(sow.Id);
    }

    /// <summary>Issue #632 — oldest contract start date first, regardless of insertion order.</summary>
    [Fact]
    public async Task GetByAssignmentIdAsync_OrdersBySowStartDate_OldestFirst()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var newest = await SeedSowAsync(assignmentId, new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31));
        var oldest = await SeedSowAsync(assignmentId, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));
        var middle = await SeedSowAsync(assignmentId, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        using var scope = Services.CreateScope();
        var repository = new CompassSowRepository(scope.ServiceProvider.GetRequiredService<LeapDbContext>());

        // Act
        var rows = await repository.GetByAssignmentIdAsync(assignmentId, TestContext.Current.CancellationToken);

        // Assert
        rows.Select(row => row.Id).ShouldBe([oldest.Id, middle.Id, newest.Id]);
    }

    [Fact]
    public async Task GetByIdAsync_WhenTheSowExists_ReturnsIt()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var sow = await SeedSowAsync(assignmentId, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        using var scope = Services.CreateScope();
        var repository = new CompassSowRepository(scope.ServiceProvider.GetRequiredService<LeapDbContext>());

        // Act
        var found = await repository.GetByIdAsync(sow.Id, TestContext.Current.CancellationToken);

        // Assert
        found.ShouldNotBeNull();
        found.Id.ShouldBe(sow.Id);
    }

    [Fact]
    public async Task GetByIdAsync_WhenTheSowDoesNotExist_ReturnsNull()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var repository = new CompassSowRepository(scope.ServiceProvider.GetRequiredService<LeapDbContext>());

        // Act
        var found = await repository.GetByIdAsync(999_999, TestContext.Current.CancellationToken);

        // Assert
        found.ShouldBeNull();
    }

    [Fact]
    public async Task AddAsync_StagesTheSow_AndSaveChangesPersistsIt()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassSowRepository(db);
        var sow = new Sow
        {
            ClientAssignmentId = assignmentId,
            SowStartDate = new DateOnly(2026, 1, 1),
            SowEndDate = new DateOnly(2026, 6, 30),
        };

        // Act
        await repository.AddAsync(sow, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        sow.Id.ShouldBeGreaterThan(0);
        var reloaded = await repository.GetByIdAsync(sow.Id, TestContext.Current.CancellationToken);
        reloaded.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetOverlappingAsync_ReturnsOnlyPeriodsWhoseDatesIntersect()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var overlapping = await SeedSowAsync(assignmentId, new DateOnly(2026, 3, 1), new DateOnly(2026, 8, 31));
        await SeedSowAsync(assignmentId, new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30));
        using var scope = Services.CreateScope();
        var repository = new CompassSowRepository(scope.ServiceProvider.GetRequiredService<LeapDbContext>());

        // Act
        var rows = await repository.GetOverlappingAsync(
            assignmentId,
            candidateStartDate: new DateOnly(2026, 1, 1),
            candidateEndDate: new DateOnly(2026, 6, 30),
            excludingSowId: null,
            TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldHaveSingleItem();
        rows[0].Id.ShouldBe(overlapping.Id);
    }

    [Fact]
    public async Task GetOverlappingAsync_ExcludesTheGivenSowId_SoAPeriodDoesNotCollideWithItself()
    {
        // Arrange
        await ResetDatabaseAsync();
        var assignmentId = await SeedAssignmentAsync();
        var sow = await SeedSowAsync(assignmentId, new DateOnly(2026, 3, 1), new DateOnly(2026, 8, 31));
        using var scope = Services.CreateScope();
        var repository = new CompassSowRepository(scope.ServiceProvider.GetRequiredService<LeapDbContext>());

        // Act
        var rows = await repository.GetOverlappingAsync(
            assignmentId,
            candidateStartDate: sow.SowStartDate,
            candidateEndDate: sow.SowEndDate,
            excludingSowId: sow.Id,
            TestContext.Current.CancellationToken);

        // Assert
        rows.ShouldBeEmpty();
    }
}
