using System.Text.Json;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.SeedData;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

/// <summary>
/// Unit coverage for <see cref="CompassDirectorySeeder"/> — the shape of the graph it builds.
/// </summary>
/// <remarks>
/// <para>
/// The InMemory provider enforces none of the database's constraints — no CHECK, no UNIQUE, no
/// exclusion constraint, no FK. So every invariant the live schema imposes is asserted here in code
/// against the produced graph, which is what makes these tests worth having: they fail on a *fixture*
/// mistake in milliseconds instead of surfacing as a 23514/23P01 in the integration run.
/// </para>
/// <para>
/// The live-catalogue counterpart — the same seeder persisted through real Postgres with all seven
/// tables' constraints armed — is
/// <c>tests/integration/Compass/CompassDirectorySeederTests.cs</c>. Both are required: this one says
/// the data is *shaped* right, that one says Postgres *accepts* it.
/// </para>
/// </remarks>
public class CompassDirectorySeederTests
{
    /// <summary>A pinned anchor date so every relative offset in the fixture is reproducible.</summary>
    private static readonly DateOnly Today = new(2026, 8, 7);

    private static LeapDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<LeapDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private static async Task<LeapDbContext> SeededContextAsync()
    {
        var context = CreateInMemoryContext();
        await CompassDirectorySeeder.SeedAsync(context, Today, TestContext.Current.CancellationToken);
        return context;
    }

    // ------------------------------------------------------------------ Coverage + idempotency

    [Fact]
    public async Task SeedAsync_OnEmptyDatabase_PopulatesAllSevenCompassTables()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["employee_type"] = await context.Set<EmployeeType>().CountAsync(TestContext.Current.CancellationToken),
            ["invoice_frequency_type"] = await context.Set<InvoiceFrequencyType>().CountAsync(TestContext.Current.CancellationToken),
            ["employee"] = await context.Set<Employee>().CountAsync(TestContext.Current.CancellationToken),
            ["client"] = await context.Set<Client>().CountAsync(TestContext.Current.CancellationToken),
            ["client_assignment"] = await context.Set<ClientAssignment>().CountAsync(TestContext.Current.CancellationToken),
            ["sow"] = await context.Set<Sow>().CountAsync(TestContext.Current.CancellationToken),
            ["billable_time_category"] = await context.Set<BillableTimeCategory>().CountAsync(TestContext.Current.CancellationToken),
        };

        // Assert — every table populated; the directory is large enough to exercise paging and sorting.
        foreach (var (table, count) in counts)
        {
            count.ShouldBeGreaterThan(0, $"compass.{table} was left empty by the seeder");
        }

        counts["employee"].ShouldBeGreaterThanOrEqualTo(
            90,
            "SC-006 measures p95 at ~90 EDJErs, so the dev fixture must reach that scale");
    }

    [Fact]
    public async Task SeedAsync_BackFillsTheDevBypassCounterpart_IntoAnAlreadySeededDirectory()
    {
        // Arrange -- the case every existing developer machine is in, and the one the original guard
        // silently skipped. The directory content is guarded as ONE unit (assignments and SOWs
        // reference employees created in the same pass), so on any database that has ever run
        // `make dev-all` that whole block is skipped -- and the DevBypass counterpart was inside it.
        // The caller then resolved to nothing and /Employees/Me answered 404: FR-011 unmet, and
        // indistinguishable from the feature being broken. Found in adversarial review.
        //
        // Simulated by seeding, then deleting only that row, then re-seeding: the directory is
        // non-empty throughout, exactly as it is in the wild.
        using var context = await SeededContextAsync();
        var counterpart = await context.Set<Employee>()
            .SingleAsync(e => e.Email == "avery.quinn@example.com", TestContext.Current.CancellationToken);
        var employeesBefore = await context.Set<Employee>().CountAsync(TestContext.Current.CancellationToken);
        context.Set<Employee>().Remove(counterpart);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await CompassDirectorySeeder.SeedAsync(context, Today, TestContext.Current.CancellationToken);

        // Assert -- it came back, and nothing else did.
        var restored = await context.Set<Employee>()
            .SingleOrDefaultAsync(e => e.Email == "avery.quinn@example.com", TestContext.Current.CancellationToken);
        restored.ShouldNotBeNull(
            "the DevBypass caller's counterpart must back-fill on its own sentinel, or FR-011 holds "
                + "only on a virgin database -- which no developer machine is");
        (await context.Set<Employee>().CountAsync(TestContext.Current.CancellationToken))
            .ShouldBe(employeesBefore, "the back-fill must add exactly the missing row");
    }

    [Fact]
    public async Task SeedAsync_RunTwice_AddsNothingTheSecondTime()
    {
        // Arrange
        using var context = await SeededContextAsync();
        var before = await context.Set<Employee>().CountAsync(TestContext.Current.CancellationToken);

        // Act
        await CompassDirectorySeeder.SeedAsync(context, Today, TestContext.Current.CancellationToken);

        // Assert
        var after = await context.Set<Employee>().CountAsync(TestContext.Current.CancellationToken);
        after.ShouldBe(before, "the seeder must be idempotent — startup runs it on every boot");
    }

    [Fact]
    public async Task SeedAsync_WithTheSameAnchorDate_ProducesAnIdenticalGraph()
    {
        // Arrange
        using var first = await SeededContextAsync();
        using var second = await SeededContextAsync();

        // Act
        var firstEmails = await first.Set<Employee>().OrderBy(e => e.Id)
            .Select(e => $"{e.Id}|{e.Email}|{e.HireDate}|{e.StateOfResidence}|{e.CoachEmployeeId}")
            .ToListAsync(TestContext.Current.CancellationToken);
        var secondEmails = await second.Set<Employee>().OrderBy(e => e.Id)
            .Select(e => $"{e.Id}|{e.Email}|{e.HireDate}|{e.StateOfResidence}|{e.CoachEmployeeId}")
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert — determinism is what lets tests and developers pin values.
        secondEmails.ShouldBe(firstEmails);
    }

    // ------------------------------------------------------------------ Live-schema invariants

    [Fact]
    public async Task SeedAsync_EveryEmployeeEmail_IsUnique()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act
        var emails = await context.Set<Employee>().Select(e => e.Email).ToListAsync(TestContext.Current.CancellationToken);

        // Assert — ux_employee_email
        emails.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(emails.Count);
        emails.ShouldAllBe(e => e.Length > 0);
    }

    [Fact]
    public async Task SeedAsync_EveryEmployeeState_IsOneOfTheFiftyStatesPlusDc()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act
        var states = await context.Set<Employee>().Select(e => e.StateOfResidence).Distinct()
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert — ck_employee_state_of_residence_us
        var invalid = states.Where(s => !UsStateCodes.Contains(s, StringComparer.Ordinal)).ToList();
        invalid.ShouldBeEmpty($"invalid state codes: {string.Join(", ", invalid)}");
    }

    [Fact]
    public async Task SeedAsync_EveryEmployeeTimezone_IsOneOfTheSupportedUsZones()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act
        var zones = await context.Set<Employee>().Select(e => e.Timezone).Distinct()
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert — FR-8.1: the stored value is one of the six IANA ids, never blank. The column
        // carries no CHECK (the list must widen without a schema change), so this assertion is the
        // only thing standing between the fixture and a bogus zone.
        var invalid = zones.Where(z => !SupportedTimezones.Contains(z, StringComparer.Ordinal)).ToList();
        invalid.ShouldBeEmpty($"unsupported timezones: {string.Join(", ", invalid)}");
    }

    [Fact]
    public async Task SeedAsync_EveryEmployeeTimezone_AgreesWithTheirStateOfResidence()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act
        var pairs = await context.Set<Employee>()
            .Select(e => new { e.StateOfResidence, e.Timezone })
            .Distinct()
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert — the fixture derives the zone from the state, so one state must never appear with
        // two zones. An EDJEr in WA carrying Eastern is not wrong in the database's eyes, but it is
        // incoherent to anyone reading the seeded directory, and OOTO renders both together.
        var ambiguous = pairs
            .GroupBy(p => p.StateOfResidence, StringComparer.Ordinal)
            .Where(g => g.Select(p => p.Timezone).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Key)
            .ToList();

        ambiguous.ShouldBeEmpty($"states seeded with more than one timezone: {string.Join(", ", ambiguous)}");
    }

    [Fact]
    public void TimezoneForState_MapsEveryUsStateCode_ToASupportedZone()
    {
        // Arrange — walk the whole domain, not just the states this fixture happens to use. The
        // seeder's pools change; the mapping's completeness should not depend on that.
        UsStateCodes.Length.ShouldBe(51, "a truncated state list would make this walk vacuous");

        // Act
        var mapped = UsStateCodes
            .Select(state => (State: state, Timezone: CompassDirectorySeeder.TimezoneForState(state)))
            .ToList();

        // Assert — this is what makes the table COMPLETE. It replaced a runtime count guard inside
        // the seeder: an `if` that never fires proves nothing and cannot be covered, whereas this
        // walks all 51 and additionally checks each lands on a real zone rather than merely existing.
        var unsupported = mapped
            .Where(m => !SupportedTimezones.Contains(m.Timezone, StringComparer.Ordinal))
            .Select(m => $"{m.State}->{m.Timezone}")
            .ToList();

        unsupported.ShouldBeEmpty($"states mapped outside the six: {string.Join(", ", unsupported)}");
    }

    [Fact]
    public void TimezoneForState_ThrowsForACodeItDoesNotMap()
    {
        // A state the table misses must fail loudly at seed time. Silently defaulting would put an
        // EDJEr in a timezone nobody chose, and the fixture is exactly where that goes unnoticed.
        var ex = Should.Throw<InvalidOperationException>(() => CompassDirectorySeeder.TimezoneForState("ZZ"));

        ex.Message.ShouldContain("ZZ");
    }

    [Fact]
    public async Task SeedAsync_EveryCoachReference_ResolvesToASeededEmployee()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act
        var ids = (await context.Set<Employee>().Select(e => e.Id).ToListAsync(TestContext.Current.CancellationToken)).ToHashSet();
        var coachIds = await context.Set<Employee>()
            .Where(e => e.CoachEmployeeId != null)
            .Select(e => new { e.Id, CoachId = e.CoachEmployeeId!.Value })
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert — the self-referencing FK, plus no employee coaching themselves.
        coachIds.ShouldAllBe(x => ids.Contains(x.CoachId));
        coachIds.ShouldAllBe(x => x.CoachId != x.Id);
        coachIds.ShouldNotBeEmpty("the coach hierarchy is a directory feature and must be exercised");
    }

    [Fact]
    public async Task SeedAsync_NoTwoSowsOnTheSameAssignment_HaveOverlappingDateRanges()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act — LegacyMigrated is excluded: the constraint itself is PARTIAL and skips those rows
        // (FR-053), and the fixture deliberately contains an overlapping legacy pair.
        var byAssignment = (await context.Set<Sow>().ToListAsync(TestContext.Current.CancellationToken))
            .Where(s => s.SowType != SowType.LegacyMigrated)
            .GroupBy(s => s.ClientAssignmentId);

        // Assert — ex_sow_no_overlap_per_assignment. The constraint uses an INCLUSIVE daterange
        // '[]', so two SOWs sharing a single endpoint DO overlap: the next period must start at
        // least one day after the previous one ends.
        foreach (var group in byAssignment)
        {
            var ordered = group.OrderBy(s => s.SowStartDate).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                ordered[i].SowStartDate.ShouldBeGreaterThan(
                    ordered[i - 1].SowEndDate,
                    $"assignment {group.Key} has SOWs {ordered[i - 1].Id} and {ordered[i].Id} sharing or "
                        + "overlapping a day — the gist exclusion constraint rejects this");
            }
        }
    }

    [Fact]
    public async Task SeedAsync_RateIncrease_IsSetOnlyOnExtensions()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act
        var offenders = await context.Set<Sow>()
            .Where(s => s.RateIncrease && s.SowType != SowType.SowExtension)
            .Select(s => s.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert — ck_sow_rate_increase_requires_extension
        offenders.ShouldBeEmpty($"initial contracts carrying a rate increase: {string.Join(", ", offenders)}");
    }

    [Fact]
    public async Task SeedAsync_EverySowEndDate_IsOnOrAfterItsStartDate()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act — LegacyMigrated is exempt; the CHECK is partial and the fixture contains a legacy row
        // whose end precedes its start, exactly as TPS recorded it.
        var offenders = await context.Set<Sow>()
            .Where(s => s.SowType != SowType.LegacyMigrated && s.SowEndDate < s.SowStartDate)
            .Select(s => s.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert — ck_sow_end_on_or_after_start
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task SeedAsync_ProducesTheLegacyMigratedCases_ThatTheExemptionExistsFor()
    {
        // Arrange
        using var context = await SeededContextAsync();
        var ct = TestContext.Current.CancellationToken;

        // Act
        var legacy = await context.Set<Sow>().Where(s => s.SowType == SowType.LegacyMigrated).ToListAsync(ct);

        // Assert — a developer must be able to see the grandfathered shapes without running a TPS
        // load, because any feature touching SOWs has to cope with them.
        legacy.ShouldNotBeEmpty();
        legacy.ShouldAllBe(s => !s.HasPassedApplicationValidation);
        legacy.ShouldAllBe(s => !s.RateIncrease);

        legacy.ShouldContain(s => s.SowEndDate < s.SowStartDate, "a legacy period whose end precedes its start");

        var overlapping = legacy
            .GroupBy(s => s.ClientAssignmentId)
            .Any(g => g.OrderBy(s => s.SowStartDate)
                .Zip(g.OrderBy(s => s.SowStartDate).Skip(1))
                .Any(pair => pair.Second.SowStartDate <= pair.First.SowEndDate));
        overlapping.ShouldBeTrue("two legacy periods overlapping on one assignment");

        // Everything Compass creates itself is validated on arrival.
        var native = await context.Set<Sow>().Where(s => s.SowType != SowType.LegacyMigrated).ToListAsync(ct);
        native.ShouldAllBe(s => s.HasPassedApplicationValidation);
    }

    [Fact]
    public async Task SeedAsync_EveryAssignmentEndDate_IsNullOrOnOrAfterItsStartDate()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act
        var offenders = await context.Set<ClientAssignment>()
            .Where(a => a.EndDate != null && a.EndDate < a.StartDate)
            .Select(a => a.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert — ck_client_assignment_end_on_or_after_start
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task SeedAsync_EverySowSitsInsideItsAssignmentsWindow()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act — not a database constraint, but a closed assignment whose SOW runs past the end date
        // is nonsense data that would make the reports lie.
        var assignments = await context.Set<ClientAssignment>()
            .ToDictionaryAsync(a => a.Id, TestContext.Current.CancellationToken);
        var sows = await context.Set<Sow>().ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        foreach (var sow in sows)
        {
            var assignment = assignments[sow.ClientAssignmentId];
            sow.SowStartDate.ShouldBeGreaterThanOrEqualTo(assignment.StartDate, $"sow {sow.Id}");
            if (assignment.EndDate is { } end)
            {
                sow.SowEndDate.ShouldBeLessThanOrEqualTo(end, $"sow {sow.Id}");
            }
        }
    }

    [Fact]
    public async Task SeedAsync_ClientNames_AreUnique()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act
        var names = await context.Set<Client>().Select(c => c.ClientName).ToListAsync(TestContext.Current.CancellationToken);

        // Assert — ux_client_client_name
        names.Distinct(StringComparer.Ordinal).Count().ShouldBe(names.Count);
    }

    [Fact]
    public async Task SeedAsync_BillableTimeCategoryNames_AreUniqueWithinAClient()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act
        var pairs = await context.Set<BillableTimeCategory>()
            .Select(b => new { b.ClientId, b.CategoryName })
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert — ux_billable_time_category_client_id_category_name. The same NAME on two different
        // clients is legitimate and the fixture deliberately contains that case.
        pairs.Distinct().Count().ShouldBe(pairs.Count);
        pairs.Select(p => p.CategoryName).Distinct(StringComparer.Ordinal).Count()
            .ShouldBeLessThan(pairs.Count, "the fixture should reuse a category name across clients");
    }

    [Fact]
    public async Task SeedAsync_EveryForeignKey_ResolvesToASeededRow()
    {
        // Arrange
        using var context = await SeededContextAsync();
        var ct = TestContext.Current.CancellationToken;

        // Act
        var employeeIds = (await context.Set<Employee>().Select(e => e.Id).ToListAsync(ct)).ToHashSet();
        var clientIds = (await context.Set<Client>().Select(c => c.Id).ToListAsync(ct)).ToHashSet();
        var typeIds = (await context.Set<EmployeeType>().Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        var freqIds = (await context.Set<InvoiceFrequencyType>().Select(f => f.Id).ToListAsync(ct)).ToHashSet();
        var assignmentIds = (await context.Set<ClientAssignment>().Select(a => a.Id).ToListAsync(ct)).ToHashSet();

        // Assert — every FK is ON DELETE RESTRICT and NOT NULL where the ERD says so.
        (await context.Set<Employee>().ToListAsync(ct)).ShouldAllBe(e => typeIds.Contains(e.EmployeeTypeId));
        (await context.Set<Client>().Where(c => c.InvoiceFrequencyTypeId != null).ToListAsync(ct))
            .ShouldAllBe(c => freqIds.Contains(c.InvoiceFrequencyTypeId!.Value));
        (await context.Set<ClientAssignment>().ToListAsync(ct))
            .ShouldAllBe(a => employeeIds.Contains(a.EmployeeId) && clientIds.Contains(a.ClientId));
        (await context.Set<Sow>().ToListAsync(ct)).ShouldAllBe(s => assignmentIds.Contains(s.ClientAssignmentId));
        (await context.Set<BillableTimeCategory>().ToListAsync(ct)).ShouldAllBe(b => clientIds.Contains(b.ClientId));
    }

    // ------------------------------------------------------------------ Feature-shaped content

    [Fact]
    public async Task SeedAsync_ProducesTheDashboardTileBoundaryCases()
    {
        // Arrange
        using var context = await SeededContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var sows = await context.Set<Sow>().ToListAsync(ct);
        var assignments = await context.Set<ClientAssignment>().ToListAsync(ct);
        var clients = await context.Set<Client>().ToListAsync(ct);

        // Act — the four tiles from the Compass dashboard, computed the way the feature will.
        var latestSowPerAssignment = sows
            .GroupBy(s => s.ClientAssignmentId)
            .ToDictionary(g => g.Key, g => g.Max(s => s.SowEndDate));

        var expiringUnder90 = latestSowPerAssignment
            .Where(kv => kv.Value >= Today && kv.Value < Today.AddDays(90))
            .ToList();
        var exactlyNinety = latestSowPerAssignment.Where(kv => kv.Value == Today.AddDays(90)).ToList();
        var internalClientIds = clients.Where(c => c.IsInternal).Select(c => c.Id).ToHashSet();

        // Assert
        expiringUnder90.ShouldNotBeEmpty("the 'SOW expiring < 90 days' tile needs rows");
        exactlyNinety.ShouldNotBeEmpty(
            "the 90-day boundary must be represented so the tile's strict-inequality can be tested");
        latestSowPerAssignment.Values.ShouldContain(
            Today.AddDays(89),
            "the 89-day case must be represented alongside the 90-day boundary");

        assignments.ShouldContain(a => a.EndDate == Today, "an assignment ending today");
        assignments.ShouldContain(a => a.EndDate > Today, "a confirmed rollout (future end date)");
        assignments.ShouldContain(
            a => internalClientIds.Contains(a.ClientId) && a.EndDate == null,
            "an EDJEr on the beach (internal client, open-ended)");
        assignments.ShouldContain(
            a => internalClientIds.Contains(a.ClientId) && a.EndDate > Today,
            "an EDJEr on the beach with a confirmed roll-on date");
    }

    [Fact]
    public async Task SeedAsync_ProducesTheDirectoryAndReportEdgeCases()
    {
        // Arrange
        using var context = await SeededContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var employees = await context.Set<Employee>().ToListAsync(ct);
        var clients = await context.Set<Client>().ToListAsync(ct);
        var assignments = await context.Set<ClientAssignment>().ToListAsync(ct);
        var sows = await context.Set<Sow>().ToListAsync(ct);

        // Act
        var assignedClientIds = assignments.Select(a => a.ClientId).ToHashSet();
        var employeesWithConcurrentWork = employees
            .Where(e => assignments.Count(a => a.EmployeeId == e.Id && (a.EndDate == null || a.EndDate >= Today)) > 1)
            .ToList();

        // Assert
        employees.ShouldContain(e => e.CoachEmployeeId == null, "the top of the org chart has no coach");
        employees.ShouldContain(e => !e.IsActive, "an inactive EDJEr, for the active/inactive filter");
        employees.Select(e => e.EmployeeTypeId).Distinct().Count()
            .ShouldBe(3, "all three employee types must appear, for the type filter");
        employees.Select(e => e.StateOfResidence).Distinct().Count()
            .ShouldBeGreaterThan(5, "several states, for the state filter");
        employeesWithConcurrentWork.ShouldNotBeEmpty(
            "the directory shows ALL current assignments where an EDJEr holds more than one");

        clients.ShouldContain(c => !assignedClientIds.Contains(c.Id), "a client with no assignments (derived Inactive)");
        clients.ShouldContain(c => c.IsInternal, "the internal EDJE client that backs the beach tile");
        clients.ShouldContain(c => c.MsaSignedDate == null, "a client missing its MSA date");
        clients.ShouldContain(c => c.NdaSignedDate == null, "a client missing its NDA date");
        clients.ShouldContain(c => c.InvoiceFrequencyTypeId == null, "a client with no invoicing cadence set");

        // A client whose every assignment has ended — derived status must read Inactive.
        clients.ShouldContain(
            c => assignments.Any(a => a.ClientId == c.Id)
                && assignments.Where(a => a.ClientId == c.Id).All(a => a.EndDate != null && a.EndDate < Today),
            "a client whose assignments have all ended (derived Inactive)");

        // Client Assignment Duration is computed over the FULL history, so an assignment older than
        // any plausible retention window has to exist.
        assignments.ShouldContain(
            a => a.StartDate < Today.AddYears(-5),
            "a long-running assignment that predates any retention window");

        // Gaps between SOWs are legitimate and must not be flagged.
        var hasGap = sows
            .GroupBy(s => s.ClientAssignmentId)
            .Any(g =>
            {
                var ordered = g.OrderBy(s => s.SowStartDate).ToList();
                return ordered.Zip(ordered.Skip(1)).Any(p => p.Second.SowStartDate > p.First.SowEndDate.AddDays(1));
            });
        hasGap.ShouldBeTrue("a legitimate gap between two SOWs on one assignment must be represented");

        sows.ShouldContain(s => s.SowType == SowType.SowExtension && s.RateIncrease, "an extension carrying a rate increase");
        sows.ShouldContain(s => s.SowType == SowType.InitialContract, "an initial contract");
        sows.ShouldContain(s => s.SowType == SowType.LegacyMigrated, "a period migrated from legacy TPS");
        assignments.ShouldContain(a => !sows.Any(s => s.ClientAssignmentId == a.Id), "an assignment with no SOW");
        assignments.ShouldContain(a => a.Note != null, "an assignment carrying an elevated-visibility note");
    }

    // ------------------------------------------------------------------ ERD conformance

    [Fact]
    public async Task SeedAsync_LookupTables_HoldExactlyTheErdSeedValues()
    {
        // Arrange
        using var context = await SeededContextAsync();
        var ct = TestContext.Current.CancellationToken;

        // Act
        var employeeTypes = await context.Set<EmployeeType>().OrderBy(t => t.Id)
            .Select(t => new { t.Id, t.TypeName }).ToListAsync(ct);
        var invoiceFrequencies = await context.Set<InvoiceFrequencyType>().OrderBy(f => f.Id)
            .Select(f => f.TypeName).ToListAsync(ct);

        // Assert — the ERD data dictionary pins both seed sets verbatim ("Seeded: Full Time, Part
        // Time, 1099" and "Seeded: Weekly, Monthly, Fixed Bid"). Extra lookup rows are not the dev
        // fixture's to invent: a Compass Super Admin manages these, and a value that exists here but
        // not in the ERD teaches the team a vocabulary the model does not have.
        //
        // Intern is the ONE sanctioned exception, and it is held to a stricter rule than the ERD
        // three: it exists ONLY so a TPS row can record the classification it actually carried
        // (EmploymentStatus 4), and it is seeded INACTIVE so nobody can apply it to a new hire. The
        // active/inactive split is asserted below rather than left implied — an Intern that became
        // selectable would be the failure it is meant to prevent.
        //
        // Unknown was the second such type and is GONE (issue #398): TPS status 0 now maps to Full
        // Time, so nothing records "Unknown" any more and RemoveCompassUnknownEmployeeType retires
        // the row.
        //
        // ⚠️ THE IDENTIFIERS ARE ASSERTED, NOT JUST THE NAMES, and that is the point of this test
        // since 2026-09-04. Retiring Unknown left the vocabulary at 1/2/3/5 and the product owner
        // asked for 1/2/3/4, so RenumberCompassInternEmployeeTypeToFour moved Intern down. A
        // name-only assertion passes identically before and after that move — it is ordered by id, so
        // the ORDER is all it ever caught — which would have let this fixture and the database
        // disagree about which integer means Intern with nothing failing. The ETL writes that integer
        // (lookups.json employment_status "4") and CompassLookups.InternEmployeeTypeId must equal it.
        employeeTypes.Select(t => t.Id).ShouldBe([1, 2, 3, 4]);
        employeeTypes.Select(t => t.TypeName).ShouldBe(["Full Time", "Part Time", "1099", "Intern"]);
        invoiceFrequencies.ShouldBe(["Weekly", "Monthly", "Fixed Bid"]);
    }

    [Fact]
    public async Task SeedAsync_MigrationOnlyEmployeeTypes_AreSeededInactive()
    {
        // Arrange
        using var context = await SeededContextAsync();
        var ct = TestContext.Current.CancellationToken;

        // Act
        var byName = await context.Set<EmployeeType>().ToDictionaryAsync(t => t.TypeName, ct);

        // Assert — the three ERD types stay selectable; the migration-provenance type must not be.
        // CompassLookupService serves only active types to the create/edit dropdown, so IsActive is
        // the whole mechanism keeping "Intern" off a new hire's record.
        byName["Full Time"].IsActive.ShouldBeTrue();
        byName["Part Time"].IsActive.ShouldBeTrue();
        byName["1099"].IsActive.ShouldBeTrue();
        byName["Intern"].IsActive.ShouldBeFalse(
            "Intern exists to record what TPS said, not to classify a new hire"
        );
        byName.ShouldNotContainKey(
            "Unknown",
            "issue #398 retired it -- TPS status 0 is Full Time now, so nothing records Unknown"
        );
    }

    [Fact]
    public async Task SeedAsync_NoEmployeeIsAssignedToAClientBeforeTheirHireDate()
    {
        // Arrange
        using var context = await SeededContextAsync();
        var ct = TestContext.Current.CancellationToken;

        // Act
        var employees = await context.Set<Employee>().ToDictionaryAsync(e => e.Id, ct);
        var offenders = (await context.Set<ClientAssignment>().ToListAsync(ct))
            .Where(a => a.StartDate < employees[a.EmployeeId].HireDate)
            .Select(a => $"assignment {a.Id} starts {a.StartDate} but employee {a.EmployeeId} was hired {employees[a.EmployeeId].HireDate}")
            .ToList();

        // Assert — the ERD carries no CHECK for this, but an engagement that predates the hire makes
        // tenure and the Client Assignment Duration report lie.
        offenders.ShouldBeEmpty(string.Join("; ", offenders.Take(5)));
    }

    [Fact]
    public async Task SeedAsync_NoInactiveEmployee_HoldsACurrentAssignment()
    {
        // Arrange
        using var context = await SeededContextAsync();
        var ct = TestContext.Current.CancellationToken;

        // Act — "current" is the ERD's own definition of the directory's assigned-client column:
        // end_date IS NULL OR end_date >= today.
        var inactive = (await context.Set<Employee>().Where(e => !e.IsActive).Select(e => e.Id).ToListAsync(ct))
            .ToHashSet();
        var offenders = (await context.Set<ClientAssignment>().ToListAsync(ct))
            .Where(a => inactive.Contains(a.EmployeeId) && (a.EndDate == null || a.EndDate >= Today))
            .Select(a => a.Id)
            .ToList();

        // Assert — a departed EDJEr still showing as currently assigned is contradictory data.
        offenders.ShouldBeEmpty($"assignments held by inactive EDJErs: {string.Join(", ", offenders)}");
        inactive.ShouldNotBeEmpty("the fixture must still contain inactive EDJErs");
    }

    [Fact]
    public async Task SeedAsync_IncludesAnEmployeeWithNoAssignmentAtAll()
    {
        // Arrange
        using var context = await SeededContextAsync();
        var ct = TestContext.Current.CancellationToken;

        // Act
        var assignedIds = (await context.Set<ClientAssignment>().Select(a => a.EmployeeId).ToListAsync(ct)).ToHashSet();
        var unassigned = await context.Set<Employee>()
            .Where(e => !assignedIds.Contains(e.Id))
            .ToListAsync(ct);

        // Assert — the ERD's employee -> client_assignment cardinality is 1 : 0..*, "An EDJEr can be
        // assigned to 0 or many clients". A new hire not yet placed is the zero case, and the
        // directory has to render it.
        unassigned.ShouldNotBeEmpty("no EDJEr exercises the zero side of employee 1 : 0..* client_assignment");
        unassigned.ShouldContain(e => e.IsActive, "the unassigned EDJEr should be an active new hire");
    }

    [Fact]
    public async Task SeedAsync_SowTypes_FollowTheErdsInitialThenExtensionSequence()
    {
        // Arrange
        using var context = await SeededContextAsync();
        var ct = TestContext.Current.CancellationToken;

        // Act — scoped to assignments Compass would have created itself. A LegacyMigrated history is
        // whatever TPS recorded: legacy TPS tracks no extensions, so EVERY migrated period is
        // LegacyMigrated and the initial-then-extension sequence simply does not apply to it.
        var byAssignment = (await context.Set<Sow>().ToListAsync(ct))
            .GroupBy(s => s.ClientAssignmentId)
            .Where(g => g.All(s => s.SowType != SowType.LegacyMigrated))
            .Select(g => g.OrderBy(s => s.SowStartDate).ToList())
            .ToList();

        // Assert — the earliest period on an assignment is the initial contract; everything after it
        // extends.
        byAssignment.ShouldNotBeEmpty();
        foreach (var periods in byAssignment)
        {
            periods[0].SowType.ShouldBe(
                SowType.InitialContract,
                $"the earliest SOW on assignment {periods[0].ClientAssignmentId} must be the Initial Contract");
            periods.Skip(1).ShouldAllBe(s => s.SowType == SowType.SowExtension);
        }
    }

    /// <summary>
    /// The six IANA zones an EDJEr may carry (PRD v9 FR-8.1). Transcribed here rather than read from
    /// <c>UsTimeZones</c>, for the same reason <see cref="UsStateCodes"/> below is: an expectation
    /// taken from the code under test agrees with it by construction.
    /// </summary>
    private static readonly string[] SupportedTimezones =
    [
        "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles",
        "Pacific/Honolulu", "America/Anchorage",
    ];

    /// <summary>The 50 states plus DC, mirroring <c>ck_employee_state_of_residence_us</c>.</summary>
    private static readonly string[] UsStateCodes =
    [
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL", "GA", "HI", "ID", "IL", "IN",
        "IA", "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH",
        "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT",
        "VT", "VA", "WA", "WV", "WI", "WY",
    ];

    // ------------------------------------------------- OOTO correlation (feature 018, issue #424)
    //
    // OOTO reads its employees' timezone from Compass, correlating on email. Before this feature the
    // two rosters were DISJOINT -- OOTO's dev/E2E personas are `*.example.test` addresses seeded by
    // AbsorbedDirectorySeeder/DevelopmentSeeder, and none of them existed in compass.employee -- so
    // the correlation resolved nothing in local dev, in the OOTO browser specs, or anywhere a human
    // could look. These tests pin the counterparts.

    /// <summary>
    /// The OOTO dev/E2E persona addresses that MUST have a Compass counterpart, and the zone each
    /// one's state implies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written out here rather than read from the seeder, deliberately: a test that derived its
    /// expectation from the code under test would agree with any change, including a change that
    /// silently dropped a persona. These addresses are the contract — they are what
    /// <c>web/timesheet/tests/e2e/helpers/auth-session.ts</c>'s <c>FIXTURE</c> table signs in as, and
    /// what <c>AbsorbedDirectorySeeder</c>/<c>DevelopmentSeeder</c> seed on the legacy side.
    /// </para>
    /// <para>
    /// Three personas are deliberately ABSENT and must stay absent — see
    /// <see cref="SeedAsync_LeavesTheDeliberatelyUnmatchedPersonas_WithoutACompassCounterpart"/>.
    /// </para>
    /// </remarks>
    private static readonly (string Email, string Timezone)[] OotoPersonaCounterparts =
    [
        ("alex.coach@example.test", "America/New_York"),
        ("blair.dev@example.test", "America/Chicago"),
        ("casey.dev@example.test", "America/Denver"),
        ("hank.deliver@example.test", "America/Los_Angeles"),
        ("ivy.control@example.test", "Pacific/Honolulu"),
        ("jules.whitfield@example.test", "America/Chicago"),
        ("kira.larsen@example.test", "America/Denver"),
        ("lane.reopener@example.test", "America/Anchorage"),
    ];

    [Fact]
    public async Task SeedAsync_ProducesACompassCounterpart_ForEveryOotoPersona()
    {
        // Arrange
        using var context = await SeededContextAsync();

        // Act
        var byEmail = await context
            .Set<Employee>()
            .ToDictionaryAsync(
                e => e.Email.Trim().ToLowerInvariant(),
                e => e,
                TestContext.Current.CancellationToken);

        // Assert
        OotoPersonaCounterparts.Length.ShouldBeGreaterThan(
            0, "a loop over zero personas would pass while seeding nothing");

        foreach (var (email, timezone) in OotoPersonaCounterparts)
        {
            byEmail.ShouldContainKey(
                email,
                $"OOTO's timezone read correlates on email, so {email} needs a compass.employee row "
                    + "or that persona's timezone resolves to nothing in dev and in the browser specs");
            byEmail[email].Timezone.ShouldBe(timezone);
            byEmail[email].IsActive.ShouldBeTrue(
                $"{email} is an ACTIVE persona on the legacy side; an inactive counterpart would "
                    + "misrepresent them");
        }
    }

    /// <summary>
    /// The DevBypass profile's address, written out rather than read from configuration for the same
    /// reason the persona table above is: a test that derived its expectation from the thing under
    /// test would agree with a change that broke the correlation.
    /// </summary>
    /// <remarks>
    /// It is the literal in <c>Auth:DevBypass:Profiles:superadmin:Email</c>
    /// (<c>api/appsettings.Development.json</c>) and in
    /// <c>DevelopmentSeeder.SeedDevBypassCallerPersona</c>. All three have to agree or the local
    /// stack authenticates a caller the directory cannot resolve.
    /// </remarks>
    private const string DevBypassCallerEmail = "avery.quinn@example.com";

    [Fact]
    public void TheDevBypassProfile_InConfiguration_CarriesTheAddressBothSeedersUse()
    {
        // Arrange -- the third leg of the agreement this file's DevBypassCallerEmail remark asserts.
        // Two were already pinned: this seeder's row, and the legacy person in DevelopmentSeederTests.
        // The CONFIG was pinned by nothing, so editing Auth:DevBypass:Profiles:superadmin:Email --
        // the one edit a developer would make to sign in as somebody else -- silently unresolves the
        // caller. That failure is invisible: /Employees/Me answers 404, which is also what a broken
        // caller port answers, which is exactly the confusion FR-011 exists to prevent.
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
        File.Exists(configPath).ShouldBeTrue(
            $"{configPath} must be copied to the test output, or this test passes by not looking");

        // Act -- JsonDocument tolerates the comments this file contains; a plain parse would throw.
        using var config = JsonDocument.Parse(
            File.ReadAllText(configPath),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        var configured = config.RootElement
            .GetProperty("Auth").GetProperty("DevBypass").GetProperty("Profiles")
            .GetProperty("superadmin").GetProperty("Email").GetString();

        // Assert
        configured.ShouldBe(
            DevBypassCallerEmail,
            "the DevBypass profile's address, this seeder's compass.employee row and "
                + "DevelopmentSeeder's legacy person must be the same string, or the local stack "
                + "signs in a caller the directory cannot resolve (spec 019 FR-011)");
    }

    [Fact]
    public async Task SeedAsync_ProducesACompassCounterpart_ForTheDevBypassCaller()
    {
        // Arrange -- spec 019 FR-011. Before issue #427 this address was in NEITHER store, so the
        // local caller resolved to nothing and /Employees/Me answered not-found on every developer
        // machine -- the same symptom a broken caller port would produce, which is why the fixture
        // is a requirement rather than a convenience.
        using var context = await SeededContextAsync();

        // Act
        var counterpart = await context
            .Set<Employee>()
            .SingleOrDefaultAsync(
                e => e.Email == DevBypassCallerEmail, TestContext.Current.CancellationToken);

        // Assert
        counterpart.ShouldNotBeNull(
            "the DevBypass caller has no compass.employee row, so the local stack cannot resolve "
                + "the signed-in caller and the feature is undemonstrable outside a hand-built "
                + "fixture");
        counterpart.IsActive.ShouldBeTrue();
        counterpart.CoachEmployeeId.ShouldBeNull(
            "the bypass profile holds every role, so coaching it would make the see-all and "
                + "see-scoped lists identical locally and hide a scope regression");
    }

    [Fact]
    public async Task SeedAsync_GivesTheOotoPersonas_AtLeastTwoDistinctTimezones()
    {
        // Arrange -- SC-001. One zone across every persona would let a bug that applies the first
        // row's timezone to every row pass every assertion above.
        using var context = await SeededContextAsync();
        var emails = OotoPersonaCounterparts.Select(p => p.Email).ToHashSet(StringComparer.Ordinal);

        // Act
        var zones = await context
            .Set<Employee>()
            .Where(e => emails.Contains(e.Email))
            .Select(e => e.Timezone)
            .Distinct()
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert -- five of the six US zones, so the OOTO list read demonstrably resolves per row.
        zones.Count.ShouldBeGreaterThanOrEqualTo(2);
        zones.ShouldContain("America/New_York");
        zones.ShouldContain("America/Chicago");
    }

    [Fact]
    public async Task SeedAsync_LeavesTheDeliberatelyUnmatchedPersonas_WithoutACompassCounterpart()
    {
        // Arrange -- FR-006 and SC-005 need an EDJEr with NO Compass record, and the legacy roster
        // already supplies two ideal ones. Seeding them would silently retire that coverage, which is
        // why their absence is asserted rather than merely left alone.
        //
        //   erin.ghost@example.test -- Person-INACTIVE but Employee-ACTIVE, so she DOES appear in
        //     OOTO's employee list (its filter is Employee.IsActive). She is therefore the unmatched
        //     ROW in a list of otherwise-matched rows.
        //   finn (no address at all) -- AbsorbedDirectorySeeder seeds Person.Email = null on purpose,
        //     so correlation is impossible for him by construction. Nothing to assert by address; he
        //     is named here so the next reader knows he is the second case and not an omission.
        using var context = await SeededContextAsync();

        // Act
        var erin = await context
            .Set<Employee>()
            .SingleOrDefaultAsync(
                e => e.Email == "erin.ghost@example.test", TestContext.Current.CancellationToken);

        // Assert
        erin.ShouldBeNull(
            "erin.ghost is the deliberately-unmatched ACTIVE persona: she is what proves an OOTO row "
                + "with no Compass counterpart reports an absent timezone rather than a substituted "
                + "default. Seeding her removes that coverage and nothing would fail.");
    }
}
