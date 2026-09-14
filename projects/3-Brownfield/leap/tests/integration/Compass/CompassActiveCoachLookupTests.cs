using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// <see cref="ICompassEmployeeRepository.ActiveEmployeeExistsAsync"/> against real PostgreSQL — the
/// read backing issue #400's rule that a former EDJEr cannot be nominated as a coach.
/// </summary>
/// <remarks>
/// <para>
/// Why this cannot be a unit test. Every unit test of the coach rule runs against a hand-written
/// in-memory double, which answers from a <c>List</c> and so proves only that the SERVICE asks the right
/// question. It cannot establish that the query Npgsql generates runs at all — the failure mode
/// where a repository read translates fine under the
/// InMemory provider and answers HTTP 500 in every real environment.
/// </para>
/// <para>
/// The predicate here is deliberately simple, so the point of these cases is coverage of the boundary
/// rather than of the SQL: active is found, inactive is not, and absent is not.
/// </para>
/// </remarks>
public class CompassActiveCoachLookupTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ActiveEmployeeExists_FindsAnActiveEdjer()
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var activeId = await SeedEmployeeAsync(employeeTypeId, "active@example.test", isActive: true);

        // Act
        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICompassEmployeeRepository>();
        var found = await repository.ActiveEmployeeExistsAsync(activeId, Token);

        // Assert
        found.ShouldBeTrue();
    }

    [Fact]
    public async Task ActiveEmployeeExists_DoesNotFindAFormerEdjer_ThoughExistsAsyncStillDoes()
    {
        // Arrange — asserting BOTH reads on the same row is the point. The pair is what lets the service
        // tell "no such EDJEr" apart from "that EDJEr has left", which are different rejections.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        var formerId = await SeedEmployeeAsync(employeeTypeId, "former@example.test", isActive: false);

        // Act
        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICompassEmployeeRepository>();
        var active = await repository.ActiveEmployeeExistsAsync(formerId, Token);
        var exists = await repository.ExistsAsync(formerId, Token);

        // Assert
        active.ShouldBeFalse("a deactivated EDJEr is not an eligible coach");
        exists.ShouldBeTrue("but the row is still there — the two reads must not collapse into one");
    }

    [Fact]
    public async Task ActiveEmployeeExists_DoesNotFindAnAbsentId()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act
        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICompassEmployeeRepository>();
        var found = await repository.ActiveEmployeeExistsAsync(4_242_424, Token);

        // Assert
        found.ShouldBeFalse();
    }

    private async Task<int> SeedEmployeeTypeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employeeType = new EmployeeType { TypeName = "Full Time", IsActive = true };
        db.Set<EmployeeType>().Add(employeeType);
        await db.SaveChangesAsync(Token);

        return employeeType.Id;
    }

    private async Task<int> SeedEmployeeAsync(int employeeTypeId, string email, bool isActive)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employee = new Employee
        {
            FirstName = "Dana",
            LastName = "Prior",
            HireDate = new DateOnly(2020, 1, 6),
            Email = email,
            EmployeeTypeId = employeeTypeId,
            IsActive = isActive,
            StateOfResidence = "OH",
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        };

        db.Set<Employee>().Add(employee);
        await db.SaveChangesAsync(Token);

        return employee.Id;
    }
}
