using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// Direct EF-Core unit tests for <see cref="CompassDirectoryRepository"/> over the InMemory provider,
/// following the house pattern established by the absorbed-directory repository tests.
/// </summary>
/// <remarks>
/// <para>
/// These now seed Compass's own entity. They previously seeded a Timesheet
/// <c>Person</c>/<c>Employee</c> pair, because the repository resolved against
/// <c>public.employees</c>. Feature 003 re-pointed it at <c>compass.employee</c>, and the legacy
/// tables are retired, so seeding them here would test a store this repository no longer reads.
/// </para>
/// <para>
/// These complement rather than replace the integration tests, which prove the same behaviour
/// against a real Postgres database. The InMemory provider does not model relational behaviour, so
/// the integration tests remain the authority on the actual query; these cover the member quickly
/// and are what the per-file coverage gate measures.
/// </para>
/// </remarks>
public class CompassDirectoryRepositoryTests
{
    private const int EmployeeTypeId = 1;

    private static LeapDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase($"CompassDirectoryRepoTestDb_{Guid.NewGuid():N}")
            .Options;
        return new LeapDbContext(options);
    }

    private static async Task<int> SeedAsync(
        LeapDbContext db, int employeeId, string first, string last, bool isActive)
    {
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
            FirstName = first,
            LastName = last,
            Email = $"{first}.{last}@example.test".ToLowerInvariant(),
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
    public async Task GetEmployeeAsync_WhenEmployeeExists_ReturnsIt()
    {
        // Arrange
        await using var db = CreateContext();
        var employeeId = await SeedAsync(db, 1, "Ada", "Lovelace", isActive: true);
        var repository = new CompassDirectoryRepository(
            db, new ClientStatusDerivation(), new CompassBusinessDate(TimeProvider.System));

        // Act
        var employee = await repository.GetEmployeeAsync(
            employeeId, TestContext.Current.CancellationToken);

        // Assert
        employee.ShouldNotBeNull();
        employee.Id.ShouldBe(employeeId);
        employee.FirstName.ShouldBe("Ada");
        employee.LastName.ShouldBe("Lovelace");
        employee.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task GetEmployeeAsync_WhenIdIsUnknown_ReturnsNull()
    {
        // Arrange
        await using var db = CreateContext();
        await SeedAsync(db, 1, "Grace", "Hopper", isActive: true);
        var repository = new CompassDirectoryRepository(
            db, new ClientStatusDerivation(), new CompassBusinessDate(TimeProvider.System));

        // Act
        var employee = await repository.GetEmployeeAsync(
            9_999, TestContext.Current.CancellationToken);

        // Assert
        employee.ShouldBeNull();
    }

    [Fact]
    public async Task GetEmployeeAsync_SelectsOnlyTheRequestedEmployee()
    {
        // Arrange — two employees, so the predicate is doing real work rather than returning "first".
        await using var db = CreateContext();
        await SeedAsync(db, 1, "Alan", "Turing", isActive: true);
        var wantedId = await SeedAsync(db, 2, "Katherine", "Johnson", isActive: false);
        var repository = new CompassDirectoryRepository(
            db, new ClientStatusDerivation(), new CompassBusinessDate(TimeProvider.System));

        // Act
        var employee = await repository.GetEmployeeAsync(
            wantedId, TestContext.Current.CancellationToken);

        // Assert
        employee.ShouldNotBeNull();
        employee.Id.ShouldBe(wantedId);
        employee.FirstName.ShouldBe("Katherine");
        employee.IsActive.ShouldBeFalse();
    }

    // ------------------------------------------------------------------ Collection reads (#430)

    /// <summary>
    /// Every client comes back with a status DERIVED from its assignments, never a stored one.
    /// </summary>
    /// <remarks>
    /// The three-valued split is what makes this worth asserting over the InMemory provider as well as
    /// the real one: a client that has never been assigned is Inactive, and one whose assignments have
    /// all ended is Former (issue #274). A set-wise derivation that dropped one of its two id
    /// projections would still answer "Active" correctly and collapse these two into each other.
    /// </remarks>
    [Fact]
    public async Task GetClientsAsync_ReturnsEveryClient_WithAStatusDerivedFromItsAssignments()
    {
        // Arrange
        await using var db = CreateContext();
        var employeeId = await SeedAsync(db, 1, "Ada", "Lovelace", isActive: true);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        db.Set<Client>().AddRange(
            new Client { Id = 1, ClientName = "Acme Corp", IsInternal = false },
            new Client { Id = 2, ClientName = "Globex Corp", IsInternal = false },
            new Client { Id = 3, ClientName = "Never Engaged Ltd", IsInternal = false });
        db.Set<ClientAssignment>().AddRange(
            new ClientAssignment
            {
                Id = 1,
                EmployeeId = employeeId,
                ClientId = 1,
                StartDate = today.AddYears(-1),
                EndDate = null,
            },
            new ClientAssignment
            {
                Id = 2,
                EmployeeId = employeeId,
                ClientId = 2,
                StartDate = today.AddYears(-3),
                EndDate = today.AddYears(-2),
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new CompassDirectoryRepository(
            db, new ClientStatusDerivation(), new CompassBusinessDate(TimeProvider.System));

        // Act
        var clients = await repository.GetClientsAsync(TestContext.Current.CancellationToken);

        // Assert — ordered by name, and all three statuses distinguished.
        clients.Select(c => c.Client.ClientName).ShouldBe(["Acme Corp", "Globex Corp", "Never Engaged Ltd"]);
        clients.Single(c => c.Client.Id == 1).Status.ShouldBe("Active");
        clients.Single(c => c.Client.Id == 2).Status.ShouldBe("Former");
        clients.Single(c => c.Client.Id == 3).Status.ShouldBe("Inactive");
    }

    [Fact]
    public async Task GetClientsAsync_WithNoClients_ReturnsEmpty()
    {
        // Arrange
        await using var db = CreateContext();
        var repository = new CompassDirectoryRepository(
            db, new ClientStatusDerivation(), new CompassBusinessDate(TimeProvider.System));

        // Act
        var clients = await repository.GetClientsAsync(TestContext.Current.CancellationToken);

        // Assert
        clients.ShouldBeEmpty();
    }
}
