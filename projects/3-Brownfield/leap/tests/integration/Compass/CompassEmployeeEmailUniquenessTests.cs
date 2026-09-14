using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// BR-9's email uniqueness, as the database enforces it: unique across all EDJErs, active and
/// inactive, compared case-insensitively and with surrounding whitespace trimmed.
/// </summary>
/// <remarks>
/// <para>
/// Only assertable against real PostgreSQL. The in-memory provider enforces no unique index at
/// all, so every assertion here would pass vacuously there. That is the same reason
/// <see cref="CompassUnitOfWorkTests"/> lives in this project.
/// </para>
/// <para>
/// Why the database and not only the service. The service pre-checks so it can name the
/// conflicting field in its rejection (FR-013), but a check-then-act cannot be the only guard: a
/// concurrent writer can commit between the check and the write, which the spec's own edge-case list
/// calls out. The index is the backstop, and these tests are what prove it is a *case-insensitive*
/// one rather than the plain <c>ux_employee_email</c> it replaced (research D-5, spec A-2).
/// </para>
/// <para>
/// The index is <c>ux_employee_email_ci</c> on <c>lower(btrim(email))</c>. It cannot be declared in
/// <c>EmployeeConfiguration</c> — EF Core's <c>HasIndex</c> takes properties, not expressions — so it
/// is raw SQL in <c>AddCompassEmployeeCaseInsensitiveEmailIndex</c>, and this file is the only thing
/// standing between that migration and a silent revert.
/// </para>
/// </remarks>
public class CompassEmployeeEmailUniquenessTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheEmailIndex_IsFunctionalOverLowerBtrim_NotAPlainColumnIndex()
    {
        // Arrange — read the shape from the live catalog rather than from the generated migration. A
        // migration file proves what was written; pg_indexes proves what is actually enforced.
        await ResetDatabaseAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(Token);

        // Act
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'compass' AND tablename = 'employee' AND indexname = 'ux_employee_email_ci';
            """;
        var definition = (string?)await command.ExecuteScalarAsync(Token);

        // Assert
        definition.ShouldNotBeNull("ux_employee_email_ci must exist on compass.employee");
        definition.ShouldContain("UNIQUE");
        definition.ShouldContain("lower");
        definition.ShouldContain("btrim");
    }

    [Fact]
    public async Task ThePlainEmailIndex_IsGone_SoOneIndexOwnsTheRule()
    {
        // Arrange — the replaced index must not survive alongside its replacement. Two indexes on the
        // same column is not merely redundant: the weaker one advertises that an exact-match collision
        // is the rule, which is the reading BR-9 rejects.
        await ResetDatabaseAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(Token);

        // Act
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'compass' AND tablename = 'employee' AND indexname = 'ux_employee_email';
            """;
        var surviving = Convert.ToInt32(await command.ExecuteScalarAsync(Token));

        // Assert
        surviving.ShouldBe(0, "ux_employee_email is replaced by ux_employee_email_ci, not joined by it");
    }

    [Theory]
    [InlineData("ada.lovelace@example.test", "Ada.Lovelace@example.test", "differing only by case")]
    [InlineData("ada.lovelace@example.test", "ADA.LOVELACE@EXAMPLE.TEST", "fully upper-cased")]
    [InlineData("ada.lovelace@example.test", "  ada.lovelace@example.test  ", "surrounded by whitespace")]
    [InlineData(
        "ada.lovelace@example.test",
        " Ada.Lovelace@example.test ",
        "differing by case AND whitespace"
    )]
    public async Task TheDatabase_RefusesASecondEmployee_WhoseEmailNormalisesToAnExistingOne(
        string first,
        string second,
        string because
    )
    {
        // Arrange
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        await SeedEmployeeAsync(employeeTypeId, first, isActive: true);

        // Act / Assert — the provider's own rejection, unwrapped: this test is about the index, and the
        // module's translation of it into CompassDuplicateKeyException is CompassUnitOfWorkTests'.
        var thrown = await Should.ThrowAsync<DbUpdateException>(
            () => SeedEmployeeAsync(employeeTypeId, second, isActive: true)
        );

        thrown.ShouldNotBeNull($"an email {because} must collide");
    }

    [Fact]
    public async Task TheDatabase_RefusesAnEmail_AlreadyHeldByAnINACTIVEEmployee()
    {
        // Arrange — BR-9's whole point, and the case most often missed. EDJErs are deactivated rather
        // than deleted (AC-NFR-6 records no termination date), so the index must NOT be filtered on
        // is_active: a filtered one would let a departed EDJEr's address be handed to someone else.
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();
        await SeedEmployeeAsync(employeeTypeId, "departed@example.test", isActive: false);

        // Act / Assert — and case-insensitively against the inactive row too, so the two rules compose
        // rather than one covering for the other.
        await Should.ThrowAsync<DbUpdateException>(
            () => SeedEmployeeAsync(employeeTypeId, "Departed@example.test", isActive: true)
        );
    }

    [Fact]
    public async Task TheDatabase_AcceptsTwoEmployees_WhoseEmailsGenuinelyDiffer()
    {
        // The positive control. Without it every assertion above could pass because the table refuses
        // all inserts, or because the seed helper is broken — the fail-open shape this repository has
        // been bitten by repeatedly.
        // Arrange
        await ResetDatabaseAsync();
        var employeeTypeId = await SeedEmployeeTypeAsync();

        // Act
        var first = await SeedEmployeeAsync(employeeTypeId, "ada@example.test", isActive: true);
        var second = await SeedEmployeeAsync(employeeTypeId, "grace@example.test", isActive: true);

        // Assert
        first.ShouldBeGreaterThan(0);
        second.ShouldNotBe(first);
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

    /// <summary>
    /// Inserts a minimal EDJEr with the given email, verbatim — no trimming and no case folding, so the
    /// database is the only thing normalising anything.
    /// </summary>
    private async Task<int> SeedEmployeeAsync(int employeeTypeId, string email, bool isActive)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employee = new Employee
        {
            FirstName = "Ada",
            LastName = "Lovelace",
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
