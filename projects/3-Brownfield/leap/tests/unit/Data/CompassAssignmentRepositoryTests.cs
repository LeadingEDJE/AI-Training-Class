using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

/// <summary>
/// Fences <c>CompassAssignmentRepository</c> against the one rule every Compass repository must
/// honour: it never calls <c>SaveChangesAsync</c> itself (Principle III). Persistence belongs to the
/// service layer, reached through <c>IAuditService.LogAsync</c> for an audited write (research R-4).
/// </summary>
/// <remarks>
/// The functional tests below use the InMemory provider directly (via
/// <see cref="TestWebApplicationFactory"/>'s <see cref="LeapDbContext"/>) rather than only the HTTP
/// surface: <c>GetOpenAssignmentsByEmployeeAsync</c> (US7's future deactivation guard) has no endpoint
/// yet, so it is otherwise unreachable at the unit level — its business-rule coverage against real
/// PostgreSQL data still belongs to the integration project once US7 lands.
/// </remarks>
public class CompassAssignmentRepositoryTests
{
    private const string RelativePath =
        "api/Modules/Compass/Data/Repositories/CompassAssignmentRepository.cs";
    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void CompassAssignmentRepository_ContainsNoSaveChangesAsyncCall()
    {
        // Arrange
        var source = ReadRepositoryFile(RelativePath);

        // Assert
        source.ShouldNotContain(
            "SaveChangesAsync",
            customMessage: "CompassAssignmentRepository must not persist — SaveChangesAsync belongs to "
                + "the service layer (Principle III), reached through IAuditService.LogAsync for an "
                + "audited write");
    }

    [Fact]
    public async Task GetOpenAssignmentsByEmployeeAsync_ReturnsOnlyAssignmentsWithNoEndDate()
    {
        // Arrange — the FR-040/FR-041 blocker condition is the ABSENCE of an end date.
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassAssignmentRepository(context, new ClientStatusDerivation());

        context.Set<EmployeeType>().Add(new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"ada-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
        };
        var openClient = new Client { ClientName = "Open", IsInternal = false };
        var closedClient = new Client { ClientName = "Closed", IsInternal = false };
        context.Set<Employee>().Add(employee);
        context.Set<Client>().AddRange(openClient, closedClient);
        await context.SaveChangesAsync(Token);

        context.Set<ClientAssignment>().AddRange(
            new ClientAssignment
            {
                EmployeeId = employee.Id,
                ClientId = openClient.Id,
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = null,
            },
            new ClientAssignment
            {
                EmployeeId = employee.Id,
                ClientId = closedClient.Id,
                StartDate = new DateOnly(2025, 1, 1),
                EndDate = new DateOnly(2025, 12, 31),
            });
        await context.SaveChangesAsync(Token);

        // Act
        var open = await repository.GetOpenAssignmentsByEmployeeAsync(employee.Id, Token);

        // Assert
        open.ShouldHaveSingleItem();
        open[0].ClientId.ShouldBe(openClient.Id);
    }

    [Fact]
    public async Task GetEmployeeAsync_WithAnUnknownId_ReturnsNull()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassAssignmentRepository(context, new ClientStatusDerivation());

        // Act
        var employee = await repository.GetEmployeeAsync(999_999, Token);

        // Assert
        employee.ShouldBeNull();
    }

    // ------------------------------------------------------------------ US2 (#63) — pickers

    [Fact]
    public async Task GetClientPickerRowsAsync_ReturnsEveryClient_IncludingOneWithZeroAssignments()
    {
        // Arrange — Plan v7's named regression test O6: a client with no assignments derives
        // Inactive (BR-11) and must still be present and selectable (FR-009-FR-012).
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassAssignmentRepository(context, new ClientStatusDerivation());

        var engaged = new Client { ClientName = "Engaged" };
        var brandNew = new Client { ClientName = "Brand New — zero assignments" };
        context.Set<Client>().AddRange(engaged, brandNew);
        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"ada-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
        };
        context.Set<EmployeeType>().Add(new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        context.Set<Employee>().Add(employee);
        await context.SaveChangesAsync(Token);
        context.Set<ClientAssignment>().Add(new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = engaged.Id,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = null,
        });
        await context.SaveChangesAsync(Token);

        // Act
        var rows = await repository.GetClientPickerRowsAsync(new DateOnly(2026, 6, 1), Token);

        // Assert
        rows.ShouldContain(r => r.Id == brandNew.Id && r.ClientName == "Brand New — zero assignments");
        rows.ShouldContain(r => r.Id == engaged.Id);
        rows.Single(r => r.Id == brandNew.Id).DerivedStatus.ShouldBe("Inactive");
        rows.Single(r => r.Id == engaged.Id).DerivedStatus.ShouldBe("Active");
    }

    [Fact]
    public async Task GetActiveEdjerPickerRowsAsync_ExcludesInactiveEmployees()
    {
        // Arrange — FR-003: only an active EDJEr may receive a new assignment.
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassAssignmentRepository(context, new ClientStatusDerivation());

        context.Set<EmployeeType>().Add(new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        var active = new Employee
        {
            FirstName = "Active",
            LastName = "Edjer",
            Email = $"active-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
        };
        var inactive = new Employee
        {
            FirstName = "Inactive",
            LastName = "Edjer",
            Email = $"inactive-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = false,
            StateOfResidence = "OH",
        };
        context.Set<Employee>().AddRange(active, inactive);
        await context.SaveChangesAsync(Token);

        // Act
        var rows = await repository.GetActiveEdjerPickerRowsAsync(Token);

        // Assert
        rows.ShouldContain(r => r.Id == active.Id);
        rows.ShouldNotContain(r => r.Id == inactive.Id);
    }

    [Fact]
    public async Task RemoveAsync_StagesTheAssignment_AndSaveChangesRemovesIt()
    {
        // Arrange — issue #593.
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassAssignmentRepository(context, new ClientStatusDerivation());

        context.Set<EmployeeType>().Add(new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"ada-{Guid.NewGuid():N}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
        };
        var client = new Client { ClientName = "Acme" };
        context.Set<Employee>().Add(employee);
        context.Set<Client>().Add(client);
        await context.SaveChangesAsync(Token);

        var assignment = new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = new DateOnly(2026, 1, 1),
        };
        context.Set<ClientAssignment>().Add(assignment);
        await context.SaveChangesAsync(Token);
        var tracked = await repository.GetByIdAsync(assignment.Id, Token);

        // Act
        await repository.RemoveAsync(tracked!, Token);
        await context.SaveChangesAsync(Token);

        // Assert
        (await repository.GetByIdAsync(assignment.Id, Token)).ShouldBeNull();
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var root = RepositoryRoot();
        var absolute = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absolute))
        {
            throw new FileNotFoundException(
                $"The no-SaveChangesAsync check found nothing to inspect: '{relativePath}' does not "
                    + $"exist under repository root '{root}'. A missing file makes this check vacuous — "
                    + "fix the path rather than letting it pass for free.",
                absolute);
        }

        return File.ReadAllText(absolute);
    }

    private static string RepositoryRoot()
    {
        const string SolutionFile = "leap.slnx";

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate '{SolutionFile}' walking up from '{AppContext.BaseDirectory}'.");
    }
}
