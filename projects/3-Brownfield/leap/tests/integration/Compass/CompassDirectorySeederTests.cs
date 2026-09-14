using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.SeedData;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// The acceptance test for the Compass development fixture: the seeder's output must be accepted by
/// the LIVE <c>compass</c> schema with every constraint armed.
/// </summary>
/// <remarks>
/// <para>
/// Why this cannot be a unit test. The InMemory provider enforces no CHECK, no UNIQUE, no
/// foreign key and — critically — no <c>EXCLUDE USING gist</c>. The unit suite
/// (<c>tests/unit/Data/CompassDirectorySeederTests.cs</c>) asserts those invariants in code against
/// the produced graph, which catches a fixture mistake fast; only this suite proves Postgres agrees.
/// A seed that passes there and fails here is exactly the failure mode worth a Testcontainers run.
/// </para>
/// <para>
/// <see cref="Seeder_DoesNotSilentlyPassBecauseTheConstraintsAreAbsent"/> is the negative
/// control. A green "the insert succeeded" proves nothing unless the constraint would have
/// rejected a bad row, so that test forces a 23P01 on the same table in the same database.
/// </para>
/// </remarks>
public class CompassDirectorySeederTests(IntegrationTestFactory factory) : IntegrationTestBase(factory)
{
    private static readonly DateOnly Anchor = new(2026, 8, 7);

    private async Task<int[]> SeedAsync(DateOnly? anchor = null)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        await CompassDirectorySeeder.SeedAsync(context, anchor ?? Anchor, TestContext.Current.CancellationToken);
        return await CountsAsync();
    }

    private async Task<int[]> CountsAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var ct = TestContext.Current.CancellationToken;
        return
        [
            await context.Set<EmployeeType>().CountAsync(ct),
            await context.Set<InvoiceFrequencyType>().CountAsync(ct),
            await context.Set<Client>().CountAsync(ct),
            await context.Set<Employee>().CountAsync(ct),
            await context.Set<ClientAssignment>().CountAsync(ct),
            await context.Set<Sow>().CountAsync(ct),
            await context.Set<BillableTimeCategory>().CountAsync(ct),
        ];
    }

    [Fact]
    public async Task Seeder_PersistsEveryCompassTable_AgainstTheLiveSchema()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act — every CHECK, UNIQUE, FK and the gist exclusion constraint are armed here. A single
        // bad fixture row aborts the whole SaveChangesAsync, so reaching the assertions is itself
        // most of the result.
        var counts = await SeedAsync();

        // Assert
        counts.ShouldAllBe(c => c > 0);
        counts[3].ShouldBeGreaterThanOrEqualTo(90, "SC-006 measures p95 at ~90 EDJErs");
    }

    /// <summary>
    /// The fixture holds EDJErs on AND off the delivery team, so both states are reachable locally.
    /// </summary>
    /// <remarks>
    /// Both counts, deliberately. The column defaults to true, so "at least one is off the delivery
    /// team" would also be satisfied by a fixture that put EVERYONE off it — as wrong as the all-true
    /// state this replaces, and invisible to a one-sided assertion. The majority check pins the
    /// remaining half of the intent: off the delivery team is the minority, matching the default.
    /// </remarks>
    [Fact]
    public async Task Seeder_ProducesEdjErs_BothOnAndOffTheDeliveryTeam()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var ct = TestContext.Current.CancellationToken;

        // Act
        var onDelivery = await context.Set<Employee>().CountAsync(e => e.IsDeliveryTeam, ct);
        var offDelivery = await context.Set<Employee>().CountAsync(e => !e.IsDeliveryTeam, ct);

        // Assert
        offDelivery.ShouldBeGreaterThan(
            0, "a developer must be able to see the not-on-delivery state without editing a row");
        onDelivery.ShouldBeGreaterThan(
            0, "the on-delivery state must survive too — an all-false fixture is equally one-sided");
        offDelivery.ShouldBeLessThan(
            onDelivery, "off the delivery team is a named minority; the default is on it");
    }

    [Fact]
    public async Task Seeder_RunTwice_LeavesTheRowCountsUnchanged()
    {
        // Arrange
        await ResetDatabaseAsync();
        var first = await SeedAsync();

        // Act — the startup path calls this on every boot.
        var second = await SeedAsync();

        // Assert
        second.ShouldBe(first);
    }

    /// <summary>
    /// The OOTO persona engagements must reach a database that already holds the directory.
    /// </summary>
    /// <remarks>
    /// Every other test here seeds a virgin database, where the all-or-nothing directory guard runs
    /// and everything inside it lands. A developer's postgres volume survives <c>make dev-down</c>, so
    /// on their machine that guard is skipped and only a back-fill on its own sentinel arrives. The
    /// arrangement deletes exactly the rows the back-fill owns, which is what such a database looks
    /// like.
    /// </remarks>
    [Fact]
    public async Task Seeder_BackFillsTheOotoPersonaEngagements_OnAnAlreadySeededDatabase()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();
        var ct = TestContext.Current.CancellationToken;

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
            context.Set<ClientAssignment>().RemoveRange(
                await context.Set<ClientAssignment>()
                    .Where(a => a.ClientId == CompassDirectorySeeder.OotoPersonaClientId)
                    .ToListAsync(ct));
            context.Set<Client>().RemoveRange(
                await context.Set<Client>()
                    .Where(c => c.Id == CompassDirectorySeeder.OotoPersonaClientId)
                    .ToListAsync(ct));
            await context.SaveChangesAsync(ct);
        }

        // Act — the startup path, on a database that already has its employees.
        await SeedAsync();

        // Assert
        using var verify = Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<LeapDbContext>();
        var assignments = await db.Set<ClientAssignment>()
            .Where(a => a.ClientId == CompassDirectorySeeder.OotoPersonaClientId)
            .ToListAsync(ct);
        var assignees = await db.Set<Employee>()
            .Where(e => assignments.Select(a => a.EmployeeId).Contains(e.Id))
            .Select(e => e.Email)
            .ToListAsync(ct);

        (await db.Set<Client>()
            .AnyAsync(c => c.Id == CompassDirectorySeeder.OotoPersonaClientId, ct))
            .ShouldBeTrue("the back-fill must create the client it is the sentinel for");
        assignments.Count.ShouldBe(5, "one engagement per persona the legacy roster also holds");
        assignments.ShouldAllBe(a => a.EndDate == null);
        assignees.ShouldBe(
            [
                "alex.coach@example.test",
                "blair.dev@example.test",
                "casey.dev@example.test",
                "hank.deliver@example.test",
                "ivy.control@example.test",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Seeder_ResyncsIdentitySequences_SoTheNextApplicationInsertDoesNotCollide()
    {
        // Arrange — the seeder writes explicit IDs, which on Postgres does NOT advance the identity
        // sequence. Without the resync the first application insert fails with 23505.
        await ResetDatabaseAsync();
        await SeedAsync();

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var ct = TestContext.Current.CancellationToken;

        // Act — no explicit Id anywhere, exactly as an application insert would arrive.
        var client = new Client { ClientName = "Post-Seed Client", IsInternal = false };
        context.Set<Client>().Add(client);
        await context.SaveChangesAsync(ct);

        var employee = new Employee
        {
            FirstName = "Post",
            LastName = "Seed",
            Email = "post.seed@example.test",
            HireDate = Anchor,
            StateOfResidence = "OH",
            EmployeeTypeId = CompassDirectorySeeder.FullTimeEmployeeTypeId,
            IsActive = true,
        };
        context.Set<Employee>().Add(employee);
        await context.SaveChangesAsync(ct);

        // Assert — a generated key above every seeded key.
        client.Id.ShouldBeGreaterThan(28);
        employee.Id.ShouldBeGreaterThan(96);
    }

    [Fact]
    public async Task Seeder_ProducesTheDashboardBoundaryRows_ReadThroughSql()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var ct = TestContext.Current.CancellationToken;

        // Act — the "SOW expiring in under 90 days with no follow-on" tile, expressed the way the
        // feature expresses it, against the anchor date the fixture was built from. Issue #457: the
        // feature also requires the SOW's assignment to be OPEN-ENDED and started (a planned rollout's
        // SOW is not flagged), so the EXISTS on client_assignment mirrors the production predicate.
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (s.sow_end_date - @today) AS days_out
            FROM compass.sow s
            WHERE s.sow_end_date >= @today
              AND NOT EXISTS (
                    SELECT 1 FROM compass.sow later
                    WHERE later.client_assignment_id = s.client_assignment_id
                      AND later.sow_start_date > s.sow_end_date)
              AND EXISTS (
                    SELECT 1 FROM compass.client_assignment ca
                    WHERE ca.client_assignment_id = s.client_assignment_id
                      AND ca.end_date IS NULL
                      AND ca.start_date <= @today)
            ORDER BY days_out;
            """;
        command.Parameters.AddWithValue("today", Anchor);

        var daysOut = new List<int>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                daysOut.Add(reader.GetInt32(0));
            }
        }

        // Assert — 89 present (inside the window, open-ended assignment 12), 90 present (the boundary,
        // open-ended assignment 13), 45 absent (that assignment already has a follow-on SOW), and 52
        // absent because assignment 900 carries an end date (issue #457: a planned rollout's SOW is not
        // an expiring SOW, even though it is inside the window with no follow-on).
        daysOut.ShouldContain(89, "the under-90 case must be present");
        daysOut.ShouldContain(90, "the exactly-90 boundary must be present so the tile can exclude it");
        daysOut.ShouldNotContain(45, "the 45-day SOW has a follow-on and must not surface as expiring");
        daysOut.ShouldNotContain(
            52, "the 52-day SOW is on an end-dated (planned-rollout) assignment and must not surface (issue #457)");
    }

    [Fact]
    public async Task Seeder_DoesNotSilentlyPassBecauseTheConstraintsAreAbsent()
    {
        // Arrange — the negative control. Seed, then attempt a row the schema must reject.
        await ResetDatabaseAsync();
        await SeedAsync();

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var ct = TestContext.Current.CancellationToken;

        // Act — assignment 3 already holds a SOW spanning the year before the anchor. An overlapping
        // period on the SAME assignment must be refused by ex_sow_no_overlap_per_assignment.
        context.Set<Sow>().Add(new Sow
        {
            ClientAssignmentId = 3,
            SowStartDate = Anchor.AddMonths(-6),
            SowEndDate = Anchor.AddMonths(-3),
            SowType = SowType.SowExtension,
            RateIncrease = false,
        });

        // Assert — 23P01 is exclusion_violation. If this ever passes, the seed's green result above
        // means nothing and the constraint has been dropped.
        var overlap = await Should.ThrowAsync<DbUpdateException>(
            async () => await context.SaveChangesAsync(ct));
        ((PostgresException)overlap.InnerException!).SqlState.ShouldBe("23P01");

        context.ChangeTracker.Clear();

        // And the state CHECK, on a second armed constraint.
        context.Set<Employee>().Add(new Employee
        {
            FirstName = "Bad",
            LastName = "State",
            Email = "bad.state@example.test",
            HireDate = Anchor,
            StateOfResidence = "XX",
            EmployeeTypeId = CompassDirectorySeeder.FullTimeEmployeeTypeId,
        });

        var badState = await Should.ThrowAsync<DbUpdateException>(
            async () => await context.SaveChangesAsync(ct));
        ((PostgresException)badState.InnerException!).SqlState.ShouldBe("23514");
    }

    [Fact]
    public async Task LegacyMigratedSows_AreExemptFromBothDateRules_ButNothingElseIs()
    {
        // Arrange — FR-053. The two constraints are PARTIAL: scoped to skip LegacyMigrated so a TPS
        // load is admitted as-is, while every row Compass itself creates stays fully protected.
        await ResetDatabaseAsync();
        await SeedAsync();

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var ct = TestContext.Current.CancellationToken;

        // Act / Assert — a legacy period overlapping an existing one on assignment 3 is ACCEPTED.
        context.Set<Sow>().Add(new Sow
        {
            ClientAssignmentId = 3,
            SowStartDate = Anchor.AddMonths(-6),
            SowEndDate = Anchor.AddMonths(-3),
            SowType = SowType.LegacyMigrated,
            HasPassedApplicationValidation = false,
        });
        await context.SaveChangesAsync(ct);

        // A legacy period that ENDS BEFORE IT STARTS is also accepted at load.
        context.Set<Sow>().Add(new Sow
        {
            ClientAssignmentId = 3,
            SowStartDate = Anchor.AddYears(-9),
            SowEndDate = Anchor.AddYears(-10),
            SowType = SowType.LegacyMigrated,
            HasPassedApplicationValidation = false,
        });
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();

        // The SAME two shapes typed as a normal extension are REFUSED — the exemption is keyed on
        // the type, not switched off globally.
        context.Set<Sow>().Add(new Sow
        {
            ClientAssignmentId = 3,
            SowStartDate = Anchor.AddMonths(-6),
            SowEndDate = Anchor.AddMonths(-3),
            SowType = SowType.SowExtension,
        });
        var overlap = await Should.ThrowAsync<DbUpdateException>(async () => await context.SaveChangesAsync(ct));
        ((PostgresException)overlap.InnerException!).SqlState.ShouldBe("23P01");
        context.ChangeTracker.Clear();

        context.Set<Sow>().Add(new Sow
        {
            ClientAssignmentId = 5,
            SowStartDate = Anchor.AddYears(-9),
            SowEndDate = Anchor.AddYears(-10),
            SowType = SowType.InitialContract,
        });
        var backwards = await Should.ThrowAsync<DbUpdateException>(async () => await context.SaveChangesAsync(ct));
        ((PostgresException)backwards.InnerException!).SqlState.ShouldBe("23514");
    }

    [Fact]
    public async Task RateIncrease_IsAcceptedOnlyOnASowExtension()
    {
        // Arrange — FR-051 restated against the three-value type: the ERD's "extensions only" CHECK
        // survives, but LegacyMigrated does NOT inherit the permission.
        await ResetDatabaseAsync();
        await SeedAsync();

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var ct = TestContext.Current.CancellationToken;

        foreach (var refused in new[] { SowType.InitialContract, SowType.LegacyMigrated })
        {
            context.Set<Sow>().Add(new Sow
            {
                ClientAssignmentId = 5,
                SowStartDate = Anchor.AddYears(-20),
                SowEndDate = Anchor.AddYears(-19),
                SowType = refused,
                RateIncrease = true,
            });

            var ex = await Should.ThrowAsync<DbUpdateException>(async () => await context.SaveChangesAsync(ct));
            ((PostgresException)ex.InnerException!).SqlState.ShouldBe("23514", $"{refused} must not carry a rate increase");
            context.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task SowType_IsPersistedAsTheEnumName_AndConstrainedToTheThreeValues()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var ct = TestContext.Current.CancellationToken;

        // Act — read the raw column, not the mapped property.
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT sow_type FROM compass.sow ORDER BY sow_type;";
        var stored = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                stored.Add(reader.GetString(0));
            }
        }

        // Assert — enum NAMES, matching the TimesheetStatus convention, and all three in the fixture.
        stored.ShouldBe(["InitialContract", "LegacyMigrated", "SowExtension"]);

        // And a fourth value is refused by the value CHECK.
        await using var bad = connection.CreateCommand();
        bad.CommandText = """
            INSERT INTO compass.sow (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
            VALUES (5, 'Renewal', false, DATE '2020-01-01', DATE '2020-12-31', false);
            """;
        var ex = await Should.ThrowAsync<PostgresException>(async () => await bad.ExecuteNonQueryAsync(ct));
        ex.SqlState.ShouldBe("23514");
    }

    [Fact]
    public async Task Seeder_IsSafeToRunOnAnyDate_NotJustThePinnedAnchor()
    {
        // Arrange — startup seeds relative to whatever today is. Month-end and leap-day arithmetic in
        // the fixture must not produce a row the schema rejects.
        await ResetDatabaseAsync();

        // Act / Assert — each of these has bitten date arithmetic before: a 29 February anchor, the
        // 31st of a month whose neighbours are shorter, and a year boundary.
        foreach (var anchor in new[]
                 {
                     new DateOnly(2028, 2, 29),
                     new DateOnly(2026, 3, 31),
                     new DateOnly(2026, 12, 31),
                     new DateOnly(2027, 1, 1),
                 })
        {
            await ResetDatabaseAsync();
            var counts = await SeedAsync(anchor);
            counts.ShouldAllBe(c => c > 0, $"anchor {anchor:O} produced an empty table");
        }
    }
}
