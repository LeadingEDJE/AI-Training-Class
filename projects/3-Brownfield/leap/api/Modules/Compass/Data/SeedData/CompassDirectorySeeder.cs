using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Data.SeedData;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.SeedData;

/// <summary>
/// Seeds the five <c>compass.*</c> tables with a synthetic development directory: a realistically
/// sized EDJEr roster, its clients, assignments and SOW history.
/// </summary>
public static class CompassDirectorySeeder
{
    /// <summary>The <c>Full Time</c> employee type.</summary>
    public const int FullTimeEmployeeTypeId = 1;

    /// <summary>The <c>Part Time</c> employee type.</summary>
    public const int PartTimeEmployeeTypeId = 2;

    /// <summary>The <c>1099</c> employee type.</summary>
    public const int ContractorEmployeeTypeId = 3;

    /// <summary>The <c>Intern</c> employee type. Seeded active, and selectable for a new hire.</summary>
    public const int InternEmployeeTypeId = 4;

    /// <summary>The <c>Weekly</c> invoice frequency.</summary>
    public const int WeeklyInvoiceFrequencyTypeId = 1;

    /// <summary>The <c>Monthly</c> invoice frequency.</summary>
    public const int MonthlyInvoiceFrequencyTypeId = 2;

    /// <summary>The <c>Fixed Bid</c> invoice frequency.</summary>
    public const int FixedBidInvoiceFrequencyTypeId = 3;

    /// <summary>The internal EDJE client that backs the dashboard's "on the beach" tile.</summary>
    public const int InternalClientId = 1;

    /// <summary>How many EDJErs are generated on top of the 19 hand-authored anchors. 97 in total.</summary>
    private const int GeneratedEmployeeCount = 78;

    /// <summary>How many clients are generated on top of the 10 hand-authored anchors.</summary>
    private const int GeneratedClientCount = 18;

    /// <summary>Anchor employees occupy 1-19, so generation starts at 20.</summary>
    private const int FirstGeneratedEmployeeId = 20;

    private const int FirstGeneratedClientId = 11;

    /// <summary>Anchor assignments occupy 1-22, so assignment id generation starts at 23.</summary>
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

        if (!await context.Set<Client>()
            .AnyAsync(c => c.Id == OotoPersonaClientId, cancellationToken))
        {
            var assignees = directorySeeded
                ? OotoPersonaAssigneeIds().ToList()
                : await context.Set<Employee>()
                    .Where(e => OotoPersonaAssigneeEmails.Contains(e.Email))
                    .Select(e => e.Id)
                    .ToListAsync(cancellationToken);

            if (assignees.Count > 0)
            {
                context.Set<Client>().Add(OotoPersonaClient(today));
                context.Set<ClientAssignment>().AddRange(OotoPersonaAssignments(today, assignees));
                seededAnything = true;
            }
        }

        if (!await context.Set<Employee>()
            .AnyAsync(e => e.Email == DevBypassCallerEmail, cancellationToken))
        {
            context.Set<Employee>().Add(DevBypassCallerCounterpart(today, DevBypassCallerEmployeeId));
            seededAnything = true;
        }

        if (!seededAnything)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken);

        await PostgresSequenceSync.ResyncIdentitySequencesAsync(context, cancellationToken);
    }

    private static IEnumerable<EmployeeType> EmployeeTypes() =>
    [
        new() { Id = FullTimeEmployeeTypeId, TypeName = "Full Time", IsActive = true },
        new() { Id = PartTimeEmployeeTypeId, TypeName = "Part Time", IsActive = true },
        new() { Id = ContractorEmployeeTypeId, TypeName = "1099", IsActive = true },
        new() { Id = InternEmployeeTypeId, TypeName = "Intern", IsActive = false },
    ];

    private static IEnumerable<InvoiceFrequencyType> InvoiceFrequencyTypes() =>
    [
        new() { Id = WeeklyInvoiceFrequencyTypeId, TypeName = "Weekly", IsActive = true },
        new() { Id = MonthlyInvoiceFrequencyTypeId, TypeName = "Monthly", IsActive = true },
        new() { Id = FixedBidInvoiceFrequencyTypeId, TypeName = "Fixed Bid", IsActive = true },
    ];

    private static List<Client> Clients(DateOnly today)
    {
        List<Client> clients =
        [
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
            new()
            {
                Id = 5,
                ClientName = "Ardent Manufacturing Group",
                IsInternal = false,
                MsaSignedDate = today.AddYears(-2).AddMonths(-3),
                NdaSignedDate = null,
                InvoiceFrequencyTypeId = MonthlyInvoiceFrequencyTypeId,
            },
            // 6 — MSA on file, NDA missing, mirroring client 8 below.
            new()
            {
                Id = 6,
                ClientName = "Vantage Point Financial",
                IsInternal = false,
                MsaSignedDate = null,
                NdaSignedDate = today.AddYears(-1).AddMonths(-2),
                InvoiceFrequencyTypeId = WeeklyInvoiceFrequencyTypeId,
            },
            new()
            {
                Id = 7,
                ClientName = "Silverline Retail Partners",
                IsInternal = false,
                MsaSignedDate = today.AddYears(-5),
                NdaSignedDate = today.AddYears(-5),
                InvoiceFrequencyTypeId = MonthlyInvoiceFrequencyTypeId,
            },
            new()
            {
                Id = 8,
                ClientName = "Quarry Ridge Energy",
                IsInternal = false,
                MsaSignedDate = today.AddMonths(-7),
                NdaSignedDate = today.AddMonths(-7),
                InvoiceFrequencyTypeId = null,
            },
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
        List<Employee> employees =
        [
            Anchor(1, "Marcus", "Bellweather", "marcus.bellweather", today.AddYears(-9), "OH", null, FullTimeEmployeeTypeId, isDeliveryTeam: false),
            Anchor(2, "Priya", "Raghunathan", "priya.raghunathan", today.AddYears(-6), "OH", 1, FullTimeEmployeeTypeId),
            Anchor(3, "Devon", "Okafor", "devon.okafor", today.AddYears(-4), "MI", 2, FullTimeEmployeeTypeId),
            Anchor(4, "Sarah-Jane", "McAllister", "sarah-jane.mcallister", today.AddYears(-3).AddMonths(-6), "OH", 2, FullTimeEmployeeTypeId),
            Anchor(5, "Tomás", "Iglesias", "tomas.iglesias", today.AddYears(-2), "TX", 2, FullTimeEmployeeTypeId),
            Anchor(6, "Willa", "Fontaine", "willa.fontaine", today.AddYears(-1).AddMonths(-3), "CA", 1, PartTimeEmployeeTypeId),
            Anchor(7, "Grant", "Ashby", "grant.ashby", today.AddMonths(-10), "FL", 1, ContractorEmployeeTypeId),
            Anchor(8, "Nadia", "Haddad", "nadia.haddad", today.AddYears(-2).AddMonths(-1), "NY", 3, FullTimeEmployeeTypeId),
            Anchor(9, "Elliot", "Voss", "elliot.voss", today.AddYears(-1), "WA", 3, FullTimeEmployeeTypeId),
            Anchor(10, "Ruth", "Delacroix", "ruth.delacroix", today.AddYears(-1).AddMonths(-4), "IL", 3, FullTimeEmployeeTypeId),
            Anchor(11, "Hector", "Salvatierra", "hector.salvatierra", today.AddYears(-2), "OH", 2, FullTimeEmployeeTypeId),
            Anchor(12, "Ingrid", "Halvorsen", "ingrid.halvorsen", today.AddYears(-2), "MN", 2, FullTimeEmployeeTypeId),
            Anchor(13, "Jamal", "Whitfield", "jamal.whitfield", today.AddYears(-3), "GA", 2, FullTimeEmployeeTypeId),
            Anchor(14, "Keiko", "Tanaka-Brooks", "keiko.tanaka-brooks", today.AddYears(-1).AddMonths(-2), "OR", 3, FullTimeEmployeeTypeId),
            Anchor(15, "Lorenzo", "Batista", "lorenzo.batista", today.AddYears(-1).AddMonths(-6), "AZ", 3, FullTimeEmployeeTypeId),
            // 16 — the only INACTIVE anchor.
            Anchor(16, "Maeve", "O'Sullivan", "maeve.osullivan", today.AddYears(-5), "MA", 1, FullTimeEmployeeTypeId, isActive: false),
            Anchor(17, "Nolan", "Pryce", "nolan.pryce", today.AddYears(-4), "DC", 1, PartTimeEmployeeTypeId),
            Anchor(18, "Odessa", "Ferrante", "odessa.ferrante", today.AddYears(-8), "OH", 2, FullTimeEmployeeTypeId),
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
                2 + (i % 3),
                typeId,
                isActive: GeneratedIsActive(i)));
        }

        employees.Add(Anchor(
            NoCoachRolloutAndExpiringSowEmployeeId,
            "Priyanka", "Solberg", "priyanka.solberg", today.AddYears(-1).AddMonths(-3), "WI",
            coachId: null, FullTimeEmployeeTypeId));

        // Feature 018 T022: left client 4 and returned.
        employees.Add(Anchor(
            LeftAndReturnedEmployeeId,
            "Desmond", "Achterberg", "desmond.achterberg", today.AddYears(-4), "CO",
            coachId: 2, FullTimeEmployeeTypeId));

        employees.AddRange(OotoPersonaCounterparts(today));

        return employees;
    }

    /// <summary>Compass counterparts for the OOTO development and end-to-end personas, correlated by name.</summary>
    private static readonly (string First, string Last, string Email, string State, bool HasLegacyCounterpart, bool Delivery)[] OotoPersonas =
    [
        ("Alex", "Coach", "alex.coach@example.test", "OH", true, true),               // Eastern
        ("Blair", "Dev", "blair.dev@example.test", "IL", true, true),                 // Central
        ("Casey", "Dev", "casey.dev@example.test", "CO", true, false),                // Mountain
        ("Hank", "Deliver", "hank.deliver@example.test", "CA", true, true),           // Pacific
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

    /// <summary>One of the eight OOTO personas the browser specs sign in as.</summary>
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

    /// <summary>The fixture's timezone for an EDJEr living in <paramref name="state"/>. Internal only.</summary>
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

    /// <summary>The last engagement, always at least 17 days before the anchor date.</summary>
    private static DateOnly GeneratedAssignmentStart(DateOnly today, int index)
    {
        var tenure = GeneratedTenureDays(index);
        var offset = 14 + ((index * 53) % Math.Max(15, tenure - 30));
        return GeneratedHireDate(today, index).AddDays(offset);
    }

    /// <summary>Builds one employee, with time-tracking flags fixed to their defaults.</summary>
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
            Assignment(1, 1, InternalClientId, today.AddYears(-5), null, "Leadership — not client-billable."),
            Assignment(2, 2, InternalClientId, today.AddYears(-4), null, "Practice lead — internal allocation."),
            Assignment(3, 3, 2, today.AddYears(-2), null, null),
            Assignment(4, 4, 3, today.AddYears(-3), null, null),
            Assignment(5, 5, 4, today.AddMonths(-18), null, null),
            Assignment(6, 6, 5, today.AddMonths(-12), null, "Part-time allocation, three days a week."),
            Assignment(7, 7, 6, today.AddMonths(-9), null, null),
            Assignment(8, 8, 2, today.AddMonths(-12), null, "Split allocation with Quarry Ridge."),
            Assignment(9, 8, 8, today.AddMonths(-6), null, "Split allocation with Nordhaven."),
            Assignment(10, 9, InternalClientId, today.AddMonths(-2), null, "Available — between engagements."),
            Assignment(11, 10, InternalClientId, today.AddMonths(-1), today.AddDays(21), "Rolls onto Brightpath after the current sprint."),
            Assignment(12, 11, 3, today.AddMonths(-14), null, null),
            Assignment(13, 12, 4, today.AddMonths(-14), null, null),
            Assignment(14, 13, 5, today.AddMonths(-20), null, null),
            Assignment(15, 14, 10, today.AddMonths(-8), today, "Engagement closes today."),
            Assignment(16, 15, 6, today.AddMonths(-11), today.AddDays(60), "Confirmed rollout at the end of the quarter."),
            Assignment(17, 16, 7, today.AddYears(-4), today.AddYears(-1), null),
            // 18 — the longest-running engagement, still open-ended.
            Assignment(18, 18, 3, today.AddYears(-7), null, "Longest-running engagement."),
            Assignment(19, 17, 7, today.AddYears(-3), today.AddMonths(-18), null),
            Assignment(20, 12, 4, today.AddYears(-2), today.AddMonths(-18), "Earlier engagement, before returning."),
            Assignment(21, 3, 5, today.AddDays(1), null, "Starts tomorrow — not yet an active assignment."),
            Assignment(22, 5, InternalClientId, today.AddDays(1), null, "Moves to internal allocation tomorrow."),
        ];

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
                isActive
                    ? i % 9 == 0 ? start.AddDays(200 + (i % 100)) : null
                    : ClosedBefore(today, start),
                i % 6 == 0 ? "Renewal discussion pending." : null));

            if (i % 5 == 0)
            {
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

        assignments.Add(Assignment(
            FeatureSevenAssignmentBaseId,
            NoCoachRolloutAndExpiringSowEmployeeId,
            5,
            today.AddMonths(-10),
            today.AddDays(52),
            "Confirmed rollout — no coach on file."));

        // Left client 4, then returned.
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

    /// <summary>One current, open-ended engagement at <see cref="OotoPersonaClientId"/> per assignee.</summary>
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

    /// <summary>The ids already persisted for those personas, read back from the store.</summary>
    private static IEnumerable<int> OotoPersonaAssigneeIds() =>
        OotoPersonas
            .Select((persona, index) => (persona, index))
            .Where(pair => pair.persona.HasLegacyCounterpart)
            .Select(pair => FirstOotoPersonaEmployeeId + pair.index);

    /// <summary>An end date halfway between <paramref name="start"/> and the anchor date.</summary>
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

        // 12 — expires in exactly 90 days, at the "under 90" tile's boundary.
        Add(12, today.AddMonths(-14), today.AddMonths(-2).AddDays(-1), SowType.InitialContract, rateIncrease: false);
        Add(12, today.AddMonths(-2), today.AddDays(89), SowType.SowExtension, rateIncrease: false);

        Add(13, today.AddMonths(-14), today.AddMonths(-2).AddDays(-1), SowType.InitialContract, rateIncrease: false);
        Add(13, today.AddMonths(-2), today.AddDays(90), SowType.SowExtension, rateIncrease: true);

        Add(14, today.AddMonths(-20), today.AddMonths(-8).AddDays(-1), SowType.InitialContract, rateIncrease: false);
        Add(14, today.AddMonths(-8), today.AddDays(45), SowType.SowExtension, rateIncrease: false);
        Add(14, today.AddDays(46), today.AddDays(400), SowType.SowExtension, rateIncrease: true, note: "Follow-on signed early.");

        Add(15, today.AddMonths(-8), today, SowType.InitialContract, rateIncrease: false);
        Add(16, today.AddMonths(-11), today.AddDays(60), SowType.InitialContract, rateIncrease: false);
        Add(17, today.AddYears(-4), today.AddYears(-1), SowType.LegacyMigrated, rateIncrease: false, note: "Migrated from TPS.");

        Add(18, today.AddYears(-7), today.AddYears(-5).AddDays(-1), SowType.InitialContract, rateIncrease: false);
        Add(18, today.AddYears(-4), today.AddYears(-2).AddDays(-1), SowType.SowExtension, rateIncrease: true, note: "Re-engaged after a year off contract.");
        Add(18, today.AddYears(-2), today.AddMonths(9), SowType.SowExtension, rateIncrease: false);

        Add(19, today.AddYears(-3), today.AddMonths(-24), SowType.LegacyMigrated, rateIncrease: false, note: "Migrated from TPS.");
        Add(19, today.AddMonths(-30), today.AddMonths(-18), SowType.LegacyMigrated, rateIncrease: false, note: "Migrated from TPS — overlaps the preceding period; unreconciled.");
        Add(19, today.AddMonths(-20), today.AddMonths(-22), SowType.LegacyMigrated, rateIncrease: false, note: "Migrated from TPS — end precedes start in the legacy record.");

        Add(FeatureSevenAssignmentBaseId, today.AddMonths(-10), today.AddDays(52),
            SowType.InitialContract, rateIncrease: false);

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

        Add(7, "Development", isActive: false);

        Add(8, "Development");
        Add(8, "Data Engineering");

        Add(10, "Consulting");

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

    /// <summary>Every client the bulk generator may assign to, including the hand-authored anchors.</summary>
    private static readonly int[] AssignableClientIds =
    [
        2, 3, 4, 5, 6, 8, 10,
        11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28,
    ];

    /// <summary>Thirteen first names, randomly paired with <see cref="GeneratedLastNames"/>.</summary>
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
