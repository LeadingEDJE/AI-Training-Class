using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

/// <summary>
/// Fences <c>CompassSowRepository</c> against the one rule every Compass repository must honour: it
/// never calls <c>SaveChangesAsync</c> itself (Principle III). Mirrors
/// <see cref="CompassAssignmentRepositoryTests"/> for the SOW repository.
/// </summary>
/// <remarks>
/// The functional tests below use the InMemory provider directly (via
/// <see cref="TestWebApplicationFactory"/>'s <see cref="LeapDbContext"/>), matching
/// <see cref="CompassAssignmentRepositoryTests"/> — no service consumes this repository yet (see
/// <c>ICompassSowRepository</c>'s remarks), so this is otherwise unreachable at the unit level.
/// Real-PostgreSQL translation proof lives in
/// <c>tests/integration/Compass/CompassSowRepositoryTests.cs</c>.
/// </remarks>
public class CompassSowRepositoryTests
{
    private const string RelativePath = "api/Modules/Compass/Data/Repositories/CompassSowRepository.cs";
    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void CompassSowRepository_ContainsNoSaveChangesAsyncCall()
    {
        // Arrange
        var source = ReadRepositoryFile(RelativePath);

        // Assert
        source.ShouldNotContain(
            "SaveChangesAsync",
            customMessage: "CompassSowRepository must not persist — SaveChangesAsync belongs to the "
                + "service layer (Principle III), reached through IAuditService.LogAsync for an "
                + "audited write");
    }

    private static async Task<int> SeedAssignmentAsync(LeapDbContext context)
    {
        if (!await context.Set<EmployeeType>().AnyAsync(e => e.Id == EmployeeTypeId, Token))
        {
            context.Set<EmployeeType>().Add(new EmployeeType { Id = EmployeeTypeId, TypeName = "Full Time", IsActive = true });
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
        };
        var client = new Client { ClientName = $"Acme-{Guid.NewGuid():N}", IsInternal = false };
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
        return assignment.Id;
    }

    [Fact]
    public async Task GetByAssignmentIdAsync_ReturnsOnlyThatAssignmentsSows()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassSowRepository(context);
        var assignmentId = await SeedAssignmentAsync(context);
        var otherAssignmentId = await SeedAssignmentAsync(context);
        var sow = new Sow
        {
            ClientAssignmentId = assignmentId,
            SowStartDate = new DateOnly(2026, 1, 1),
            SowEndDate = new DateOnly(2026, 6, 30),
        };
        context.Set<Sow>().AddRange(
            sow,
            new Sow
            {
                ClientAssignmentId = otherAssignmentId,
                SowStartDate = new DateOnly(2026, 1, 1),
                SowEndDate = new DateOnly(2026, 6, 30),
            });
        await context.SaveChangesAsync(Token);

        // Act
        var rows = await repository.GetByAssignmentIdAsync(assignmentId, Token);

        // Assert
        rows.ShouldHaveSingleItem();
        rows[0].Id.ShouldBe(sow.Id);
    }

    /// <summary>Issue #632 — oldest contract start date first, regardless of insertion order.</summary>
    [Fact]
    public async Task GetByAssignmentIdAsync_OrdersBySowStartDate_OldestFirst()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassSowRepository(context);
        var assignmentId = await SeedAssignmentAsync(context);
        var newest = new Sow
        {
            ClientAssignmentId = assignmentId,
            SowStartDate = new DateOnly(2026, 7, 1),
            SowEndDate = new DateOnly(2026, 12, 31),
        };
        var oldest = new Sow
        {
            ClientAssignmentId = assignmentId,
            SowStartDate = new DateOnly(2025, 1, 1),
            SowEndDate = new DateOnly(2025, 6, 30),
        };
        var middle = new Sow
        {
            ClientAssignmentId = assignmentId,
            SowStartDate = new DateOnly(2026, 1, 1),
            SowEndDate = new DateOnly(2026, 6, 30),
        };
        // Inserted in a deliberately non-chronological order, so a passing assertion cannot be
        // explained by insertion or identifier order matching date order by coincidence.
        context.Set<Sow>().AddRange(newest, oldest, middle);
        await context.SaveChangesAsync(Token);

        // Act
        var rows = await repository.GetByAssignmentIdAsync(assignmentId, Token);

        // Assert
        rows.Select(row => row.Id).ShouldBe([oldest.Id, middle.Id, newest.Id]);
    }

    [Fact]
    public async Task GetByIdAsync_WhenTheSowDoesNotExist_ReturnsNull()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassSowRepository(context);

        // Act
        var found = await repository.GetByIdAsync(999_999, Token);

        // Assert
        found.ShouldBeNull();
    }

    [Fact]
    public async Task AddAsync_StagesTheSow_AndSaveChangesPersistsIt()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassSowRepository(context);
        var assignmentId = await SeedAssignmentAsync(context);
        var sow = new Sow
        {
            ClientAssignmentId = assignmentId,
            SowStartDate = new DateOnly(2026, 1, 1),
            SowEndDate = new DateOnly(2026, 6, 30),
        };

        // Act
        await repository.AddAsync(sow, Token);
        await context.SaveChangesAsync(Token);

        // Assert
        sow.Id.ShouldBeGreaterThan(0);
        var reloaded = await repository.GetByIdAsync(sow.Id, Token);
        reloaded.ShouldNotBeNull();
    }

    [Fact]
    public async Task RemoveAsync_StagesTheSow_AndSaveChangesRemovesIt()
    {
        // Arrange — issue #593.
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassSowRepository(context);
        var assignmentId = await SeedAssignmentAsync(context);
        var sow = new Sow
        {
            ClientAssignmentId = assignmentId,
            SowStartDate = new DateOnly(2026, 1, 1),
            SowEndDate = new DateOnly(2026, 6, 30),
        };
        context.Set<Sow>().Add(sow);
        await context.SaveChangesAsync(Token);
        var tracked = await repository.GetByIdAsync(sow.Id, Token);

        // Act
        await repository.RemoveAsync(tracked!, Token);
        await context.SaveChangesAsync(Token);

        // Assert
        (await repository.GetByIdAsync(sow.Id, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task GetOverlappingAsync_ExcludesTheGivenSowId_AndReturnsOnlyIntersectingPeriods()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassSowRepository(context);
        var assignmentId = await SeedAssignmentAsync(context);
        var overlapping = new Sow
        {
            ClientAssignmentId = assignmentId,
            SowStartDate = new DateOnly(2026, 3, 1),
            SowEndDate = new DateOnly(2026, 8, 31),
        };
        var nonOverlapping = new Sow
        {
            ClientAssignmentId = assignmentId,
            SowStartDate = new DateOnly(2027, 1, 1),
            SowEndDate = new DateOnly(2027, 6, 30),
        };
        context.Set<Sow>().AddRange(overlapping, nonOverlapping);
        await context.SaveChangesAsync(Token);

        // Act
        var rows = await repository.GetOverlappingAsync(
            assignmentId,
            candidateStartDate: new DateOnly(2026, 1, 1),
            candidateEndDate: new DateOnly(2026, 6, 30),
            excludingSowId: null,
            Token);
        var excludingSelf = await repository.GetOverlappingAsync(
            assignmentId,
            candidateStartDate: overlapping.SowStartDate,
            candidateEndDate: overlapping.SowEndDate,
            excludingSowId: overlapping.Id,
            Token);

        // Assert
        rows.ShouldHaveSingleItem();
        rows[0].Id.ShouldBe(overlapping.Id);
        excludingSelf.ShouldBeEmpty();
    }

    /// <summary>
    /// <c>GetAllAsync</c> returns every period, projected for the migration's verification read.
    /// </summary>
    /// <remarks>
    /// These two reads exist for feature 010's reconciliation — SC-002 requires every migrated
    /// relationship checked at 100%, which a create-only surface makes impossible in principle. They
    /// had no unit coverage, which is what put this file's assembly under the 98% backend floor.
    /// <para>
    /// What this does NOT prove: that GetAllAsync translates. It orders an anonymous
    /// projection and builds the DTO in memory precisely because ordering by a member of a
    /// constructed type is untranslatable by Npgsql — and the InMemory provider used here evaluates
    /// the whole tree in .NET, so it would pass either way. The translation proof is
    /// <c>tests/integration/Compass/CompassSowRepositoryTests.cs</c>' job.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task GetAllAsync_ProjectsEveryPeriod_OrderedById()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassSowRepository(context);
        var assignmentId = await SeedAssignmentAsync(context);
        context.Set<Sow>().AddRange(
            new Sow
            {
                ClientAssignmentId = assignmentId,
                SowType = SowType.InitialContract,
                SowStartDate = new DateOnly(2026, 1, 1),
                SowEndDate = new DateOnly(2026, 6, 30),
                Note = "first",
            },
            new Sow
            {
                ClientAssignmentId = assignmentId,
                SowType = SowType.SowExtension,
                RateIncrease = true,
                SowStartDate = new DateOnly(2026, 7, 1),
                SowEndDate = new DateOnly(2026, 12, 31),
            });
        await context.SaveChangesAsync(Token);

        // Act
        var rows = await repository.GetAllAsync(Token);

        // Assert — every field the DTO carries, because this projection is hand-written: a
        // transposed pair (RateIncrease for HasPassedApplicationValidation, say) would still return
        // the right NUMBER of rows and reconcile a migration against the wrong values.
        rows.Count.ShouldBe(2);
        rows.Select(row => row.Id).ShouldBe(rows.Select(row => row.Id).Order().ToList());

        var extension = rows.Single(row => row.SowType == SowType.SowExtension);
        extension.ClientAssignmentId.ShouldBe(assignmentId);
        extension.RateIncrease.ShouldBeTrue();
        extension.SowStartDate.ShouldBe(new DateOnly(2026, 7, 1));
        extension.SowEndDate.ShouldBe(new DateOnly(2026, 12, 31));
        extension.Note.ShouldBeNull();

        rows.Single(row => row.SowType == SowType.InitialContract).Note.ShouldBe("first");
    }

    [Fact]
    public async Task GetAsync_ReturnsTheProjectedPeriod()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassSowRepository(context);
        var assignmentId = await SeedAssignmentAsync(context);
        var sow = new Sow
        {
            ClientAssignmentId = assignmentId,
            SowType = SowType.LegacyMigrated,
            SowStartDate = new DateOnly(2024, 1, 1),
            SowEndDate = new DateOnly(2024, 12, 31),
            HasPassedApplicationValidation = false,
            Note = "Migrated from TPS.",
        };
        context.Set<Sow>().Add(sow);
        await context.SaveChangesAsync(Token);

        // Act
        var found = await repository.GetAsync(sow.Id, Token);

        // Assert — a migrated row specifically, because HasPassedApplicationValidation is the field
        // the spot-check reads to tell a legacy load from an application write, and false is the
        // only honest value for one admitted past both partial constraints.
        found.ShouldNotBeNull();
        found.Id.ShouldBe(sow.Id);
        found.SowType.ShouldBe(SowType.LegacyMigrated);
        found.HasPassedApplicationValidation.ShouldBeFalse();
        found.Note.ShouldBe("Migrated from TPS.");
    }

    [Fact]
    public async Task GetAsync_WhenThePeriodDoesNotExist_ReturnsNull()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassSowRepository(context);

        // Act
        var found = await repository.GetAsync(999_999, Token);

        // Assert — null rather than an empty projection, so the endpoint can answer 404.
        found.ShouldBeNull();
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
