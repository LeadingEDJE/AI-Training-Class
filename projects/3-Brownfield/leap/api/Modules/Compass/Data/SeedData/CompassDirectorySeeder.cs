using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Data.SeedData;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.SeedData;

/// <summary>
/// Seeds the seven <c>compass.*</c> tables with a synthetic development directory: a realistically
/// sized EDJEr roster, its clients, assignments and SOW history.
/// </summary>
/// <remarks>
/// Synthetic by design — no production data, scrubbed or otherwise; every email is
/// <c>@example.test</c>, because pseudonymised rows stay re-identifiable from hire dates and coach
/// chains. Everything satisfies the live schema, which carries constraints the timesheet tables do
/// not: a 51-value CHECK on <c>state_of_residence</c>, unique email, unique client name, unique
/// (client, category), <c>rate_increase</c> only on extensions, end-on-or-after-start on both dated
/// tables, and a GiST exclusion constraint over SOW periods whose <c>daterange</c> bounds are
/// INCLUSIVE — two SOWs sharing an endpoint already overlap, so each starts a day after the previous
/// ends. Dates are anchor-relative; generation is index arithmetic, never <c>Random</c>; idempotent.
/// </remarks>
public static class CompassDirectorySeeder
{
    // Lookup IDs are pinned so the anchor rows below can reference them without a lookup round-trip,
    // and so a developer can write them into a test fixture.

    /// <summary>The <c>Full Time</c> employee type.</summary>
    public const int FullTimeEmployeeTypeId = 1;

    /// <summary>The <c>Part Time</c> employee type.</summary>
    public const int PartTimeEmployeeTypeId = 2;

    /// <summary>The <c>1099</c> employee type.</summary>
    public const int ContractorEmployeeTypeId = 3;

    /// <summary>
    /// The <c>Intern</c> employee type. Seeded inactive — migration provenance only.
    /// </summary>
    /// <remarks>
    /// TPS <c>EmploymentStatus = 4</c>, and inactive on purpose: the type exists so a migrated row
    /// can say what TPS said, not so a person can classify a new hire as an intern.
    /// <c>CompassLookupService</c> serves only active types to the create/edit dropdown, which is
    /// what keeps that true.
    ///
    /// Moving this constant on its own would reclassify records — it and the ETL's
    /// <c>CompassLookups.InternEmployeeTypeId</c> must always agree with the database. The
    /// migration that renumbered it repointed every <c>compass.employee</c> row in the same
    /// transaction, so no EDJEr's classification changed.
    /// </remarks>
    public const int InternEmployeeTypeId = 4;

    /// <summary>The <c>Weekly</c> invoice frequency.</summary>
    public const int WeeklyInvoiceFrequencyTypeId = 1;

    /// <summary>The <c>Monthly</c> invoice frequency.</summary>
    public const int MonthlyInvoiceFrequencyTypeId = 2;

    /// <summary>The <c>Fixed Bid</c> invoice frequency.</summary>
    public const int FixedBidInvoiceFrequencyTypeId = 3;

    /// <summary>The internal EDJE client that backs the dashboard's "on the beach" tile.</summary>
    public const int InternalClientId = 1;

    /// <summary>
    /// How many EDJErs are generated on top of the 19 hand-authored anchors.
    /// </summary>
    /// <remarks>
    /// 19 + 78 = 97 was the whole roster when this comment first said "97 in total". It no longer is:
    /// feature 007 appended two fixtures and feature 018 appended eight OOTO persona counterparts. The
    /// total is deliberately not restated here — <c>Seeder_AssignsContiguousIdentifiersFromOne</c>
    /// asserts contiguity from 1 for whatever the count is, precisely so appending does not
    /// require a number to be maintained in a comment that nothing checks.
    /// </remarks>
    private const int GeneratedEmployeeCount = 78;

    /// <summary>How many clients are generated on top of the 10 hand-authored anchors.</summary>
    private const int GeneratedClientCount = 18;

    /// <summary>Anchor employees occupy 1-19, so generation starts at 20.</summary>
    private const int FirstGeneratedEmployeeId = 20;

    private const int FirstGeneratedClientId = 11;

    /// <summary>
    /// Anchor assignments occupy 1-19 and three fixtures occupy 20-22, so assignment id generation
    /// starts at 23. Employee id generation still starts at 20; only the assignment ids shifted.
    /// </summary>
    private const int FirstGeneratedAssignmentId = 23;

    /// <summary>
    /// Appended after the generated roster (20..97) so its id stays contiguous with
    /// <c>CompassSeedDeterminismTests.Seeder_AssignsContiguousIdentifiersFromOne</c>.
    /// </summary>
    private const int NoCoachRolloutAndExpiringSowEmployeeId = FirstGeneratedEmployeeId + GeneratedEmployeeCount;

    /// <summary>The next contiguous id after <see cref="NoCoachRolloutAndExpiringSowEmployeeId"/>.</summary>
    private const int LeftAndReturnedEmployeeId = NoCoachRolloutAndExpiringSowEmployeeId + 1;

    /// <summary>
    /// The first of the OOTO persona counterparts, appended after
    /// <see cref="LeftAndReturnedEmployeeId"/> so identifiers stay contiguous from 1.
    /// </summary>
    /// <remarks>
    /// Contiguity is required by
    /// <c>CompassSeedDeterminismTests.Seeder_AssignsContiguousIdentifiersFromOne</c>, and appending is
    /// the pattern the two fixtures directly above established. These must go at the end rather than
    /// among the anchors: every earlier identifier is referenced by an assignment, a SOW or a coach
    /// relationship, so inserting in the middle would renumber live fixtures, and a stored
    /// cross-module reference elsewhere would silently point at a different EDJEr.
    /// </remarks>
    private const int FirstOotoPersonaEmployeeId = LeftAndReturnedEmployeeId + 1;

    /// <summary>
    /// Deliberately far outside the hand-authored (1-19), fixture (20-22) and generated assignment
    /// id ranges, so these two fixtures cannot collide with a shifted generation count.
    /// </summary>
    private const int FeatureSevenAssignmentBaseId = 900;

    /// <summary>
    /// The client the OOTO persona counterparts are engaged at, appended after the generated clients
    /// so identifiers stay contiguous from 1.
    /// </summary>
    /// <remarks>
    /// A dedicated client rather than an existing one: every hand-authored client carries a
    /// load-bearing shape (internal, all-ended, never-assigned, missing MSA, missing NDA) and every
    /// generated one is a bulk-paging row, so adding assignees to any of them changes a population
    /// another feature's fixture counts. This one is new, so nothing counted it before.
    /// </remarks>
    public const int OotoPersonaClientId = FirstGeneratedClientId + GeneratedClientCount;

    /// <summary>
    /// Assignment ids for the OOTO persona engagements, clear of the hand-authored (1-22), generated
    /// and feature-007 (900+) ranges.
    /// </summary>
    private const int OotoPersonaAssignmentBaseId = 1000;

    /// <summary>
    /// Seeds the Compass directory relative to an explicit anchor date, so tests can pin every
    /// relative offset.
    /// </summary>
    /// <param name="context">The shared context.</param>
    /// <param name="today">The anchor date every relative offset is computed from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SeedAsync(
        LeapDbContext context,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        var seededAnything = false;

        if (!await context.Set<EmployeeType>().AnyAsync(cancellationToken))
        {
            context.Set<EmployeeType>().AddRange(EmployeeTypes());
            seededAnything = true;
        }

        if (!await context.Set<InvoiceFrequencyType>().AnyAsync(cancellationToken))
        {
            context.Set<InvoiceFrequencyType>().AddRange(InvoiceFrequencyTypes());
            seededAnything = true;
        }

        // The directory content is guarded as ONE unit rather than table by table: assignments,
        // SOWs and categories all reference the employees and clients created in the same pass, so a
        // partial re-run would produce dangling references against RESTRICT foreign keys.
        var directorySeeded = false;
        if (!await context.Set<Employee>().AnyAsync(cancellationToken))
        {
            context.Set<Client>().AddRange(Clients(today));
            context.Set<Employee>().AddRange(Employees(today));
            context.Set<ClientAssignment>().AddRange(Assignments(today));
            context.Set<Sow>().AddRange(Sows(today));
            context.Set<BillableTimeCategory>().AddRange(BillableTimeCategories());
            seededAnything = true;
            directorySeeded = true;
        }

        // The OOTO persona engagements back-fill on their OWN sentinel for the same reason as the
        // DevBypass row below: inside the guard they never reach a machine that has booted
        // `make dev-all` once, whose postgres volume survives `make dev-down`. The client is the
        // sentinel because it is the assignments' FK parent, so neither can exist without it, and the
        // assignees are read back by address when this pass did not create them.
        if (!await context.Set<Client>()
            .AnyAsync(c => c.Id == OotoPersonaClientId, cancellationToken))
        {
            var assignees = directorySeeded
                ? OotoPersonaAssigneeIds().ToList()
                : await context.Set<Employee>()
                    .Where(e => OotoPersonaAssigneeEmails.Contains(e.Email))
                    .Select(e => e.Id)
                    .ToListAsync(cancellationToken);

            // No assignee, no client: an engagement-free client fixes nothing and an assignment
            // without its EDJEr is a dangling RESTRICT foreign key.
            if (assignees.Count > 0)
            {
                context.Set<Client>().Add(OotoPersonaClient(today));
                context.Set<ClientAssignment>().AddRange(OotoPersonaAssignments(today, assignees));
                seededAnything = true;
            }
        }

        // The DevBypass caller's counterpart back-fills on its OWN sentinel, outside the
        // all-or-nothing guard above. That guard covers the directory as one unit because
        // assignments, SOWs and categories reference employees and clients created in the same pass;
        // this row has none of those. Inside the guard it would never appear on a machine that had
        // booted `make dev-all` once, and the caller would resolve to nothing (FR-011).
        if (!await context.Set<Employee>()
            .AnyAsync(e => e.Email == DevBypassCallerEmail, cancellationToken))
        {
            // The employee type is a FK parent and is seeded by the lookups block above, which has
            // its own guard, so it is present on a back-fill too.
            context.Set<Employee>().Add(DevBypassCallerCounterpart(today, DevBypassCallerEmployeeId));
            seededAnything = true;
        }

        if (!seededAnything)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken);

        // Explicit-ID inserts do not advance a Postgres identity sequence, so the first application
        // insert would collide with 23505. The resync is schema-aware and covers compass.* too.
        await PostgresSequenceSync.ResyncIdentitySequencesAsync(context, cancellationToken);
    }

    // Lookups (ERD-specified)

    private static IEnumerable<EmployeeType> EmployeeTypes() =>
    [
        new() { Id = FullTimeEmployeeTypeId, TypeName = "Full Time", IsActive = true },
        new() { Id = PartTimeEmployeeTypeId, TypeName = "Part Time", IsActive = true },
        new() { Id = ContractorEmployeeTypeId, TypeName = "1099", IsActive = true },
        // Inactive: migration provenance, never selectable for a new hire. See the id constants.
        new() { Id = InternEmployeeTypeId, TypeName = "Intern", IsActive = false },
    ];

    private static IEnumerable<InvoiceFrequencyType> InvoiceFrequencyTypes() =>
    [
        new() { Id = WeeklyInvoiceFrequencyTypeId, TypeName = "Weekly", IsActive = true },
        new() { Id = MonthlyInvoiceFrequencyTypeId, TypeName = "Monthly", IsActive = true },
        new() { Id = FixedBidInvoiceFrequencyTypeId, TypeName = "Fixed Bid", IsActive = true },
        // Exactly the ERD's seed set — no more. Both lookups are Super Admin-managed, so an inactive
        // row to exercise the "only active types are selectable" filter is something a developer
        // creates through the admin screen, not something the fixture invents. Seeding a value the
        // ERD does not define teaches a vocabulary the model does not have.
    ];

    // Clients

    private static List<Client> Clients(DateOnly today)
    {
        List<Client> clients =
        [
            // 1 — the internal client. Beach assignments point here; it has no contracts.
            new()
            {
                Id = InternalClientId,
                ClientName = "Leading EDJE (Internal)",
                IsInternal = true,
                MsaSignedDate = null,
                NdaSignedDate = null,
                InvoiceFrequencyTypeId = null,
            },
            new()
            {
                Id = 2,
                ClientName = "Nordhaven Logistics",
                IsInternal = false,
                MsaSignedDate = today.AddYears(-3),
                NdaSignedDate = today.AddYears(-3).AddDays(-14),
                InvoiceFrequencyTypeId = MonthlyInvoiceFrequencyTypeId,
            },
            new()
            {
                Id = 3,
                ClientName = "Cascade Mutual Insurance",
                IsInternal = false,
                MsaSignedDate = today.AddYears(-8),
                NdaSignedDate = today.AddYears(-8).AddDays(-21),
                InvoiceFrequencyTypeId = WeeklyInvoiceFrequencyTypeId,
            },
            new()
            {
                Id = 4,
                ClientName = "Brightpath Health Systems",
                IsInternal = false,
                MsaSignedDate = today.AddYears(-2),
                NdaSignedDate = today.AddYears(-2),
                InvoiceFrequencyTypeId = FixedBidInvoiceFrequencyTypeId,
            },
            // 5 — MSA on file, NDA missing. The client screen must render the gap, not crash.
            new()
            {
                Id = 5,
                ClientName = "Ardent Manufacturing Group",
                IsInternal = false,
                MsaSignedDate = today.AddYears(-2).AddMonths(-3),
                NdaSignedDate = null,
                InvoiceFrequencyTypeId = MonthlyInvoiceFrequencyTypeId,
            },
            // 6 — the mirror case: NDA on file, MSA missing.
            new()
            {
                Id = 6,
                ClientName = "Vantage Point Financial",
                IsInternal = false,
                MsaSignedDate = null,
                NdaSignedDate = today.AddYears(-1).AddMonths(-2),
                InvoiceFrequencyTypeId = WeeklyInvoiceFrequencyTypeId,
            },
            // 7 — every assignment has ENDED, so derived status must read Former (it read Inactive
            // before the Inactive/Former split). Paired with client 9 below, which has
            // never been assigned and so still reads Inactive, this seed demonstrates the split on
            // every client-status surface — which is what the Compass e2e directory spec asserts.
            new()
            {
                Id = 7,
                ClientName = "Silverline Retail Partners",
                IsInternal = false,
                MsaSignedDate = today.AddYears(-5),
                NdaSignedDate = today.AddYears(-5),
                InvoiceFrequencyTypeId = MonthlyInvoiceFrequencyTypeId,
            },
            // 8 — no invoicing cadence configured.
            new()
            {
                Id = 8,
                ClientName = "Quarry Ridge Energy",
                IsInternal = false,
                MsaSignedDate = today.AddMonths(-7),
                NdaSignedDate = today.AddMonths(-7),
                InvoiceFrequencyTypeId = null,
            },
            // 9 — NEVER assigned to anyone. Derived status Inactive with an empty history panel.
            // This is the ONLY seeded client that reads Inactive; contrast client 7.
            new()
            {
                Id = 9,
                ClientName = "Halcyon Media Works",
                IsInternal = false,
                MsaSignedDate = null,
                NdaSignedDate = null,
                InvoiceFrequencyTypeId = null,
            },
            new()
            {
                Id = 10,
                ClientName = "Thornbury Civic Trust",
                IsInternal = false,
                MsaSignedDate = today.AddMonths(-9),
                NdaSignedDate = today.AddMonths(-9),
                InvoiceFrequencyTypeId = FixedBidInvoiceFrequencyTypeId,
            },
        ];

        // Bulk clients, so the client directory needs paging and sorting.
        for (var i = 0; i < GeneratedClientCount; i++)
        {
            clients.Add(new Client
            {
                Id = FirstGeneratedClientId + i,
                ClientName = GeneratedClientNames[i],
                IsInternal = false,
                MsaSignedDate = i % 5 == 0 ? null : today.AddMonths(-12 - (i * 3)),
                NdaSignedDate = i % 7 == 0 ? null : today.AddMonths(-12 - (i * 3)),
                InvoiceFrequencyTypeId = (i % 4) switch
                {
                    0 => WeeklyInvoiceFrequencyTypeId,
                    1 => MonthlyInvoiceFrequencyTypeId,
                    2 => FixedBidInvoiceFrequencyTypeId,
                    _ => null,
                },
            });
        }

        return clients;
    }

    // Employees

    private static List<Employee> Employees(DateOnly today)
    {
        // Anchors. The coach chain runs 1 -> 2 -> 3 -> {8,9,10,14,15}, which is deep enough for the
        // directory's coach column and the no-coach notification path to both have real cases.
        List<Employee> employees =
        [
            // 1 — OFF the delivery team, and the only anchor the fixture already says so about:
            // assignment 1's note reads "Leadership — not client-billable".
            Anchor(1, "Marcus", "Bellweather", "marcus.bellweather", today.AddYears(-9), "OH", null, FullTimeEmployeeTypeId, isDeliveryTeam: false),
            Anchor(2, "Priya", "Raghunathan", "priya.raghunathan", today.AddYears(-6), "OH", 1, FullTimeEmployeeTypeId),
            Anchor(3, "Devon", "Okafor", "devon.okafor", today.AddYears(-4), "MI", 2, FullTimeEmployeeTypeId),
            // Hyphenated surname + mixed case, for partial/case-insensitive last-name search.
            Anchor(4, "Sarah-Jane", "McAllister", "sarah-jane.mcallister", today.AddYears(-3).AddMonths(-6), "OH", 2, FullTimeEmployeeTypeId),
            // Non-ASCII display name with an ASCII email — the pair the directory has to render.
            Anchor(5, "Tomás", "Iglesias", "tomas.iglesias", today.AddYears(-2), "TX", 2, FullTimeEmployeeTypeId),
            Anchor(6, "Willa", "Fontaine", "willa.fontaine", today.AddYears(-1).AddMonths(-3), "CA", 1, PartTimeEmployeeTypeId),
            Anchor(7, "Grant", "Ashby", "grant.ashby", today.AddMonths(-10), "FL", 1, ContractorEmployeeTypeId),
            // 8 — holds TWO concurrent assignments.
            Anchor(8, "Nadia", "Haddad", "nadia.haddad", today.AddYears(-2).AddMonths(-1), "NY", 3, FullTimeEmployeeTypeId),
            // 9 — on the beach, open-ended.
            Anchor(9, "Elliot", "Voss", "elliot.voss", today.AddYears(-1), "WA", 3, FullTimeEmployeeTypeId),
            // 10 — on the beach with a confirmed roll-on date.
            Anchor(10, "Ruth", "Delacroix", "ruth.delacroix", today.AddYears(-1).AddMonths(-4), "IL", 3, FullTimeEmployeeTypeId),
            // 11/12/13 — the three SOW-expiry cases the dashboard tile has to separate.
            Anchor(11, "Hector", "Salvatierra", "hector.salvatierra", today.AddYears(-2), "OH", 2, FullTimeEmployeeTypeId),
            Anchor(12, "Ingrid", "Halvorsen", "ingrid.halvorsen", today.AddYears(-2), "MN", 2, FullTimeEmployeeTypeId),
            Anchor(13, "Jamal", "Whitfield", "jamal.whitfield", today.AddYears(-3), "GA", 2, FullTimeEmployeeTypeId),
            // 14 — assignment ends today.
            Anchor(14, "Keiko", "Tanaka-Brooks", "keiko.tanaka-brooks", today.AddYears(-1).AddMonths(-2), "OR", 3, FullTimeEmployeeTypeId),
            // 15 — confirmed rollout, future end date.
            Anchor(15, "Lorenzo", "Batista", "lorenzo.batista", today.AddYears(-1).AddMonths(-6), "AZ", 3, FullTimeEmployeeTypeId),
            // 16 — INACTIVE, with a closed assignment. Email stays unique across active and inactive.
            Anchor(16, "Maeve", "O'Sullivan", "maeve.osullivan", today.AddYears(-5), "MA", 1, FullTimeEmployeeTypeId, isActive: false),
            // 17 — DC, the non-state member of the CHECK's 51 values.
            Anchor(17, "Nolan", "Pryce", "nolan.pryce", today.AddYears(-4), "DC", 1, PartTimeEmployeeTypeId),
            // 18 — the longest tenure, and the assignment with a gap between SOWs.
            Anchor(18, "Odessa", "Ferrante", "odessa.ferrante", today.AddYears(-8), "OH", 2, FullTimeEmployeeTypeId),
            // 19 — a new hire with NO assignment yet. The ERD's employee -> client_assignment
            // cardinality is 1 : 0..* ("An EDJEr can be assigned to 0 or many clients"), and this is
            // the zero case. Deliberately holds no rows in client_assignment.
            Anchor(19, "Soren", "Kirkpatrick", "soren.kirkpatrick", today.AddDays(-21), "OH", 2, FullTimeEmployeeTypeId),
        ];

        for (var i = 0; i < GeneratedEmployeeCount; i++)
        {
            var first = GeneratedFirstNames[i % GeneratedFirstNames.Length];
            var last = GeneratedLastNames[i / GeneratedFirstNames.Length];
            var typeId = i % 17 == 0
                ? ContractorEmployeeTypeId
                : i % 11 == 0
                    ? PartTimeEmployeeTypeId
                    : FullTimeEmployeeTypeId;

            employees.Add(Anchor(
                FirstGeneratedEmployeeId + i,
                first,
                last,
                $"{first}.{last}".ToLowerInvariant(),
                GeneratedHireDate(today, i),
                GeneratedStates[i % GeneratedStates.Length],
                // Coaches are the three anchor leads, all of which exist by construction.
                2 + (i % 3),
                typeId,
                isActive: GeneratedIsActive(i)));
        }

        // 98 — no coach, on one current assignment with a future end date, so she qualifies for the
        // confirmed-rollouts population (FR-004) and exercises the dashboard's and Availability
        // Report's compensating control for the coach notification that is skipped (FR-008, FR-013).
        // Not an expiring-SOWs fixture: that population is scoped to SOWs on open-ended assignments,
        // and the boundary rows live on the open-ended assignments 12 and 13 (SOWs at 89 and 90 days).
        employees.Add(Anchor(
            NoCoachRolloutAndExpiringSowEmployeeId,
            "Priyanka", "Solberg", "priyanka.solberg", today.AddYears(-1).AddMonths(-3), "WI",
            coachId: null, FullTimeEmployeeTypeId));

        // 99 — feature 007 T016: LEFT client 3 and returned. Two non-overlapping assignments on the
        // same EDJEr-client pair, required for the Client Assignment Duration report's per-pair sum
        // (FR-015) — nothing else seeded exercises a returning EDJEr, so the combined-tenure case
        // would otherwise be untested until a real one showed up in production.
        employees.Add(Anchor(
            LeftAndReturnedEmployeeId,
            "Desmond", "Achterberg", "desmond.achterberg", today.AddYears(-4), "CO",
            coachId: 2, FullTimeEmployeeTypeId));

        employees.AddRange(OotoPersonaCounterparts(today));

        // The DevBypass counterpart is NOT added here — it back-fills on its own sentinel in
        // SeedAsync, because this collection is only reachable on a virgin database. Its id is still
        // derived from this list rather than declared independently; see DevBypassCallerEmployeeId.
        return employees;
    }

    /// <summary>
    /// Compass counterparts for the OOTO development and end-to-end personas, correlated by email.
    /// </summary>
    /// <remarks>
    /// OOTO correlates on email because it holds a legacy <c>Guid</c> and Compass answers on an
    /// <c>int</c>; without these rows nothing resolves locally. The addresses are written out, not
    /// derived, because they must match the legacy side byte for byte: they are the same strings
    /// <c>web/timesheet/tests/e2e/helpers/auth-session.ts</c>'s <c>FIXTURE</c> table signs in as.
    ///
    /// State determines the zone through <see cref="TimezoneForState"/>, spreading these personas across
    /// five of the six US zones. <c>erin.ghost</c>, <c>finn</c> and <c>dana.former</c> get no counterpart:
    /// the first two are the no-match cases FR-006 needs, and <c>CompassDirectorySeederTests</c> fails if
    /// Erin is seeded. No coach; the five the legacy roster also holds hold one engagement, at
    /// <see cref="OotoPersonaClientId"/> alone.
    /// </remarks>
    // (first, last, address, state, hasLegacyCounterpart, delivery). The state selects the zone.
    // hasLegacyCounterpart says whether AbsorbedDirectorySeeder holds an active employee on the same
    // address, and so who is engaged at the OOTO persona client. Delivery is seeded independently of
    // what that seeder's own derivation decides: Blair, Casey, Hank and Ivy agree with it, while Alex,
    // Jules, Kira and Lane have no title and no current employment record and so derive false there.
    // Hoisted to a field so DevBypassCallerEmployeeId derives from its length, not a second count.
    private static readonly (string First, string Last, string Email, string State, bool HasLegacyCounterpart, bool Delivery)[] OotoPersonas =
    [
        ("Alex", "Coach", "alex.coach@example.test", "OH", true, true),               // Eastern
        ("Blair", "Dev", "blair.dev@example.test", "IL", true, true),                 // Central
        // Casey holds the title "Operations Manager" over there, which sits in the Operations
        // category, not Delivery.
        ("Casey", "Dev", "casey.dev@example.test", "CO", true, false),                // Mountain
        ("Hank", "Deliver", "hank.deliver@example.test", "CA", true, true),           // Pacific
        // Ivy is that seeder's own worked control for the derivation: a TERMINATED Delivery
        // employment record plus a CURRENT Operations one, which resolves to off the delivery team.
        ("Ivy", "Control", "ivy.control@example.test", "HI", true, false),            // Hawaiian
        ("Jules", "Whitfield", "jules.whitfield@example.test", "IL", false, true),    // Central
        ("Kira", "Larsen", "kira.larsen@example.test", "CO", false, true),  // Mountain
        ("Lane", "Reopener", "lane.reopener@example.test", "AK", false, true),        // Alaskan
    ];

    /// <summary>The DevBypass caller's address — must match `Auth:DevBypass` byte for byte.</summary>
    private const string DevBypassCallerEmail = "avery.quinn@example.com";

    /// <summary>Appended after the OOTO personas, derived from their count rather than declared.</summary>
    private static readonly int DevBypassCallerEmployeeId =
        FirstOotoPersonaEmployeeId + OotoPersonas.Length;

    private static IEnumerable<Employee> OotoPersonaCounterparts(DateOnly today)
    {
        return OotoPersonas.Select((persona, index) => new Employee
        {
            Id = FirstOotoPersonaEmployeeId + index,
            FirstName = persona.First,
            LastName = persona.Last,
            Email = persona.Email,
            // Two years, so nobody is a new-hire boundary case on the dashboard.
            HireDate = today.AddYears(-2),
            StateOfResidence = persona.State,
            Timezone = TimezoneForState(persona.State),
            CoachEmployeeId = null,
            EmployeeTypeId = FullTimeEmployeeTypeId,
            IsActive = true,
            IsDeliveryTeam = persona.Delivery,
            TimesheetRequired = true,
            CanSubmitUnder40 = false,
            IncludeInPayroll = true,
        });
    }

    /// <summary>
    /// The Compass counterpart of the DevBypass caller — the identity the local stack signs every
    /// request in as (FR-011).
    /// </summary>
    /// <remarks>
    /// Not one of the eight OOTO personas: those are fixture EDJErs the browser specs sign in as; this
    /// is the DEVELOPER, <c>Auth:DevBypass</c>'s default profile in
    /// <c>api/appsettings.Development.json</c>, whose address was in neither store — so
    /// <c>/api/v1/Employees/Me</c> answered not-found on every local stack. The legacy half is
    /// <c>DevelopmentSeeder.SeedDevBypassCallerPersona</c> on the same address; correlation is by
    /// email, so either row alone resolves nothing and the <c>leadingedje.com</c> address (not the
    /// <c>example.test</c> the other rows use) must match the profile after normalisation. No coach
    /// and no assignment, so the populations <c>CompassDirectorySeederTests</c> counts are untouched.
    /// </remarks>
    /// <param name="today">The business date the fixture is anchored to.</param>
    /// <param name="employeeId">The next contiguous identifier after the OOTO personas.</param>
    private static Employee DevBypassCallerCounterpart(DateOnly today, int employeeId) => new()
    {
        Id = employeeId,
        FirstName = "Avery",
        LastName = "Quinn",
        Email = DevBypassCallerEmail,
        HireDate = today.AddYears(-2),
        StateOfResidence = "OH",
        Timezone = TimezoneForState("OH"),
        CoachEmployeeId = null,
        EmployeeTypeId = FullTimeEmployeeTypeId,
        IsActive = true,
        TimesheetRequired = true,
        CanSubmitUnder40 = false,
        IncludeInPayroll = true,
    };

    // Deterministic per-index generation
    // Employees and their assignments are built in separate passes, so anything both passes depend
    // on is derived here rather than duplicated. Keeping tenure the single source for BOTH the hire
    // date and the assignment start is what guarantees no EDJEr is engaged before being hired.

    /// <summary>
    /// The fixture's timezone for an EDJEr living in <paramref name="state"/>.
    /// </summary>
    /// <remarks>
    /// Derived, not assigned, so the fixture stays coherent. Split states take their predominant
    /// zone: <c>AZ</c> maps to <c>America/Denver</c>, not <c>America/Phoenix</c>, which is outside
    /// the six. The migration keeps its own copy (<c>StateTimeZones</c> in
    /// <c>etl/tps-to-compass/</c>) so a fixture tweak cannot change what a production load writes,
    /// and <c>StateTimeZonesTests.EveryEntry_AgreesWithTheCompassSeedersOwnStateToZoneTable</c>
    /// plus a completeness test over all 51 codes pin it — which is why this method is public. To
    /// anchor a zone no persona covers, add a persona (as <c>HI</c> and <c>AK</c> have) rather than
    /// widening <c>GeneratedStates</c>, which re-deals the states of every generated row.
    /// </remarks>
    public static string TimezoneForState(string state) =>
        StateTimezones.TryGetValue(state, out var timezone)
            ? timezone
            : throw new InvalidOperationException(
                $"CompassDirectorySeeder has no timezone mapped for state '{state}'. Add it to "
                    + $"{nameof(StateTimezones)} — every one of the 51 codes must map to a member of "
                    + $"{nameof(UsTimeZones)}.{nameof(UsTimeZones.All)}.");

    /// <summary>Every US state code to its predominant zone. See <see cref="TimezoneForState"/>.</summary>
    private static readonly Dictionary<string, string> StateTimezones = BuildStateTimezones();

    private static Dictionary<string, string> BuildStateTimezones()
    {
        // Grouped by zone rather than written as 51 pairs: the grouping is the reviewable claim, and
        // a miscategorised state is visible as a state in the wrong row.
        (string Timezone, string[] States)[] byZone =
        [
            ("America/New_York",
                ["CT", "DE", "DC", "FL", "GA", "IN", "KY", "ME", "MD", "MA", "MI", "NH", "NJ", "NY",
                 "NC", "OH", "PA", "RI", "SC", "VT", "VA", "WV"]),
            ("America/Chicago",
                ["AL", "AR", "IL", "IA", "KS", "LA", "MN", "MS", "MO", "NE", "ND", "OK", "SD", "TN",
                 "TX", "WI"]),
            ("America/Denver", ["AZ", "CO", "ID", "MT", "NM", "UT", "WY"]),
            ("America/Los_Angeles", ["CA", "NV", "OR", "WA"]),
            ("Pacific/Honolulu", ["HI"]),
            ("America/Anchorage", ["AK"]),
        ];

        // A DUPLICATED code throws out of ToDictionary here; a MISSING one is caught by
        // TimezoneForState_MapsEveryUsStateCode_ToASupportedZone. Neither needs a guard in this
        // method, and a count check here would be an unrunnable restatement of that test. Every code
        // maps, so no row reaches the "unmapped non-blank" state FR-8.3b would have a load list and
        // block — and the migration's own copy guesses rather than rejecting for a timezone reason.
        return byZone
            .SelectMany(zone => zone.States.Select(state => (State: state, zone.Timezone)))
            .ToDictionary(pair => pair.State, pair => pair.Timezone, StringComparer.Ordinal);
    }

    /// <summary>Tenure in days for a generated EDJEr: 90 days to roughly eight years.</summary>
    private static int GeneratedTenureDays(int index) => 90 + ((index * 137) % 2900);

    private static DateOnly GeneratedHireDate(DateOnly today, int index) =>
        today.AddDays(-GeneratedTenureDays(index));

    /// <summary>Roughly one generated EDJEr in twenty-three has left.</summary>
    private static bool GeneratedIsActive(int index) => index % 23 != 0;

    /// <summary>
    /// The first engagement, always at least 14 days after the hire and at least 17 days before the
    /// anchor date — so it sits strictly inside the employment and never in the future.
    /// </summary>
    private static DateOnly GeneratedAssignmentStart(DateOnly today, int index)
    {
        var tenure = GeneratedTenureDays(index);
        var offset = 14 + ((index * 53) % Math.Max(15, tenure - 30));
        return GeneratedHireDate(today, index).AddDays(offset);
    }

    /// <summary>
    /// Builds one employee. The three time-tracking flags follow from the employee type: contractors
    /// are outside payroll, and anyone not full time may submit under forty hours.
    /// </summary>
    private static Employee Anchor(
        int id,
        string firstName,
        string lastName,
        string emailLocalPart,
        DateOnly hireDate,
        string state,
        int? coachId,
        int employeeTypeId,
        bool isActive = true,
        bool isDeliveryTeam = true) =>
        new()
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            Email = $"{emailLocalPart}@example.test",
            HireDate = hireDate,
            StateOfResidence = state,
            Timezone = TimezoneForState(state),
            CoachEmployeeId = coachId,
            EmployeeTypeId = employeeTypeId,
            IsActive = isActive,
            IsDeliveryTeam = isDeliveryTeam,
            TimesheetRequired = true,
            CanSubmitUnder40 = employeeTypeId != FullTimeEmployeeTypeId,
            IncludeInPayroll = employeeTypeId != ContractorEmployeeTypeId,
        };

    // Assignments

    private static List<ClientAssignment> Assignments(DateOnly today)
    {
        List<ClientAssignment> assignments =
        [
            // 1/2 — leadership carried on the internal client, open-ended and contract-free.
            Assignment(1, 1, InternalClientId, today.AddYears(-5), null, "Leadership — not client-billable."),
            Assignment(2, 2, InternalClientId, today.AddYears(-4), null, "Practice lead — internal allocation."),
            Assignment(3, 3, 2, today.AddYears(-2), null, null),
            Assignment(4, 4, 3, today.AddYears(-3), null, null),
            Assignment(5, 5, 4, today.AddMonths(-18), null, null),
            Assignment(6, 6, 5, today.AddMonths(-12), null, "Part-time allocation, three days a week."),
            Assignment(7, 7, 6, today.AddMonths(-9), null, null),
            // 8/9 — Nadia holds both at once; the directory must list them together.
            Assignment(8, 8, 2, today.AddMonths(-12), null, "Split allocation with Quarry Ridge."),
            Assignment(9, 8, 8, today.AddMonths(-6), null, "Split allocation with Nordhaven."),
            // 10 — on the beach, open-ended.
            Assignment(10, 9, InternalClientId, today.AddMonths(-2), null, "Available — between engagements."),
            // 11 — on the beach with a confirmed roll-on.
            Assignment(11, 10, InternalClientId, today.AddMonths(-1), today.AddDays(21), "Rolls onto Brightpath after the current sprint."),
            Assignment(12, 11, 3, today.AddMonths(-14), null, null),
            Assignment(13, 12, 4, today.AddMonths(-14), null, null),
            Assignment(14, 13, 5, today.AddMonths(-20), null, null),
            // 15 — ends TODAY.
            Assignment(15, 14, 10, today.AddMonths(-8), today, "Engagement closes today."),
            // 16 — confirmed rollout.
            Assignment(16, 15, 6, today.AddMonths(-11), today.AddDays(60), "Confirmed rollout at the end of the quarter."),
            // 17/19 — both of Silverline's assignments are closed, so it derives Former.
            Assignment(17, 16, 7, today.AddYears(-4), today.AddYears(-1), null),
            // 18 — seven years at one client, with a gap in the SOW history.
            Assignment(18, 18, 3, today.AddYears(-7), null, "Longest-running engagement."),
            Assignment(19, 17, 7, today.AddYears(-3), today.AddMonths(-18), null),
            // 20 — left a client and returned: employee 12's assignment 13 to client 4 is the
            // return leg, this is the earlier closed one, so the pair has two non-overlapping
            // assignments, which is what FR-015's per-pair sum needs. It starts on employee 12's
            // hire date, not before, because
            // SeedAsync_NoEmployeeIsAssignedToAClientBeforeTheirHireDate is a real invariant.
            Assignment(20, 12, 4, today.AddYears(-2), today.AddMonths(-18), "Earlier engagement, before returning."),
            // 21/22 — T057b: FUTURE START DATES (FR-032). These have not begun, so they must appear on
            // NO tile, in NO breakdown, in NO Availability section and in NO duration row (SC-011).
            // Both are attached to EDJErs who already hold an open-ended assignment, so neither changes
            // any rollout verdict — the fixtures isolate the future-start property itself. Nothing
            // seeded started in the future before this, which is why FR-032's defect went unseen.
            Assignment(21, 3, 5, today.AddDays(1), null, "Starts tomorrow — not yet an active assignment."),
            Assignment(22, 5, InternalClientId, today.AddDays(1), null, "Moves to internal allocation tomorrow."),
        ];

        // Bulk assignments over the assignable clients. Clients 1 (internal), 7 (all-ended) and 9
        // (never assigned) are deliberately excluded so their hand-authored edge cases survive.
        // Employee 19 is deliberately skipped — the ERD's zero-assignment case.
        var nextId = FirstGeneratedAssignmentId;
        for (var i = 0; i < GeneratedEmployeeCount; i++)
        {
            var employeeId = FirstGeneratedEmployeeId + i;
            var isActive = GeneratedIsActive(i);
            var start = GeneratedAssignmentStart(today, i);

            assignments.Add(Assignment(
                nextId++,
                employeeId,
                AssignableClientIds[i % AssignableClientIds.Length],
                start,
                // An EDJEr who has LEFT cannot still be on an engagement: the ERD's directory rule
                // reads a current assignment as "end_date IS NULL OR end_date >= today", so leaving
                // one open would show a departed EDJEr as currently assigned. Everyone else follows
                // the one-in-nine rule, which gives a natural mix of closed engagements and
                // confirmed rollouts depending on where the start landed.
                isActive
                    ? i % 9 == 0 ? start.AddDays(200 + (i % 100)) : null
                    : ClosedBefore(today, start),
                i % 6 == 0 ? "Renewal discussion pending." : null));

            // Every fifth EDJEr carries a second concurrent engagement.
            if (i % 5 == 0)
            {
                // Offset from the FIRST engagement rather than from today, so it also lands inside
                // the employment, and clamp it to yesterday so it is genuinely concurrent.
                var latest = Math.Max(1, today.DayNumber - start.DayNumber - 1);
                var secondStart = start.AddDays(Math.Min(7 + ((i * 29) % 60), latest));

                assignments.Add(Assignment(
                    nextId++,
                    employeeId,
                    AssignableClientIds[(i + 7) % AssignableClientIds.Length],
                    secondStart,
                    isActive ? null : ClosedBefore(today, secondStart),
                    null));
            }
        }

        // 98 — feature 007 T015. ONE current assignment whose end date is in the future: every active
        // assignment this EDJEr holds carries an end date, so FR-004's universal condition qualifies
        // her as a confirmed rollout. Because the assignment IS end-dated, the matching SOW below no
        // longer surfaces as an expiring SOW (that population is scoped to open-ended assignments) —
        // the expiring boundary rows live on assignments 12/13 instead.
        assignments.Add(Assignment(
            FeatureSevenAssignmentBaseId,
            NoCoachRolloutAndExpiringSowEmployeeId,
            5,
            today.AddMonths(-10),
            today.AddDays(52),
            "Confirmed rollout — no coach on file."));

        // 99 — feature 007 T016. Left client 3, then returned: two assignments on the SAME
        // EDJEr-client pair, non-overlapping (A-7), the first CLOSED and the second CURRENT. The
        // Client Assignment Duration report must sum both spans (FR-015), not just the current one.
        assignments.Add(Assignment(
            FeatureSevenAssignmentBaseId + 1,
            LeftAndReturnedEmployeeId,
            3,
            today.AddYears(-3),
            today.AddYears(-2),
            "First engagement — ended."));
        assignments.Add(Assignment(
            FeatureSevenAssignmentBaseId + 2,
            LeftAndReturnedEmployeeId,
            3,
            today.AddMonths(-18),
            null,
            "Re-engaged after eleven months away."));

        return assignments;
    }

    /// <summary>
    /// The client whose assignees are the OOTO personas the legacy roster also holds.
    /// </summary>
    /// <param name="today">The business date the fixture is anchored to.</param>
    private static Client OotoPersonaClient(DateOnly today) => new()
    {
        Id = OotoPersonaClientId,
        ClientName = "Meridian Trust (OOTO Personas)",
        IsInternal = false,
        MsaSignedDate = today.AddYears(-2),
        NdaSignedDate = today.AddYears(-2),
        InvoiceFrequencyTypeId = MonthlyInvoiceFrequencyTypeId,
    };

    /// <summary>
    /// One current, open-ended engagement at <see cref="OotoPersonaClientId"/> per assignee.
    /// </summary>
    /// <remarks>
    /// This is what makes the per-client employee list resolvable: a client's assignees come from
    /// <c>compass.client_assignment</c> and correlate back to the legacy roster by email, so an
    /// engagement is needed on an address both stores hold. Six months after a two-year hire date, so
    /// no engagement predates its EDJEr, and no SOW, since contract history is not what this fixture
    /// demonstrates.
    /// </remarks>
    /// <param name="today">The business date the fixture is anchored to.</param>
    /// <param name="employeeIds">The Compass employees to engage.</param>
    private static IEnumerable<ClientAssignment> OotoPersonaAssignments(
        DateOnly today, IEnumerable<int> employeeIds) =>
        employeeIds.Select((employeeId, ordinal) => Assignment(
            OotoPersonaAssignmentBaseId + ordinal,
            employeeId,
            OotoPersonaClientId,
            today.AddMonths(-6),
            null,
            "OOTO persona engagement — the per-client employee list resolves through this."));

    /// <summary>The addresses of the personas the legacy roster holds as active employees.</summary>
    private static readonly string[] OotoPersonaAssigneeEmails =
        [.. OotoPersonas.Where(p => p.HasLegacyCounterpart).Select(p => p.Email)];

    /// <summary>
    /// The ids <see cref="OotoPersonaCounterparts"/> gives those personas, for the pass that creates
    /// them: they are pending rather than queryable at that point.
    /// </summary>
    private static IEnumerable<int> OotoPersonaAssigneeIds() =>
        OotoPersonas
            .Select((persona, index) => (persona, index))
            .Where(pair => pair.persona.HasLegacyCounterpart)
            .Select(pair => FirstOotoPersonaEmployeeId + pair.index);

    /// <summary>
    /// An end date two-thirds of the way from <paramref name="start"/> to the anchor date: always on
    /// or after the start (the CHECK) and always in the past (so the engagement reads as closed).
    /// </summary>
    private static DateOnly ClosedBefore(DateOnly today, DateOnly start) =>
        start.AddDays(Math.Max(1, (today.DayNumber - start.DayNumber) * 2 / 3));

    private static ClientAssignment Assignment(
        int id,
        int employeeId,
        int clientId,
        DateOnly start,
        DateOnly? end,
        string? note) =>
        new()
        {
            Id = id,
            EmployeeId = employeeId,
            ClientId = clientId,
            StartDate = start,
            EndDate = end,
            Note = note,
        };

    // SOWs

    private static List<Sow> Sows(DateOnly today)
    {
        var sows = new List<Sow>();
        var nextId = 1;

        // Consecutive periods start one day after the previous ends: the exclusion constraint's
        // daterange is inclusive on both ends, so a shared endpoint is an overlap.
        //
        // HasPassedApplicationValidation is true for everything except LegacyMigrated, which is the
        // point of the flag: a migrated row has not been through the application's rules yet.
        void Add(int assignmentId, DateOnly start, DateOnly end, SowType type, bool rateIncrease, string? note = null) =>
            sows.Add(new Sow
            {
                Id = nextId++,
                ClientAssignmentId = assignmentId,
                SowStartDate = start,
                SowEndDate = end,
                SowType = type,
                RateIncrease = rateIncrease,
                HasPassedApplicationValidation = type != SowType.LegacyMigrated,
                Note = note,
            });

        // Assignments 1, 2, 10 and 11 are internal — no contracts at all.

        Add(3, today.AddYears(-2), today.AddYears(-1).AddDays(-1), SowType.InitialContract, rateIncrease: false);
        Add(3, today.AddYears(-1), today.AddMonths(6), SowType.SowExtension, rateIncrease: true);

        Add(4, today.AddYears(-3), today.AddYears(-2).AddDays(-1), SowType.InitialContract, rateIncrease: false);
        Add(4, today.AddYears(-2), today.AddYears(-1).AddDays(-1), SowType.SowExtension, rateIncrease: false);
        Add(4, today.AddYears(-1), today.AddMonths(3), SowType.SowExtension, rateIncrease: true);

        Add(5, today.AddMonths(-18), today.AddMonths(2), SowType.InitialContract, rateIncrease: false);
        Add(6, today.AddMonths(-12), today.AddDays(30), SowType.InitialContract, rateIncrease: false);
        Add(7, today.AddMonths(-9), today.AddDays(120), SowType.InitialContract, rateIncrease: false);
        Add(8, today.AddMonths(-12), today.AddDays(180), SowType.InitialContract, rateIncrease: false);
        Add(9, today.AddMonths(-6), today.AddDays(150), SowType.InitialContract, rateIncrease: false);

        // 12 — expires in 89 days with NO follow-on: the tile must show it.
        Add(12, today.AddMonths(-14), today.AddMonths(-2).AddDays(-1), SowType.InitialContract, rateIncrease: false);
        Add(12, today.AddMonths(-2), today.AddDays(89), SowType.SowExtension, rateIncrease: false);

        // 13 — expires in EXACTLY 90 days: the tile's "under 90" must exclude it.
        Add(13, today.AddMonths(-14), today.AddMonths(-2).AddDays(-1), SowType.InitialContract, rateIncrease: false);
        Add(13, today.AddMonths(-2), today.AddDays(90), SowType.SowExtension, rateIncrease: true);

        // 14 — expires in 45 days but a follow-on already exists, so the tile must NOT show it.
        Add(14, today.AddMonths(-20), today.AddMonths(-8).AddDays(-1), SowType.InitialContract, rateIncrease: false);
        Add(14, today.AddMonths(-8), today.AddDays(45), SowType.SowExtension, rateIncrease: false);
        Add(14, today.AddDays(46), today.AddDays(400), SowType.SowExtension, rateIncrease: true, note: "Follow-on signed early.");

        Add(15, today.AddMonths(-8), today, SowType.InitialContract, rateIncrease: false);
        Add(16, today.AddMonths(-11), today.AddDays(60), SowType.InitialContract, rateIncrease: false);
        // 17 — the TYPICAL shape a TPS load produces: one period per closed assignment, no
        // extensions (legacy TPS does not track them), clean dates. Loads as LegacyMigrated with
        // HasPassedApplicationValidation false.
        Add(17, today.AddYears(-4), today.AddYears(-1), SowType.LegacyMigrated, rateIncrease: false, note: "Migrated from TPS.");

        // 18 — a deliberate GAP between the first period and the second. Gaps are legitimate and must
        // never be flagged as a problem.
        Add(18, today.AddYears(-7), today.AddYears(-5).AddDays(-1), SowType.InitialContract, rateIncrease: false);
        Add(18, today.AddYears(-4), today.AddYears(-2).AddDays(-1), SowType.SowExtension, rateIncrease: true, note: "Re-engaged after a year off contract.");
        Add(18, today.AddYears(-2), today.AddMonths(9), SowType.SowExtension, rateIncrease: false);

        // 19 — the MESSY shape FR-053's exemption exists for, and the only place a developer can see
        // it without running a TPS load: two OVERLAPPING legacy periods, plus one whose end precedes
        // its start. Both database rules skip LegacyMigrated, so all three load; the first edit
        // through the application has to reconcile them before it will save. A feature that touches
        // SOWs must cope with these rows — which is exactly why they are in the fixture.
        Add(19, today.AddYears(-3), today.AddMonths(-24), SowType.LegacyMigrated, rateIncrease: false, note: "Migrated from TPS.");
        Add(19, today.AddMonths(-30), today.AddMonths(-18), SowType.LegacyMigrated, rateIncrease: false, note: "Migrated from TPS — overlaps the preceding period; unreconciled.");
        Add(19, today.AddMonths(-20), today.AddMonths(-22), SowType.LegacyMigrated, rateIncrease: false, note: "Migrated from TPS — end precedes start in the legacy record.");

        // 900 — a rollout contract that ends when the assignment does, 52 days out with no
        // follow-on. It does not surface as an expiring SOW, because that population is scoped to
        // open-ended assignments. Kept at 52 rather than 45/89/90 so it stays clear of the boundary
        // values the integration tests assert on by exact day count.
        Add(FeatureSevenAssignmentBaseId, today.AddMonths(-10), today.AddDays(52),
            SowType.InitialContract, rateIncrease: false);

        // Bulk SOW history: one or two consecutive annual periods per generated assignment, clamped
        // to the assignment window so a closed engagement never carries a contract that outlives
        // it. The upper bound excludes the fixtures seated at FeatureSevenAssignmentBaseId (900+);
        // without it the bulk generator adds a second SOW over the one hand-authored for assignment
        // 900, which overlaps it and trips ex_sow_no_overlap_per_assignment.
        foreach (var assignment in Assignments(today)
            .Where(a => a.Id is >= FirstGeneratedAssignmentId and < FeatureSevenAssignmentBaseId))
        {
            AddClamped(assignment, assignment.StartDate, assignment.StartDate.AddDays(364), SowType.InitialContract, rateIncrease: false);
            AddClamped(
                assignment,
                assignment.StartDate.AddDays(365),
                assignment.StartDate.AddDays(729),
                SowType.SowExtension,
                rateIncrease: assignment.Id % 3 == 0);
        }

        return sows;

        void AddClamped(ClientAssignment assignment, DateOnly start, DateOnly end, SowType type, bool rateIncrease)
        {
            if (assignment.EndDate is { } assignmentEnd)
            {
                if (start > assignmentEnd)
                {
                    return;
                }

                // Cannot invert the window: `start > assignmentEnd` already returned above, so
                // clamping `end` down keeps `end >= start`, and both call sites pass `end = start +
                // 364`. An `if (end < start) return;` guard stood here, unreachable for that
                // reason, and was removed rather than waived with a coverage exclusion. If a future
                // caller passes an end before its start, reinstate it with a covering case.
                end = end > assignmentEnd ? assignmentEnd : end;
            }

            Add(assignment.Id, start, end, type, rateIncrease);
        }
    }

    // Billable time categories

    private static List<BillableTimeCategory> BillableTimeCategories()
    {
        var categories = new List<BillableTimeCategory>();
        var nextId = 1;

        void Add(int clientId, string name, bool isActive = true) =>
            categories.Add(new BillableTimeCategory
            {
                Id = nextId++,
                ClientId = clientId,
                CategoryName = name,
                IsActive = isActive,
            });

        // "Development" recurs across clients on purpose: the unique index is per client, not global.
        Add(InternalClientId, "Internal — Non-Billable");
        Add(InternalClientId, "Professional Development");

        Add(2, "Development");
        Add(2, "Project Management");
        Add(2, "Support");

        Add(3, "Development");
        Add(3, "Quality Assurance");
        Add(3, "Business Analysis");
        Add(3, "Support");

        Add(4, "Development");
        Add(4, "Implementation");

        Add(5, "Development");
        Add(5, "Consulting");

        Add(6, "Development");
        Add(6, "Architecture");

        // A retired category on a dormant client — the admin screen needs an inactive row.
        Add(7, "Development", isActive: false);

        Add(8, "Development");
        Add(8, "Data Engineering");

        Add(10, "Consulting");

        // Client 9 deliberately has none, matching its empty assignment history.

        for (var i = 0; i < GeneratedClientCount; i++)
        {
            var clientId = FirstGeneratedClientId + i;
            Add(clientId, "Development");
            Add(clientId, "Support");
            if (i % 3 == 0)
            {
                Add(clientId, "Discovery");
            }
        }

        return categories;
    }

    // Generation pools

    /// <summary>
    /// Clients the bulk generator may assign to. Excludes the internal client (1), the all-ended
    /// client (7) and the never-assigned client (9), whose hand-authored shapes are load-bearing.
    /// </summary>
    private static readonly int[] AssignableClientIds =
    [
        2, 3, 4, 5, 6, 8, 10,
        11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28,
    ];

    /// <summary>
    /// Thirteen first names. Paired with <see cref="GeneratedLastNames"/> by
    /// <c>(i % 13, i / 13)</c>, so every generated combination — and therefore every email — is unique.
    /// </summary>
    private static readonly string[] GeneratedFirstNames =
    [
        "Amara", "Bennett", "Corinne", "Dashiell", "Esperanza", "Fitzgerald", "Giselle",
        "Hollis", "Imogen", "Jericho", "Katarina", "Leopold", "Mireille",
    ];

    /// <summary>Eight surnames — 104 unique pairings, comfortably above the generated roster size.</summary>
    private static readonly string[] GeneratedLastNames =
    [
        "Ashgrove", "Brightwater", "Castellanos", "Dunmore", "Eastbrook", "Fairweather",
        "Glenhaven", "Harrowgate",
    ];

    /// <summary>States the bulk roster spans, so the directory's state filter has real variety.</summary>
    private static readonly string[] GeneratedStates =
    [
        "OH", "MI", "IN", "KY", "PA", "NY", "TX", "CA", "FL", "GA", "NC", "CO", "WA", "IL", "TN", "AZ",
    ];

    /// <summary>Eighteen bulk client names, distinct from the ten anchors.</summary>
    private static readonly string[] GeneratedClientNames =
    [
        "Alderman Freight Systems", "Bracken Hollow Foods", "Cobalt Line Utilities",
        "Dunhaven Property Group", "Everlee Pharmaceuticals", "Foxglove Analytics",
        "Granite Bay Credit Union", "Harborview Shipping", "Ironwood Textiles",
        "Junction Peak Software", "Kestrel Aviation Services", "Larkspur Agricultural",
        "Meridian Stone Quarries", "Northgate Publishing", "Oakhurst Dental Group",
        "Pemberton Rail Holdings", "Quillon Security Systems", "Ravensworth Hotels",
    ];
}
