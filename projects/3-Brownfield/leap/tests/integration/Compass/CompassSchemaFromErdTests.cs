using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// Verifies the EDJE Compass schema built from the companion ERD (issue #50) against the LIVE
/// Postgres catalog and the LIVE constraint behaviour — not against the generated migration file.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is deliberately raw, schema-qualified SQL. The point of issue #50 is the
/// constraints the DATABASE enforces, so asserting them through EF would prove the wrong thing —
/// EF would be validating its own model rather than the catalog. `docs/platform/adding-a-module.md`
/// § 3 step 4 is explicit that reading generated C# is not verification; these tests are the
/// automated form of that instruction.
/// </para>
/// <para>
/// ADR-003 is why these are Postgres constraints at all. The ERD's original 2026-07-22
/// revision was written for MySQL 8.0 and said of SOW non-overlap: "MySQL cannot enforce this
/// declaratively — the application layer must enforce it." Postgres can, with an exclusion
/// constraint over a <c>daterange</c>, so the integrity rule that revision had to delegate to
/// application code is enforced by the database here. That is the "constraints the DB engine will
/// not auto-apply" line in #50, discharged rather than deferred. The current revision
/// (2026-08-10, PR #199) was regenerated from this shipped schema and now records the rule as the
/// <c>EXCLUDE USING gist</c> constraint these tests assert.
/// </para>
/// <para>
/// SOW type is a three-value enum, and migration provenance IS tracked (owner decision,
/// 2026-08-07). The 2026-07-22 ERD's boolean <c>is_extension</c> was superseded by
/// <c>sow_type</c> (<c>InitialContract</c>, <c>SowExtension</c>, <c>LegacyMigrated</c>), persisted
/// as the enum name. An earlier revision removed the third value on the grounds that one field
/// could not record both "extends a prior period" and "came from TPS"; the premise fails in the
/// other direction, because legacy TPS tracks no extensions at all, so a migrated row is never an
/// extension. The current ERD documents <c>sow_type</c> and
/// <c>has_passed_application_validation</c> directly, so <c>sow</c> is no longer a departure from
/// the document — see <see cref="ErdDataDictionary"/>.
/// </para>
/// <para>
/// Two of the constraints below are therefore PARTIAL. The non-overlap rule and the
/// end-after-start rule skip <c>LegacyMigrated</c>, so a TPS load is admitted exactly as the legacy
/// system recorded it (FR-053) while every period Compass creates stays fully protected. Postgres
/// accepts a <c>WHERE</c> clause on both an exclusion constraint and a CHECK — and does not even
/// evaluate the indexed expression for a row the predicate excludes, which is what lets a backwards
/// date range load rather than erroring on range construction.
/// <c>Sow_AcceptsTheBulkLegacyMigrationShape</c> pins that load path;
/// <c>Sow_RejectsOverlappingPeriodsWithinOneAssignment</c> pins that the exemption is keyed on the
/// type rather than switched off.
/// </para>
/// </remarks>
public class CompassSchemaFromErdTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private const string CompassSchema = "compass";

    /// <summary>Postgres SQLSTATEs, so a failure names the rule that fired rather than a number.</summary>
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string ExclusionViolation = "23P01";
    private const string NotNullViolation = "23502";
    private const string ForeignKeyViolation = "23503";

    private static readonly string[] ExpectedTables =
    [
        "billable_time_category",
        "client",
        "client_assignment",
        "employee",
        "employee_type",
        "invoice_frequency_type",
        "sow",
    ];

    // ---- catalog shape -------------------------------------------------------------------------

    [Fact]
    public async Task CompassSchema_ContainsExactlyTheErdTables()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var tables = await context
            .Database.SqlQueryRaw<string>(
                $@"SELECT table_name AS ""Value"" FROM information_schema.tables
                   WHERE table_schema = '{CompassSchema}' ORDER BY table_name;"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        // NOTE: the (expected, message) overload binds to Shouldly's `Case` parameter for string
        // collections, so the explicit ignoreOrder form is required to attach a message at all.
        tables.ShouldBe(
            ExpectedTables,
            ignoreOrder: false,
            customMessage: "the compass schema must hold exactly the ERD's seven tables — an extra "
                + "table is speculative modelling, a missing one is an unimplemented ERD entity"
        );
    }

    [Fact]
    public async Task CompassTables_AreNotAlsoCreatedInPublic()
    {
        // Arrange — guards the failure mode where ToTable's schema argument is silently ignored and
        // everything lands in `public` alongside the timesheet tables.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var strays = await context
            .Database.SqlQueryRaw<string>(
                @"SELECT table_name AS ""Value"" FROM information_schema.tables
                  WHERE table_schema = 'public'
                    AND table_name IN ('employee','employee_type','invoice_frequency_type','client',
                                       'billable_time_category','client_assignment','sow');"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        strays.ShouldBeEmpty("compass tables belong in the compass schema, not public");
    }

    [Fact]
    public async Task CompassColumns_AreSnakeCase()
    {
        // Arrange — the explicit ToTable(name, schema) mapping is the first in this repository, and
        // whether it fights UseSnakeCaseNamingConvention() was unverified before this test existed.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var offenders = await context
            .Database.SqlQueryRaw<string>(
                $@"SELECT (table_name || '.' || column_name) AS ""Value""
                   FROM information_schema.columns
                   WHERE table_schema = '{CompassSchema}' AND column_name <> lower(column_name);"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        offenders.ShouldBeEmpty(
            "every compass column must be snake_case; a PascalCase column means the explicit table "
                + "mapping suppressed the naming convention"
        );
    }

    /// <summary>
    /// Walks every <c>table.column</c> pair the ERD publishes against the live catalog, in both
    /// directions (task 002-T008).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tests above assert the table set and the shape of individual columns the ERD calls out.
    /// Neither notices a column that exists in the database and appears nowhere in the ERD, nor one
    /// the ERD publishes that was never built. Both directions are failures, and for opposite reasons:
    /// an undocumented column is a contract other streams cannot see, and a missing one is an
    /// unimplemented part of a contract they can.
    /// </para>
    /// <para>
    /// The count assertion is not redundant. A both-direction set comparison against an empty
    /// expectation passes — twice, cleanly, and for no reason. That is the fail-open shape this
    /// repository has found in eight gates, so the pair count is asserted against a constant that is
    /// declared rather than derived. See <see cref="ErdDataDictionary.ExpectedPairCount"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CompassColumns_MatchTheErdDataDictionaryExactly()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var live = await context
            .Database.SqlQueryRaw<string>(
                $@"SELECT (table_name || '.' || column_name) AS ""Value""
                   FROM information_schema.columns
                   WHERE table_schema = '{CompassSchema}'
                   ORDER BY table_name, column_name;"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert — the expectation is non-empty and internally consistent BEFORE it is compared,
        // so a truncated or emptied list fails here rather than sailing through the comparison.
        ErdDataDictionary.ExpectedPairCount.ShouldBeGreaterThan(
            0,
            "an empty ERD expectation satisfies every comparison below and asserts nothing"
        );
        ErdDataDictionary.Pairs.Length.ShouldBe(
            ErdDataDictionary.ExpectedPairCount,
            "ErdDataDictionary.Pairs and ExpectedPairCount disagree — one was edited without the "
                + "other, so the transcription is no longer a reviewed statement of the ERD's shape"
        );

        var undocumented = live.Except(ErdDataDictionary.Pairs).Order().ToList();
        var missing = ErdDataDictionary.Pairs.Except(live).Order().ToList();

        undocumented.ShouldBeEmpty(
            "these columns exist in the compass schema but appear in no ERD data dictionary entry — "
                + "either add them to the ERD or drop them as speculative modelling (Principle II)"
        );
        missing.ShouldBeEmpty(
            "the ERD publishes these columns but the database has no such column — either the ERD "
                + "describes an unimplemented entity or a column was renamed without updating it"
        );
        live.Count.ShouldBe(
            ErdDataDictionary.ExpectedPairCount,
            "the compass schema must hold exactly the ERD's published columns"
        );
    }

    [Fact]
    public async Task Employee_CoachIsASelfReferencingNullableForeignKey()
    {
        // Arrange — the ERD's one self-reference: an EDJEr's coach is another EDJEr, UI-required but
        // DB-nullable for the top of the org chart.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var selfRefs = await context
            .Database.SqlQueryRaw<int>(
                $@"SELECT count(*)::int AS ""Value""
                   FROM information_schema.table_constraints tc
                   JOIN information_schema.constraint_column_usage ccu
                     ON tc.constraint_name = ccu.constraint_name
                    AND tc.table_schema = ccu.table_schema
                   WHERE tc.constraint_type = 'FOREIGN KEY'
                     AND tc.table_schema = '{CompassSchema}'
                     AND tc.table_name = 'employee'
                     AND ccu.table_name = 'employee';"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        var coachNullable = await context
            .Database.SqlQueryRaw<string>(
                $@"SELECT is_nullable AS ""Value"" FROM information_schema.columns
                   WHERE table_schema = '{CompassSchema}' AND table_name = 'employee'
                     AND column_name = 'coach_employee_id';"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        selfRefs.ShouldHaveSingleItem();
        selfRefs[0].ShouldBe(1, "employee.coach_employee_id must be a self-referencing FK");
        coachNullable.ShouldHaveSingleItem();
        coachNullable[0].ShouldBe("YES", "the top of the org chart has no coach");
    }

    [Fact]
    public async Task Employee_StoresNeitherTerminationDateNorBillingRate()
    {
        // Arrange — AC-NFR-6 states these are NOT stored. A negative requirement needs a test or it
        // silently decays the first time someone "completes" the model.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var forbidden = await context
            .Database.SqlQueryRaw<string>(
                $@"SELECT (table_name || '.' || column_name) AS ""Value""
                   FROM information_schema.columns
                   WHERE table_schema = '{CompassSchema}'
                     AND (column_name LIKE '%termination%'
                          OR column_name LIKE '%rate%' AND column_name <> 'rate_increase');"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        forbidden.ShouldBeEmpty(
            "AC-NFR-6: termination dates and billing rates are not stored (rate_increase is a "
                + "yes/no indicator, not a rate)"
        );
    }

    // ---- constraints the ERD implies -----------------------------------------------------------

    [Fact]
    public async Task ClientAssignment_RejectsEndDateBeforeStartDate()
    {
        var ctx = await SeedGraphAsync();

        var ex = await ThrowsPostgresAsync(
            ctx,
            $@"INSERT INTO ""compass"".""client_assignment""
                   (employee_id, client_id, start_date, end_date)
               VALUES ({ctx.EmployeeId}, {ctx.ClientId}, DATE '2026-06-01', DATE '2026-05-01');"
        );

        ex.SqlState.ShouldBe(CheckViolation, "CHECK: end_date >= start_date");
    }

    [Fact]
    public async Task ClientAssignment_AllowsNullEndDateAsOpenEnded()
    {
        var ctx = await SeedGraphAsync();

        // Act — the ERD is explicit: NULL end_date means open-ended, and must remain insertable.
        await ExecAsync(
            ctx,
            $@"INSERT INTO ""compass"".""client_assignment""
                   (employee_id, client_id, start_date, end_date)
               VALUES ({ctx.EmployeeId}, {ctx.ClientId}, DATE '2026-01-01', NULL);"
        );

        var rows = await CountAsync(
            ctx,
            @"SELECT count(*)::int AS ""Value"" FROM ""compass"".""client_assignment""
              WHERE end_date IS NULL;"
        );
        rows.ShouldBe(1);
    }

    [Fact]
    public async Task Sow_RejectsEndDateBeforeStartDate()
    {
        var ctx = await SeedGraphAsync(withAssignment: true);

        var ex = await ThrowsPostgresAsync(
            ctx,
            $@"INSERT INTO ""compass"".""sow""
                   (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
               VALUES ({ctx.AssignmentId}, 'InitialContract', false, DATE '2026-06-01', DATE '2026-05-01', true);"
        );

        ex.SqlState.ShouldBe(CheckViolation, "CHECK: sow_end_date >= sow_start_date");
    }

    [Fact]
    public async Task Sow_RejectsRateIncreaseOnANonExtension()
    {
        var ctx = await SeedGraphAsync(withAssignment: true);

        // The ERD: "rate_increase — Extensions only (CHECK-enforced)".
        var ex = await ThrowsPostgresAsync(
            ctx,
            $@"INSERT INTO ""compass"".""sow""
                   (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
               VALUES ({ctx.AssignmentId}, 'InitialContract', true, DATE '2026-01-01', DATE '2026-06-30', true);"
        );

        ex.SqlState.ShouldBe(CheckViolation, "CHECK: rate_increase implies sow_type = SowExtension");
    }

    [Fact]
    public async Task Sow_RejectsOverlappingPeriodsWithinOneAssignment()
    {
        var ctx = await SeedGraphAsync(withAssignment: true);

        await ExecAsync(
            ctx,
            $@"INSERT INTO ""compass"".""sow""
                   (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
               VALUES ({ctx.AssignmentId}, 'InitialContract', false, DATE '2026-01-01', DATE '2026-06-30', true);"
        );

        var ex = await ThrowsPostgresAsync(
            ctx,
            $@"INSERT INTO ""compass"".""sow""
                   (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
               VALUES ({ctx.AssignmentId}, 'SowExtension', false, DATE '2026-06-30', DATE '2026-12-31', true);"
        );

        ex.SqlState.ShouldBe(
            ExclusionViolation,
            "SOW periods must not overlap within one assignment — inclusive bounds, so a shared "
                + "boundary date IS an overlap"
        );
    }

    [Fact]
    public async Task Sow_AllowsAdjacentPeriodsAndGapsWithinOneAssignment()
    {
        var ctx = await SeedGraphAsync(withAssignment: true);

        await ExecAsync(
            ctx,
            $@"INSERT INTO ""compass"".""sow""
                   (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
               VALUES ({ctx.AssignmentId}, 'InitialContract', false, DATE '2026-01-01', DATE '2026-06-30', true);"
        );

        // Adjacent (starts the day after) and a later gapped period must both be accepted — the ERD
        // allows gaps explicitly.
        await ExecAsync(
            ctx,
            $@"INSERT INTO ""compass"".""sow""
                   (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
               VALUES ({ctx.AssignmentId}, 'SowExtension', true, DATE '2026-07-01', DATE '2026-12-31', true);"
        );
        await ExecAsync(
            ctx,
            $@"INSERT INTO ""compass"".""sow""
                   (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
               VALUES ({ctx.AssignmentId}, 'SowExtension', false, DATE '2027-03-01', DATE '2027-08-31', true);"
        );

        var rows = await CountAsync(
            ctx,
            @"SELECT count(*)::int AS ""Value"" FROM ""compass"".""sow"";"
        );
        rows.ShouldBe(3, "adjacent and gapped SOW periods are both legal");
    }

    [Fact]
    public async Task Sow_AllowsOverlapAcrossDifferentAssignments()
    {
        // Arrange — non-overlap is scoped PER ASSIGNMENT. A constraint that ignored the scope would
        // pass every test above and wrongly reject two EDJErs on concurrent contracts.
        var ctx = await SeedGraphAsync(withAssignment: true);
        var secondAssignment = await ScalarAsync(
            ctx,
            $@"INSERT INTO ""compass"".""client_assignment""
                   (employee_id, client_id, start_date, end_date)
               VALUES ({ctx.EmployeeId}, {ctx.ClientId}, DATE '2026-01-01', NULL)
               RETURNING client_assignment_id AS ""Value"";"
        );

        await ExecAsync(
            ctx,
            $@"INSERT INTO ""compass"".""sow""
                   (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
               VALUES ({ctx.AssignmentId}, 'InitialContract', false, DATE '2026-01-01', DATE '2026-12-31', true);"
        );

        // Act — the SAME period under a DIFFERENT assignment.
        await ExecAsync(
            ctx,
            $@"INSERT INTO ""compass"".""sow""
                   (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
               VALUES ({secondAssignment}, 'InitialContract', false, DATE '2026-01-01', DATE '2026-12-31', true);"
        );

        var rows = await CountAsync(
            ctx,
            @"SELECT count(*)::int AS ""Value"" FROM ""compass"".""sow"";"
        );
        rows.ShouldBe(2, "the non-overlap rule is scoped to one assignment");
    }







    [Fact]
    public async Task Sow_AcceptsTheBulkLegacyMigrationShape()
    {
        // Arrange — the shape the product owner described for the TPS bulk load: each closed
        // assignment gets EXACTLY ONE SOW, never an extension, because legacy TPS has no
        // initial-versus-extension concept. Every such row loads as LegacyMigrated (2026-08-07), so
        // this pins the real load path end to end.
        var ctx = await SeedGraphAsync(withAssignment: true);
        var secondAssignment = await ScalarAsync(
            ctx,
            $@"INSERT INTO ""compass"".""client_assignment""
                   (employee_id, client_id, start_date, end_date)
               VALUES ({ctx.EmployeeId}, {ctx.ClientId}, DATE '2024-01-01', DATE '2024-12-31')
               RETURNING client_assignment_id AS ""Value"";"
        );

        // Act — one LegacyMigrated SOW per assignment, in a single bulk statement, arriving
        // unvalidated. The two periods deliberately OVERLAP each other in time; they belong to
        // different assignments, which the non-overlap rule must not reject regardless of type.
        await ExecAsync(
            ctx,
            $@"INSERT INTO ""compass"".""sow""
                   (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
               VALUES ({ctx.AssignmentId}, 'LegacyMigrated', false, DATE '2024-03-01', DATE '2025-02-28', false),
                      ({secondAssignment}, 'LegacyMigrated', false, DATE '2024-01-01', DATE '2024-12-31', false);"
        );

        // And the rows TPS cannot be relied on to have kept clean: a second period on an assignment
        // that overlaps the first, and one whose end precedes its start. Both database rules are
        // partial and skip LegacyMigrated, so the load is admitted as recorded (FR-053) rather than
        // failing partway and leaving the migration half-applied.
        await ExecAsync(
            ctx,
            $@"INSERT INTO ""compass"".""sow""
                   (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
               VALUES ({ctx.AssignmentId}, 'LegacyMigrated', false, DATE '2024-06-01', DATE '2025-06-30', false),
                      ({secondAssignment}, 'LegacyMigrated', false, DATE '2024-11-30', DATE '2024-06-30', false);"
        );

        // Assert
        var rows = await CountAsync(
            ctx,
            @"SELECT count(*)::int AS ""Value"" FROM ""compass"".""sow""
              WHERE sow_type = 'LegacyMigrated' AND NOT rate_increase
                AND NOT has_passed_application_validation;"
        );
        rows.ShouldBe(
            4,
            "every TPS period must load as an unvalidated LegacyMigrated row, including the "
                + "overlapping and backwards ones the legacy system permitted"
        );
    }

    [Fact]
    public async Task Employee_RejectsANonUsStateCode()
    {
        var ctx = await SeedGraphAsync();

        // AC-NFR-6: only US-based EDJErs are supported.
        var ex = await ThrowsPostgresAsync(
            ctx,
            $@"INSERT INTO ""compass"".""employee""
                   (first_name, last_name, hire_date, email, employee_type_id, is_active,
                    state_of_residence, timesheet_required, can_submit_under_40, include_in_payroll)
               VALUES ('Ada','Byron', DATE '2026-01-05','ada.byron@leadingedje.com',
                       {ctx.EmployeeTypeId}, true, 'ZZ', true, false, true);"
        );

        ex.SqlState.ShouldBe(CheckViolation, "state_of_residence must be a US state code");
    }

    [Fact]
    public async Task Employee_TimezoneIsNotNullable()
    {
        var ctx = await SeedGraphAsync();

        // FR-8.1 — timezone is REQUIRED on compass.employee. An explicit NULL must be refused, which
        // is what makes "every EDJEr has a timezone" a property of the database rather than a habit
        // of the code that happens to write one.
        var ex = await ThrowsPostgresAsync(
            ctx,
            $@"INSERT INTO ""compass"".""employee""
                   (first_name, last_name, hire_date, email, employee_type_id, is_active,
                    state_of_residence, timesheet_required, can_submit_under_40, include_in_payroll,
                    timezone)
               VALUES ('Ada','Byron', DATE '2026-01-05','ada.byron@leadingedje.com',
                       {ctx.EmployeeTypeId}, true, 'OH', true, false, true, NULL);"
        );

        ex.SqlState.ShouldBe(NotNullViolation, "timezone must be required");
    }

    [Fact]
    public async Task Employee_TimezoneDefaultsToEasternWhenTheInsertOmitsIt()
    {
        var ctx = await SeedGraphAsync();

        // EXPAND/CONTRACT. Schema is applied by a PRE-ROLLOUT
        // hook, so the previously-running application keeps inserting employees without this column
        // during the rollout. A required column with no default would turn every one of those
        // inserts into a 23502 until the new pods took over. The database default is what makes this
        // migration safe to land ahead of the code, and FR-8.3's load default is the same value.
        //
        // This is also why the INSERTs elsewhere in this file — none of which name timezone — still
        // work unchanged.
        await RawExecAsync(
            ctx.Context,
            $@"INSERT INTO ""compass"".""employee""
                   (first_name, last_name, hire_date, email, employee_type_id, is_active,
                    state_of_residence, timesheet_required, can_submit_under_40, include_in_payroll)
               VALUES ('Ada','Byron', DATE '2026-01-05','ada.byron@leadingedje.com',
                       {ctx.EmployeeTypeId}, true, 'OH', true, false, true);"
        );

        var stored = await ctx.Context
            .Database.SqlQueryRaw<string>(
                @"SELECT timezone AS ""Value"" FROM ""compass"".""employee""
                  WHERE email = 'ada.byron@leadingedje.com';"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        stored.Single().ShouldBe("America/New_York");
    }

    [Fact]
    public async Task Employee_TimezoneCarriesNoCheckConstraint()
    {
        var ctx = await SeedGraphAsync();

        // DELIBERATELY the opposite of state_of_residence, which IS constrained to 51 values.
        //
        // AC-NFR-6 makes US-only residence permanent, so a CHECK states the domain completely. The
        // timezone list is US-only "AT THIS TIME" (Plan v6, open question 1) and the plan requires
        // the option list be widenable "without a schema change" — a CHECK generated from the six
        // would make widening it exactly that. So the six are enforced where they are a *choice*:
        // the dropdown (#421) and the migration's mapping table (#423).
        //
        // Asserted by inserting a valid IANA id outside the six rather than by reading
        // information_schema: a query for an absent constraint passes just as well when the query
        // itself is wrong, and this way the test says what the column actually permits.
        await RawExecAsync(
            ctx.Context,
            $@"INSERT INTO ""compass"".""employee""
                   (first_name, last_name, hire_date, email, employee_type_id, is_active,
                    state_of_residence, timesheet_required, can_submit_under_40, include_in_payroll,
                    timezone)
               VALUES ('Ada','Byron', DATE '2026-01-05','ada.byron@leadingedje.com',
                       {ctx.EmployeeTypeId}, true, 'AZ', true, false, true, 'America/Phoenix');"
        );

        var stored = await ctx.Context
            .Database.SqlQueryRaw<string>(
                @"SELECT timezone AS ""Value"" FROM ""compass"".""employee""
                  WHERE email = 'ada.byron@leadingedje.com';"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        stored.Single().ShouldBe("America/Phoenix");
    }

    [Fact]
    public async Task Employee_RejectsADuplicateEmailEvenWhenInactive()
    {
        var ctx = await SeedGraphAsync();

        // The spec is explicit that email uniqueness spans active AND inactive records, because
        // EDJErs are deactivated rather than deleted.
        var ex = await ThrowsPostgresAsync(
            ctx,
            $@"INSERT INTO ""compass"".""employee""
                   (first_name, last_name, hire_date, email, employee_type_id, is_active,
                    state_of_residence, timesheet_required, can_submit_under_40, include_in_payroll)
               VALUES ('Other','Person', DATE '2026-02-01','{ctx.EmployeeEmail}',
                       {ctx.EmployeeTypeId}, false, 'OH', true, false, true);"
        );

        ex.SqlState.ShouldBe(UniqueViolation, "employee.email is unique across active and inactive");
    }

    [Fact]
    public async Task Employee_RequiresAnEmployeeType()
    {
        var ctx = await SeedGraphAsync();

        var ex = await ThrowsPostgresAsync(
            ctx,
            @"INSERT INTO ""compass"".""employee""
                  (first_name, last_name, hire_date, email, employee_type_id, is_active,
                   state_of_residence, timesheet_required, can_submit_under_40, include_in_payroll)
              VALUES ('No','Type', DATE '2026-02-01','no.type@leadingedje.com',
                      NULL, true, 'OH', true, false, true);"
        );

        ex.SqlState.ShouldBe(NotNullViolation, "employee_type_id is required");
    }

    [Fact]
    public async Task Client_RejectsADuplicateName()
    {
        var ctx = await SeedGraphAsync();

        var ex = await ThrowsPostgresAsync(
            ctx,
            $@"INSERT INTO ""compass"".""client"" (client_name, is_internal)
               VALUES ('{ctx.ClientName}', false);"
        );

        ex.SqlState.ShouldBe(UniqueViolation, "client_name is unique");
    }

    [Fact]
    public async Task Client_HasNoStoredStatusColumn()
    {
        // Arrange — the spec: a client "carries no stored status"; Active/Inactive is DERIVED from
        // its assignments. A stored column would let the two disagree.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var statusColumns = await context
            .Database.SqlQueryRaw<string>(
                $@"SELECT column_name AS ""Value"" FROM information_schema.columns
                   WHERE table_schema = '{CompassSchema}' AND table_name = 'client'
                     AND column_name IN ('is_active','status','client_status');"
            )
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        statusColumns.ShouldBeEmpty("client status is derived from assignments, never stored");
    }

    [Fact]
    public async Task BillableTimeCategory_RejectsADuplicateNameWithinOneClient()
    {
        var ctx = await SeedGraphAsync();

        await ExecAsync(
            ctx,
            $@"INSERT INTO ""compass"".""billable_time_category"" (client_id, category_name, is_active)
               VALUES ({ctx.ClientId}, 'Development', true);"
        );

        var ex = await ThrowsPostgresAsync(
            ctx,
            $@"INSERT INTO ""compass"".""billable_time_category"" (client_id, category_name, is_active)
               VALUES ({ctx.ClientId}, 'Development', true);"
        );

        ex.SqlState.ShouldBe(UniqueViolation, "category_name is unique PER CLIENT");
    }

    [Fact]
    public async Task BillableTimeCategory_AllowsTheSameNameForADifferentClient()
    {
        var ctx = await SeedGraphAsync();
        var otherClient = await ScalarAsync(
            ctx,
            @"INSERT INTO ""compass"".""client"" (client_name, is_internal)
              VALUES ('Second Client', false) RETURNING client_id AS ""Value"";"
        );

        await ExecAsync(
            ctx,
            $@"INSERT INTO ""compass"".""billable_time_category"" (client_id, category_name, is_active)
               VALUES ({ctx.ClientId}, 'Development', true);"
        );
        await ExecAsync(
            ctx,
            $@"INSERT INTO ""compass"".""billable_time_category"" (client_id, category_name, is_active)
               VALUES ({otherClient}, 'Development', true);"
        );

        var rows = await CountAsync(
            ctx,
            @"SELECT count(*)::int AS ""Value"" FROM ""compass"".""billable_time_category"";"
        );
        rows.ShouldBe(2, "uniqueness is scoped to the client, not global");
    }

    [Fact]
    public async Task LookupTypes_RejectDuplicateNames()
    {
        var ctx = await SeedGraphAsync();

        var employeeTypeClash = await ThrowsPostgresAsync(
            ctx,
            @"INSERT INTO ""compass"".""employee_type"" (type_name, is_active)
              VALUES ('Full Time', true);"
        );
        var frequencyClash = await ThrowsPostgresAsync(
            ctx,
            @"INSERT INTO ""compass"".""invoice_frequency_type"" (type_name, is_active)
              VALUES ('Monthly', true);"
        );

        employeeTypeClash.SqlState.ShouldBe(UniqueViolation, "employee_type.type_name is unique");
        frequencyClash.SqlState.ShouldBe(
            UniqueViolation,
            "invoice_frequency_type.type_name is unique"
        );
    }

    [Fact]
    public async Task Compass_RefusesDeleteOfAReferencedRow()
    {
        // Arrange — one isolated parent/child pair per DeleteBehavior.Restrict relationship (PR
        // #186 configured 7, across five Compass configurations), so a refusal is attributable to
        // exactly the FK under test rather than one of several relationships sharing a row. `client`
        // is the case that needs isolating deliberately: it is the parent of BOTH
        // billable_time_category and client_assignment, so one client per child keeps each DELETE
        // pinned to a single relationship.
        var g = await SeedFullGraphAsync();

        // Act / Assert — each DELETE must be refused by Postgres itself (23503), with the
        // application bypassed entirely. A cascade or a missing constraint would let one of these
        // succeed and destroy data another table still refers to.
        (
            await ThrowsPostgresAsync(
                g.Context,
                $@"DELETE FROM ""compass"".""employee_type"" WHERE employee_type_id = {g.EmployeeTypeId};"
            )
        ).SqlState.ShouldBe(ForeignKeyViolation, "employee.employee_type_id -> employee_type");

        (
            await ThrowsPostgresAsync(
                g.Context,
                $@"DELETE FROM ""compass"".""invoice_frequency_type"" WHERE invoice_frequency_type_id = {g.InvoiceFrequencyTypeId};"
            )
        ).SqlState.ShouldBe(
            ForeignKeyViolation,
            "client.invoice_frequency_type_id -> invoice_frequency_type"
        );

        (
            await ThrowsPostgresAsync(
                g.Context,
                $@"DELETE FROM ""compass"".""employee"" WHERE employee_id = {g.CoachEmployeeId};"
            )
        ).SqlState.ShouldBe(ForeignKeyViolation, "employee.coach_employee_id -> employee (self)");

        (
            await ThrowsPostgresAsync(
                g.Context,
                $@"DELETE FROM ""compass"".""employee"" WHERE employee_id = {g.AssignedEmployeeId};"
            )
        ).SqlState.ShouldBe(ForeignKeyViolation, "client_assignment.employee_id -> employee");

        (
            await ThrowsPostgresAsync(
                g.Context,
                $@"DELETE FROM ""compass"".""client"" WHERE client_id = {g.ClientForAssignmentId};"
            )
        ).SqlState.ShouldBe(ForeignKeyViolation, "client_assignment.client_id -> client");

        (
            await ThrowsPostgresAsync(
                g.Context,
                $@"DELETE FROM ""compass"".""client"" WHERE client_id = {g.ClientForCategoryId};"
            )
        ).SqlState.ShouldBe(ForeignKeyViolation, "billable_time_category.client_id -> client");

        (
            await ThrowsPostgresAsync(
                g.Context,
                $@"DELETE FROM ""compass"".""client_assignment"" WHERE client_assignment_id = {g.AssignmentId};"
            )
        ).SqlState.ShouldBe(ForeignKeyViolation, "sow.client_assignment_id -> client_assignment");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private sealed record Graph(
        LeapDbContext Context,
        int EmployeeTypeId,
        int EmployeeId,
        int ClientId,
        int AssignmentId,
        string EmployeeEmail,
        string ClientName
    );

    /// <summary>
    /// Seeds the minimum valid graph the ERD requires: a lookup type, an EDJEr, a client, and
    /// optionally an assignment to hang SOWs from.
    /// </summary>
    private async Task<Graph> SeedGraphAsync(bool withAssignment = false)
    {
        await ResetDatabaseAsync();
        var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        const string email = "grace.hopper@leadingedje.com";
        const string clientName = "Acme Industrial";

        var employeeTypeId = await RawScalarAsync(
            context,
            @"INSERT INTO ""compass"".""employee_type"" (type_name, is_active)
              VALUES ('Full Time', true) RETURNING employee_type_id AS ""Value"";"
        );
        await RawExecAsync(
            context,
            @"INSERT INTO ""compass"".""invoice_frequency_type"" (type_name, is_active)
              VALUES ('Monthly', true);"
        );
        var employeeId = await RawScalarAsync(
            context,
            $@"INSERT INTO ""compass"".""employee""
                   (first_name, last_name, hire_date, email, employee_type_id, coach_employee_id,
                    is_active, state_of_residence, timesheet_required, can_submit_under_40,
                    include_in_payroll)
               VALUES ('Grace','Hopper', DATE '2026-01-05','{email}', {employeeTypeId}, NULL,
                       true, 'OH', true, false, true)
               RETURNING employee_id AS ""Value"";"
        );
        var clientId = await RawScalarAsync(
            context,
            $@"INSERT INTO ""compass"".""client"" (client_name, is_internal)
               VALUES ('{clientName}', false) RETURNING client_id AS ""Value"";"
        );

        var assignmentId = 0;
        if (withAssignment)
        {
            assignmentId = await RawScalarAsync(
                context,
                $@"INSERT INTO ""compass"".""client_assignment""
                       (employee_id, client_id, start_date, end_date)
                   VALUES ({employeeId}, {clientId}, DATE '2026-01-01', NULL)
                   RETURNING client_assignment_id AS ""Value"";"
            );
        }

        return new Graph(
            context,
            employeeTypeId,
            employeeId,
            clientId,
            assignmentId,
            email,
            clientName
        );
    }

    private sealed record FullGraph(
        LeapDbContext Context,
        int EmployeeTypeId,
        int InvoiceFrequencyTypeId,
        int CoachEmployeeId,
        int AssignedEmployeeId,
        int ClientForAssignmentId,
        int ClientForCategoryId,
        int AssignmentId
    );

    /// <summary>
    /// Seeds one isolated parent/child pair per <c>DeleteBehavior.Restrict</c> relationship: two
    /// employees (a coach and the EDJEr they coach, so the self-referencing FK has a distinct
    /// parent from the one <c>client_assignment.employee_id</c> targets) and two clients (one
    /// carrying the assignment, one carrying the billable category) so deleting either client
    /// exercises exactly one of client's two child relationships.
    /// </summary>
    private async Task<FullGraph> SeedFullGraphAsync()
    {
        await ResetDatabaseAsync();
        var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var employeeTypeId = await RawScalarAsync(
            context,
            @"INSERT INTO ""compass"".""employee_type"" (type_name, is_active)
              VALUES ('Full Time', true) RETURNING employee_type_id AS ""Value"";"
        );
        var invoiceFrequencyTypeId = await RawScalarAsync(
            context,
            @"INSERT INTO ""compass"".""invoice_frequency_type"" (type_name, is_active)
              VALUES ('Monthly', true) RETURNING invoice_frequency_type_id AS ""Value"";"
        );

        var coachEmployeeId = await RawScalarAsync(
            context,
            $@"INSERT INTO ""compass"".""employee""
                   (first_name, last_name, hire_date, email, employee_type_id, coach_employee_id,
                    is_active, state_of_residence, timesheet_required, can_submit_under_40,
                    include_in_payroll)
               VALUES ('Coach','Person', DATE '2020-01-05','coach.person@leadingedje.com',
                       {employeeTypeId}, NULL, true, 'OH', true, false, true)
               RETURNING employee_id AS ""Value"";"
        );
        var assignedEmployeeId = await RawScalarAsync(
            context,
            $@"INSERT INTO ""compass"".""employee""
                   (first_name, last_name, hire_date, email, employee_type_id, coach_employee_id,
                    is_active, state_of_residence, timesheet_required, can_submit_under_40,
                    include_in_payroll)
               VALUES ('Assigned','Person', DATE '2026-01-05','assigned.person@leadingedje.com',
                       {employeeTypeId}, {coachEmployeeId}, true, 'OH', true, false, true)
               RETURNING employee_id AS ""Value"";"
        );

        var clientForAssignmentId = await RawScalarAsync(
            context,
            $@"INSERT INTO ""compass"".""client"" (client_name, is_internal, invoice_frequency_type_id)
               VALUES ('Client With Assignment', false, {invoiceFrequencyTypeId})
               RETURNING client_id AS ""Value"";"
        );
        var clientForCategoryId = await RawScalarAsync(
            context,
            @"INSERT INTO ""compass"".""client"" (client_name, is_internal)
              VALUES ('Client With Category', false) RETURNING client_id AS ""Value"";"
        );

        await RawExecAsync(
            context,
            $@"INSERT INTO ""compass"".""billable_time_category"" (client_id, category_name, is_active)
               VALUES ({clientForCategoryId}, 'Development', true);"
        );

        var assignmentId = await RawScalarAsync(
            context,
            $@"INSERT INTO ""compass"".""client_assignment""
                   (employee_id, client_id, start_date, end_date)
               VALUES ({assignedEmployeeId}, {clientForAssignmentId}, DATE '2026-01-01', NULL)
               RETURNING client_assignment_id AS ""Value"";"
        );

        await RawExecAsync(
            context,
            $@"INSERT INTO ""compass"".""sow""
                   (client_assignment_id, sow_type, rate_increase, sow_start_date, sow_end_date, has_passed_application_validation)
               VALUES ({assignmentId}, 'InitialContract', false, DATE '2026-01-01', DATE '2026-06-30', true);"
        );

        return new FullGraph(
            context,
            employeeTypeId,
            invoiceFrequencyTypeId,
            coachEmployeeId,
            assignedEmployeeId,
            clientForAssignmentId,
            clientForCategoryId,
            assignmentId
        );
    }

    private static Task ExecAsync(Graph graph, string sql) => RawExecAsync(graph.Context, sql);

    private static Task<int> ScalarAsync(Graph graph, string sql) =>
        RawScalarAsync(graph.Context, sql);

    private static Task<int> CountAsync(Graph graph, string sql) =>
        RawScalarAsync(graph.Context, sql);

    private static Task RawExecAsync(LeapDbContext context, string sql) =>
        context.Database.ExecuteSqlRawAsync(sql, TestContext.Current.CancellationToken);

    private static async Task<int> RawScalarAsync(LeapDbContext context, string sql)
    {
        var rows = await context
            .Database.SqlQueryRaw<int>(sql)
            .ToListAsync(TestContext.Current.CancellationToken);
        return rows.Single();
    }

    /// <summary>
    /// Asserts the statement is rejected by Postgres and hands back the exception so the caller can
    /// assert on SQLSTATE. A statement that SUCCEEDS fails the test explicitly — the constraint is
    /// missing, which is the whole point of #50.
    /// </summary>
    private static Task<PostgresException> ThrowsPostgresAsync(Graph graph, string sql) =>
        ThrowsPostgresAsync(graph.Context, sql);

    private static async Task<PostgresException> ThrowsPostgresAsync(LeapDbContext context, string sql)
    {
        try
        {
            await RawExecAsync(context, sql);
        }
        catch (PostgresException ex)
        {
            return ex;
        }

        throw new Shouldly.ShouldAssertException(
            "expected Postgres to REJECT the statement, but it was accepted — the constraint the "
                + "ERD implies is missing from the schema"
        );
    }
}
