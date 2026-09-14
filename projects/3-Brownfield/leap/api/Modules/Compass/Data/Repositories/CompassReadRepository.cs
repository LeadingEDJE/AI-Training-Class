using System.Linq.Expressions;
using LeadingEDJE.Leap.Api.Modules.Compass.Authorization;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>EF Core reads backing the Compass application read surface (ADR-008).</summary>
/// <remarks>
/// Reached through <c>Set&lt;T&gt;()</c>, not a <c>DbSet</c> property (Option 2).
/// <c>AsNoTracking</c> throughout; no writes.
///
/// Three query shapes are load-bearing rather than stylistic: entitlement composes into the query
/// (FR-021, so a regular EDJEr never receives an inactive one); current assignments are projected
/// in the same round trip, because per-row loading is ~90 extra queries against AC-NFR-4; and both
/// the coach projection and its opposite, direct reports, are null-safe and filtered by <see
/// cref="EdjerVisibility"/>, since <c>coach_employee_id</c> is nullable and an inner join would
/// silently drop every coachless EDJEr.
/// </remarks>
public class CompassReadRepository(LeapDbContext context, IClientStatusDerivation statusDerivation)
    : ICompassReadRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TeamDirectoryRowDto>> GetTeamDirectoryAsync(
        TeamDirectoryQuery query,
        CompassTier tier,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var isCurrent = statusDerivation.IsCurrent(today);

        // FR-032: "current" means begun AND not ended. `IsCurrent` alone is only "not ended" — it
        // never reads StartDate — so a future-dated assignment rendered in CURRENT CLIENT(S) as
        // though the EDJEr were on it today. Caught by the team-directory visual baseline once
        // feature 007 seeded the first future-start assignment; before that, nothing in the seed
        // could expose it.
        var hasStarted = statusDerivation.HasStarted(today);
        var showsStatus = tier.SeesInactiveEdjers();

        // Entitlement FIRST and unconditionally: the status filter below can only narrow what
        // survives it, so no request parameter widens the result set (FR-011a).
        IQueryable<Employee> employees = context
            .Set<Employee>()
            .AsNoTracking()
            .Where(EdjerVisibility.For(tier));

        employees = query.Status switch
        {
            DirectoryStatusFilter.Active => employees.Where(e => e.IsActive),
            DirectoryStatusFilter.Inactive => employees.Where(e => !e.IsActive),
            _ => employees,
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // AC-6: partial, case-insensitive, LAST NAME only — widening it makes results unpredictable.
            var search = query.Search.Trim().ToLower();
            employees = employees.Where(e => e.LastName.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.EmployeeType))
        {
            var employeeType = query.EmployeeType.Trim();
            employees = employees.Where(e => e.EmployeeType != null && e.EmployeeType.TypeName == employeeType);
        }

        if (!string.IsNullOrWhiteSpace(query.State))
        {
            var state = query.State.Trim();
            employees = employees.Where(e => e.StateOfResidence == state);
        }

        if (query.CoachId is not null)
        {
            // Issue #655: narrows to one coach's team, by id rather than name — two coaches can
            // share a display name, and the row already carries CoachId for the drill-in link.
            employees = employees.Where(e => e.CoachEmployeeId == query.CoachId);
        }

        employees = Sort(employees, query.Sort, query.Descending, isCurrent, hasStarted);

        return await employees
            .Select(e => new TeamDirectoryRowDto
            {
                Id = e.Id,
                FirstName = e.FirstName,
                LastName = e.LastName,
                HireDate = e.HireDate,
                Email = e.Email,
                EmployeeType = e.EmployeeType != null ? e.EmployeeType.TypeName : string.Empty,

                // Null-conditional rather than a join, so a coachless EDJEr keeps their row.
                CoachId = e.Coach != null ? e.Coach.Id : (int?)null,
                Coach = e.Coach != null ? e.Coach.FirstName + " " + e.Coach.LastName : null,
                State = e.StateOfResidence,
                CurrentAssignments = e.ClientAssignments
                    .AsQueryable()
                    .Where(isCurrent)
                    .Where(hasStarted)
                    .Select(a => new TeamDirectoryAssignmentDto
                    {
                        ClientId = a.ClientId,
                        ClientName = a.Client != null ? a.Client.ClientName : string.Empty,
                    })
                    .ToList(),

                // Absent for a baseline viewer: their set is all-active, so it carries no information.
                IsActive = showsStatus ? e.IsActive : null,
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmployeeDetailDto?> GetEmployeeDetailAsync(
        int employeeId,
        CompassTier tier,
        string? viewerEmail,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var seesElevated = tier.SeesOthersSowsAndNotes();
        var seesTimeTracking = tier.SeesTimeTrackingSettings();

        // Ownership resolved as its own translatable query, not inside the projection: applying the
        // predicate there compiles it to a delegate invoked per row, which EF cannot translate — it
        // works against the in-memory provider and throws against Postgres.
        var ownsRecord = !seesElevated
            && await context
                .Set<Employee>()
                .AsNoTracking()
                .Where(e => e.Id == employeeId)
                .Where(OwnRecordMatch.IsOwnedBy(viewerEmail))
                .AnyAsync(cancellationToken);

        // Entitlement is part of the QUERY, not a check after loading: a baseline viewer asking for
        // an inactive EDJEr by id gets no row, which is what lets the endpoint answer "not found"
        // rather than "forbidden" (FR-021).
        var detail = await context
            .Set<Employee>()
            .AsNoTracking()
            .Where(EdjerVisibility.For(tier))
            .Where(e => e.Id == employeeId)
            .Select(e => new EmployeeDetailDto
            {
                Id = e.Id,
                FirstName = e.FirstName,
                LastName = e.LastName,
                HireDate = e.HireDate,
                Email = e.Email,
                EmployeeType = e.EmployeeType != null ? e.EmployeeType.TypeName : string.Empty,
                CoachId = e.Coach != null ? e.Coach.Id : (int?)null,
                Coach = e.Coach != null ? e.Coach.FirstName + " " + e.Coach.LastName : null,
                State = e.StateOfResidence,

                // Outside the tier branch below, deliberately: an ordinary directory attribute like
                // State, visible to every tier that can open the record (FR-004). A plain column
                // read, and it must stay one -- no OrderBy or Where over a member of the DTO
                // constructed here, which Npgsql cannot translate and the in-memory provider
                // evaluates happily.
                IsDeliveryTeam = e.IsDeliveryTeam,
                IsActive = seesElevated ? e.IsActive : null,
                // The org-chart direction opposite Coach/CoachId, filtered by the same visibility
                // rule as the directory itself (BR-1) and ordered BEFORE the projection, so this
                // stays the safe LINQ shape. Seniority
                // order: oldest hire date first, ties broken alphabetically by last name.
                DirectReports = e.Coachees
                    .AsQueryable()
                    .Where(EdjerVisibility.For(tier))
                    .OrderBy(c => c.HireDate)
                    .ThenBy(c => c.LastName)
                    .Select(c => new DirectReportDto
                    {
                        Id = c.Id,
                        FirstName = c.FirstName,
                        LastName = c.LastName,

                        // Lets an elevated viewer tell a former direct report apart from
                        // a current one. Gated the same as the top-level IsActive above (seesElevated).
                        IsActive = seesElevated ? c.IsActive : (bool?)null,
                    })
                    .ToList(),
                TimeTrackingSettings = seesTimeTracking
                    ? new TimeTrackingSettingsDto
                    {
                        TimesheetRequired = e.TimesheetRequired,
                        CanSubmitUnder40 = e.CanSubmitUnder40,
                        IncludeInPayroll = e.IncludeInPayroll,
                    }
                    : null,
                // Newest first, ordered BEFORE the projection on the entity's own StartDate column,
                // so this stays the safe LINQ shape —
                // ordering by a member of the DTO constructed below passes against InMemory and
                // throws "could not be translated" against Postgres. AssignmentId ties off equal
                // start dates so two same-day assignments do not flip order.
                AssignmentHistory = e.ClientAssignments
                    .OrderByDescending(a => a.StartDate)
                    .ThenByDescending(a => a.Id)
                    .Select(a => new EmployeeAssignmentDto
                    {
                        AssignmentId = a.Id,
                        ClientId = a.ClientId,
                        ClientName = a.Client != null ? a.Client.ClientName : string.Empty,

                        // The row's own client's internal-EDJE flag, so the screen can withhold
                        // "View SOWs" where the destination has no contract section. Null-tolerant
                        // like ClientName above; a null Client is a broken FK, not something a
                        // projection should throw on. Do not append an OrderBy or Where after this
                        // Select.
                        IsInternal = a.Client != null && a.Client.IsInternal,
                        StartDate = a.StartDate,
                        EndDate = a.EndDate,

                        // Elevated-only INCLUDING on one's own record: AC-11 grants SOWs, not notes.
                        Note = seesElevated ? a.Note : null,

                        // Elevated sees all; a baseline viewer only on their own record.
                        Sows = seesElevated || ownsRecord
                            ? a.Sows
                                .Select(sow => new EmployeeSowDto
                                {
                                    SowType = sow.SowType.ToString(),
                                    StartDate = sow.SowStartDate,
                                    EndDate = sow.SowEndDate,
                                    RateIncrease = seesElevated ? sow.RateIncrease : null,
                                    Note = seesElevated ? sow.Note : null,
                                })
                                .ToList()
                            : null,

                        // Elevated tiers only (AC-16/FR-025) — matches GetClientViewAsync's
                        // CanViewSow below; the assignment-detail endpoint refuses anyone else.
                        CanViewAssignment = seesElevated ? true : null,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (detail is null)
        {
            return null;
        }

        // AC-20's status per row, one set-wise query whose cost does not grow with the history's
        // length (AC-NFR-4). It cannot join the projection above: `IsCurrent` is an Expression and
        // the value comes from `From(...)`, a method call, and either inside a SQL projection
        // compiles to a delegate EF cannot translate. So the predicate runs in the database and the
        // word is applied here — hence `EmployeeAssignmentDto.Status` is its one settable member.
        var currentAssignmentIds = await context
            .Set<ClientAssignment>()
            .AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var current = currentAssignmentIds.ToHashSet();

        foreach (var assignment in detail.AssignmentHistory)
        {
            // From the derivation, so the two status words are named in exactly one file. The
            // ASSIGNMENT-level naming method: a row is current or it is not, and AssignmentStatus has
            // no Former member, so this level cannot acquire the client-level third value.
            assignment.Status = statusDerivation
                .StatusOfAssignment(current.Contains(assignment.AssignmentId))
                .ToString();
        }

        return detail;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientDirectoryRowDto>> GetClientDirectoryAsync(
        ClientDirectoryQuery query,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        IQueryable<Client> clients = context.Set<Client>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // AC-12: partial and case-insensitive, against the client name.
            var search = query.Search.Trim().ToLower();
            clients = clients.Where(c => c.ClientName.ToLower().Contains(search));
        }

        // Derived SET-WISE: one query answers "which of these are active" for the whole set. Per-row
        // evaluation is an N+1 against AC-NFR-4. Two queries regardless of how many clients exist.
        var activeIds = await clients
            .Where(statusDerivation.IsActive(today))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var active = activeIds.ToHashSet();

        // The second derived fact, also SET-WISE: one query answers "which of these have ever
        // been assigned" for the whole set. Three queries now, and still three however many clients
        // exist — the property AC-NFR-4 needs, and the one the command-count tests assert.
        var everAssignedIds = await clients
            .Where(statusDerivation.HasEverBeenAssigned())
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var everAssigned = everAssignedIds.ToHashSet();

        var rows = await clients
            .Select(c => new { c.Id, c.ClientName })
            .ToListAsync(cancellationToken);

        var projected = rows.Select(c => new ClientDirectoryRowDto
        {
            Id = c.Id,
            ClientName = c.ClientName,

            // From the derivation, so the enum's values are named in exactly one file.
            Status = statusDerivation
                .StatusOfClient(active.Contains(c.Id), everAssigned.Contains(c.Id))
                .ToString(),
        });

        var ordered = query.Sort?.Trim().ToLowerInvariant() switch
        {
            "status" => projected.OrderBy(r => r.Status, StringComparer.Ordinal)
                                 .ThenBy(r => r.ClientName, StringComparer.Ordinal),
            _ => projected.OrderBy(r => r.ClientName, StringComparer.Ordinal).ThenBy(r => r.Id),
        };

        return query.Descending ? [.. ordered.Reverse()] : [.. ordered];
    }

    /// <inheritdoc />
    public async Task<ClientViewDto?> GetClientViewAsync(
        int clientId,
        CompassTier tier,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var seesElevated = tier.SeesOthersSowsAndNotes();
        var seesConfiguration = tier.SeesTimeTrackingSettings();
        var seesInactive = tier.SeesInactiveEdjers();

        // The same component the Client Directory uses — which is what makes SC-005 true by
        // construction rather than by assertion.
        var isActive = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(c => c.Id == clientId)
            .AnyAsync(statusDerivation.IsActive(today), cancellationToken);

        // The second fact needed to tell "never engaged" (Inactive) from "engaged and stopped"
        // (Former). A separate round trip rather than a projected Any(), for the same reason isActive
        // is one: the predicate is an Expression the derivation owns, and inlining it into the
        // projection below would mean restating the rule here.
        var hasEverBeenAssigned = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(c => c.Id == clientId)
            .AnyAsync(statusDerivation.HasEverBeenAssigned(), cancellationToken);

        var view = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(c => new ClientViewDto
            {
                Id = c.Id,

                // Outside every optional section: AC-13 requires the name for EVERY tier, because
                // AC-14 hides the panel it would otherwise live in.
                ClientName = c.ClientName,
                Status = statusDerivation.StatusOfClient(isActive, hasEverBeenAssigned).ToString(),

                // Served to every tier, unlike the rest of Client Details — the
                // assignment history table needs it to decide whether the SOW column applies at all.
                IsInternal = c.IsInternal,
                ClientDetails = seesConfiguration
                    ? new ClientDetailsDto
                    {
                        MsaSignedDate = c.MsaSignedDate,
                        NdaSignedDate = c.NdaSignedDate,
                        IsInternal = c.IsInternal,
                        InvoiceFrequency = c.InvoiceFrequencyType != null
                            ? c.InvoiceFrequencyType.TypeName
                            : null,
                        BillableTimeCategories = c.BillableTimeCategories
                            .Select(b => b.CategoryName)
                            .ToList(),
                    }
                    : null,

                // The visibility invariant applies here too — one rule, three listings (FR-023).
                // Newest first on both Client Details and Admin/Edit Client, which this one
                // projection feeds; ordered BEFORE the projection on the entity's own StartDate
                // column, so this stays the safe LINQ shape, with AssignmentId tying off equal start dates.
                AssignmentHistory = c.ClientAssignments
                    .Where(a => seesInactive || (a.Employee != null && a.Employee.IsActive))
                    .OrderByDescending(a => a.StartDate)
                    .ThenByDescending(a => a.Id)
                    .Select(a => new ClientAssignmentHistoryDto
                    {
                        AssignmentId = a.Id,
                        EmployeeId = a.EmployeeId,
                        EmployeeName = a.Employee != null
                            ? a.Employee.FirstName + " " + a.Employee.LastName
                            : string.Empty,
                        StartDate = a.StartDate,
                        EndDate = a.EndDate,
                        EmployeeIsActive = seesInactive && a.Employee != null ? a.Employee.IsActive : null,

                        // An internal (EDJE-to-EDJE) client has no contracts, so no tier
                        // gets this affordance for its assignments.
                        CanViewSow = seesElevated && !c.IsInternal ? true : null,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (view is null)
        {
            return null;
        }

        // AC-24's status per row, the same set-wise shape GetEmployeeDetailAsync uses for AC-20 and
        // for the same AC-NFR-4 reason; `ClientAssignmentHistoryDto.Status` is its one settable member. The
        // key is `ClientId`, not `EmployeeId`: this shows one client's history, and an
        // employee-keyed filter would report a row as current because its EDJEr is busy elsewhere
        // (`CompassClientAssignmentStatusTests.TheStatusLookupIsScopedToThisClient_NotToTheEdjer`).
        var currentAssignmentIds = await context
            .Set<ClientAssignment>()
            .AsNoTracking()
            .Where(a => a.ClientId == clientId)
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var current = currentAssignmentIds.ToHashSet();

        foreach (var assignment in view.AssignmentHistory)
        {
            // From the derivation, so the two status words are named in exactly one file. The
            // ASSIGNMENT-level naming method: a row is current or it is not, and AssignmentStatus has
            // no Former member, so this level cannot acquire the client-level third value.
            assignment.Status = statusDerivation
                .StatusOfAssignment(current.Contains(assignment.AssignmentId))
                .ToString();
        }

        return view;
    }

    /// <summary>
    /// Applies AC-6's column sort, defaulting to hire date.
    /// </summary>
    /// <remarks>An unrecognised column falls back rather than throwing — it arrives from a query string.</remarks>
    private static IQueryable<Employee> Sort(
        IQueryable<Employee> employees,
        string? sort,
        bool descending,
        Expression<Func<ClientAssignment, bool>> isCurrent,
        Expression<Func<ClientAssignment, bool>> hasStarted)
    {
        var ascending = sort?.Trim().ToLowerInvariant() switch
        {
            "lastname" => employees.OrderBy(e => e.LastName).ThenBy(e => e.FirstName),
            "firstname" => employees.OrderBy(e => e.FirstName).ThenBy(e => e.LastName),
            "email" => employees.OrderBy(e => e.Email),
            "employeetype" => employees.OrderBy(e => e.EmployeeType!.TypeName).ThenBy(e => e.LastName),
            "state" => employees.OrderBy(e => e.StateOfResidence).ThenBy(e => e.LastName),
            "coach" => employees.OrderBy(e => e.Coach!.LastName).ThenBy(e => e.LastName),

            // Sorting by current clients (FR-010) keys on the alphabetically-first current client,
            // reusing `isCurrent` so the order matches what the cell renders; unassigned EDJErs
            // sort last because `FirstOrDefault()` yields NULL. A correlated subquery the unit
            // suite cannot catch — `CompassTeamDirectorySortTests.SortingByCurrentClients_*` are
            // the real-PostgreSQL cases; do not rewrite this branch without them.
            "currentclients" => employees
                .OrderBy(e => e.ClientAssignments
                    .AsQueryable()
                    .Where(isCurrent)
                    .Where(hasStarted)
                    .Select(a => a.Client!.ClientName)
                    .OrderBy(name => name)
                    .FirstOrDefault())
                .ThenBy(e => e.LastName),
            _ => employees.OrderBy(e => e.HireDate).ThenBy(e => e.LastName),
        };

        return descending ? Reverse(ascending) : ascending;
    }

    /// <summary>
    /// Reverses an ordered query, since <c>IOrderedQueryable</c> exposes no direction flip.
    /// </summary>
    /// <remarks>
    /// This DOES translate to SQL. Automated review flagged it as untranslatable under Npgsql
    /// and predicted a 500 on every <c>desc=true</c> request; that is wrong for EF Core 7+, which
    /// translates <c>Queryable.Reverse()</c> over an ordered query by inverting the ORDER BY.
    /// Verified rather than argued: <c>CompassTeamDirectorySortTests</c> exercises every sortable
    /// column in both directions against real PostgreSQL and asserts the reversal, and EF throws on
    /// untranslatable expressions rather than silently evaluating them client-side.
    /// The finding did expose a real gap, though — <c>desc=true</c> had no integration coverage at
    /// all, which is why those tests now exist. Keep them if this is ever rewritten.
    /// </remarks>
    private static IQueryable<Employee> Reverse(IQueryable<Employee> ordered) =>
        ordered.Reverse();
}
