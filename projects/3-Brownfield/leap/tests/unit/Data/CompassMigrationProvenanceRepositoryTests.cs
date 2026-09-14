using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

/// <summary>
/// <c>CompassMigrationProvenanceRepository</c> — the crosswalk rebuild's only read path.
/// </summary>
/// <remarks>
/// <para>
/// This file exists because the repository had ZERO unit coverage. Its five queries were
/// exercised only through the integration suite, and the backend coverage gate measures
/// <c>tests/unit</c> alone — so 40 uncovered lines sat in the report and dragged the whole assembly
/// under its 98% floor. Its two siblings, <see cref="CompassAssignmentRepositoryTests"/> and
/// <see cref="CompassSowRepositoryTests"/>, were written this way from the start; this one was
/// simply missed.
/// </para>
/// <para>
/// What this can and cannot prove. Against the InMemory provider these tests establish that
/// each of the five tables is read, that the provenance filter excludes rows without one, and that
/// the id paired with each identifier is the row's own. They establish NOTHING about translation —
/// InMemory evaluates the expression tree in .NET, so an untranslatable query passes here and 500s
/// against PostgreSQL. That proof is the integration suite's job, and the repository's own remarks
/// explain the projection shape it depends on.
/// </para>
/// </remarks>
public class CompassMigrationProvenanceRepositoryTests
{
    private const string RelativePath =
        "api/Modules/Compass/Data/Repositories/CompassMigrationProvenanceRepository.cs";

    private const int EmployeeTypeId = 1;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void CompassMigrationProvenanceRepository_ContainsNoSaveChangesAsyncCall()
    {
        // Arrange
        var source = ReadRepositoryFile(RelativePath);

        // Assert — the same Principle III fence its two siblings carry. This repository only reads,
        // so a SaveChangesAsync appearing here would be a change of kind, not of degree.
        source.ShouldNotContain(
            "SaveChangesAsync",
            customMessage: "CompassMigrationProvenanceRepository must not persist — "
                + "SaveChangesAsync belongs to the service layer (Principle III)"
        );
    }

    /// <summary>Seeds one row in every provenance-bearing table, carrying the given identifiers.</summary>
    /// <remarks>
    /// Every one of the five tables is seeded on every call, because the assertion that matters is
    /// that <c>GetAllAsync</c> reads ALL of them — a query accidentally dropped from the method
    /// would still pass a test that only seeded the tables it happens to read.
    /// </remarks>
    private static async Task<SeededProvenance> SeedAllFiveAsync(LeapDbContext context, string tag)
    {
        if (!await context.Set<EmployeeType>().AnyAsync(e => e.Id == EmployeeTypeId, Token))
        {
            context
                .Set<EmployeeType>()
                .Add(new EmployeeType
                {
                    Id = EmployeeTypeId,
                    TypeName = "Full Time",
                    IsActive = true,
                });
        }

        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"ada-{tag}@example.test",
            HireDate = new DateOnly(2020, 1, 1),
            EmployeeTypeId = EmployeeTypeId,
            IsActive = true,
            StateOfResidence = "OH",
            LegacyTpsId = $"tps-employee-{tag}",
        };

        var client = new Client
        {
            ClientName = $"Acme-{tag}",
            IsInternal = false,
            LegacyTpsId = $"tps-client-{tag}",
        };

        context.Set<Employee>().Add(employee);
        context.Set<Client>().Add(client);
        await context.SaveChangesAsync(Token);

        var assignment = new ClientAssignment
        {
            EmployeeId = employee.Id,
            ClientId = client.Id,
            StartDate = new DateOnly(2026, 1, 1),
            LegacyTpsId = $"tps-assignment-{tag}",
        };
        context.Set<ClientAssignment>().Add(assignment);

        var category = new BillableTimeCategory
        {
            ClientId = client.Id,
            CategoryName = $"Category-{tag}",
            IsActive = true,
            LegacyTpsId = $"tps-category-{tag}",
        };
        context.Set<BillableTimeCategory>().Add(category);
        await context.SaveChangesAsync(Token);

        var sow = new Sow
        {
            ClientAssignmentId = assignment.Id,
            SowStartDate = new DateOnly(2026, 1, 1),
            SowEndDate = new DateOnly(2026, 6, 30),
            LegacyTpsId = $"tps-sow-{tag}",
        };
        context.Set<Sow>().Add(sow);
        await context.SaveChangesAsync(Token);

        return new SeededProvenance(
            employee.Id,
            client.Id,
            assignment.Id,
            sow.Id,
            category.Id,
            tag
        );
    }

    private sealed record SeededProvenance(
        int EmployeeId,
        int ClientId,
        int AssignmentId,
        int SowId,
        int CategoryId,
        string Tag
    );

    [Fact]
    public async Task GetAllAsync_ReturnsAnEntryFromEveryProvenanceBearingTable()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassMigrationProvenanceRepository(context);
        var tag = Guid.NewGuid().ToString("N");
        var seeded = await SeedAllFiveAsync(context, tag);

        // Act
        var entries = await repository.GetAllAsync(Token);

        // Assert — one per table, each paired with its OWN row id. Asserting the id rather than only
        // the identifier is the point: these five queries are what a crosswalk rebuild trusts to say
        // "TPS x is Compass y", and a copy-paste slip pairing an identifier with the wrong table's
        // key would still return five entries.
        var byIdentifier = entries.ToDictionary(e => e.LegacyTpsId, e => e.CompassId);

        byIdentifier[$"tps-employee-{tag}"].ShouldBe(seeded.EmployeeId);
        byIdentifier[$"tps-client-{tag}"].ShouldBe(seeded.ClientId);
        byIdentifier[$"tps-assignment-{tag}"].ShouldBe(seeded.AssignmentId);
        byIdentifier[$"tps-sow-{tag}"].ShouldBe(seeded.SowId);
        byIdentifier[$"tps-category-{tag}"].ShouldBe(seeded.CategoryId);
    }

    [Fact]
    public async Task GetAllAsync_OmitsRowsCarryingNoProvenance()
    {
        // Arrange — an EDJEr and a client a PERSON created, which is the ordinary case: no TPS
        // origin, so nothing for a crosswalk to rebuild from.
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassMigrationProvenanceRepository(context);
        var tag = Guid.NewGuid().ToString("N");

        if (!await context.Set<EmployeeType>().AnyAsync(e => e.Id == EmployeeTypeId, Token))
        {
            context
                .Set<EmployeeType>()
                .Add(new EmployeeType
                {
                    Id = EmployeeTypeId,
                    TypeName = "Full Time",
                    IsActive = true,
                });
        }

        context
            .Set<Employee>()
            .Add(new Employee
            {
                FirstName = "Grace",
                LastName = "Hopper",
                Email = $"grace-{tag}@example.test",
                HireDate = new DateOnly(2021, 1, 1),
                EmployeeTypeId = EmployeeTypeId,
                IsActive = true,
                StateOfResidence = "OH",
            });

        context
            .Set<Client>()
            .Add(new Client { ClientName = $"Hand-Made-{tag}", IsInternal = false });

        await context.SaveChangesAsync(Token);

        // Act
        var entries = await repository.GetAllAsync(Token);

        // Assert — the filter is what keeps a rebuilt crosswalk honest. Without it every
        // hand-created record would arrive carrying a null identifier and the migration would treat
        // rows it never loaded as already loaded.
        entries.ShouldNotContain(entry => entry.LegacyTpsId.Contains(tag, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAllAsync_WhenNothingHasBeenMigrated_ReturnsEmpty()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var repository = new CompassMigrationProvenanceRepository(context);

        // Act
        var entries = await repository.GetAllAsync(Token);

        // Assert — empty, not an error. A first run reads this before it has written anything, so
        // the empty case is the NORMAL one rather than an edge.
        entries.ShouldBeEmpty();
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
                absolute
            );
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
            $"Could not locate '{SolutionFile}' walking up from '{AppContext.BaseDirectory}'."
        );
    }
}
