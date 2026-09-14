using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// Proves the Compass directory read against a REAL Postgres row in <c>compass.employee</c>.
/// </summary>
/// <remarks>
/// <para>
/// Repository and EF behaviour is proven against a real database rather than the InMemory provider —
/// the InMemory provider does not model relational behaviour (schemas, no-tracking semantics against
/// real SQL), so an InMemory-only proof would be weaker than it looks. That matters more than usual
/// here: the entity is mapped into a non-default schema, which the InMemory provider ignores
/// entirely.
/// </para>
/// <para>
/// The PERSON navigation these tests used to assert is gone with the re-point — the Compass employee
/// carries its own given and family names, so there is nothing to eagerly load there. **Amended for
/// spec 009 Slice 1**: the repository now also includes <c>EmployeeType</c> and the self-referencing
/// <c>Coach</c>, and that translation IS proven against real Postgres — by
/// <c>CompassDirectoryBoundaryTests</c>, not here, since it is the boundary's grown payload rather
/// than this file's basic-read scope.
/// </para>
/// </remarks>
public class CompassDirectoryRepositoryTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private const int EmployeeTypeId = 1;

    private async Task<int> SeedEmployeeAsync(
        int employeeId, string firstName, string lastName, bool isActive)
    {
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
            Email = $"compass-{employeeId}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = isActive,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return employeeId;
    }

    [Fact]
    public async Task GetEmployeeAsync_WhenRowExists_ReturnsTheCompassEmployee()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync(1, "Ada", "Lovelace", isActive: true);
        using var scope = Services.CreateScope();
        var repository = new CompassDirectoryRepository(
            scope.ServiceProvider.GetRequiredService<LeapDbContext>(),
            new ClientStatusDerivation(),
            new CompassBusinessDate(TimeProvider.System));

        // Act
        var employee = await repository.GetEmployeeAsync(
            employeeId, TestContext.Current.CancellationToken);

        // Assert
        employee.ShouldNotBeNull();
        employee.Id.ShouldBe(employeeId);
        employee.FirstName.ShouldBe("Ada");
        employee.LastName.ShouldBe("Lovelace");
    }

    [Fact]
    public async Task GetEmployeeAsync_WhenIdIsUnknown_ReturnsNull()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var repository = new CompassDirectoryRepository(
            scope.ServiceProvider.GetRequiredService<LeapDbContext>(),
            new ClientStatusDerivation(),
            new CompassBusinessDate(TimeProvider.System));

        // Act
        var employee = await repository.GetEmployeeAsync(
            9_999, TestContext.Current.CancellationToken);

        // Assert
        employee.ShouldBeNull();
    }

    [Fact]
    public async Task GetEmployeeAsync_DoesNotTrackTheReturnedEntity()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeId = await SeedEmployeeAsync(1, "Grace", "Hopper", isActive: false);
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassDirectoryRepository(
            context, new ClientStatusDerivation(), new CompassBusinessDate(TimeProvider.System));

        // Act
        var employee = await repository.GetEmployeeAsync(
            employeeId, TestContext.Current.CancellationToken);

        // Assert — a read-only boundary must not leave entities in the change tracker.
        employee.ShouldNotBeNull();
        context.Entry(employee).State.ShouldBe(
            EntityState.Detached, "the Compass directory read is AsNoTracking by construction");
        context.ChangeTracker.Entries<Employee>().ShouldBeEmpty();
    }
}
