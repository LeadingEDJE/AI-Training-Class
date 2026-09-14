using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>EF Core reads backing the Sales Dashboard (AC-36, AC-37).</summary>
/// <remarks>
/// Every count and breakdown is ONE query (FR-021); no writes. Confirmed rollouts is a per-EDJEr
/// universal condition shaped so an EDJEr with no current non-internal assignment forms no group and
/// cannot qualify vacuously (research D-2). Expiring SOWs chains IsExpiringWithin, HasNoFollowOn and
/// SowAssignmentIsOpenEndedAndActive as three <c>Where</c> clauses in one query; the third drops a SOW
/// whose assignment has an end date — a planned rollout's departure is handled, its SOW needs no alert.
///
/// One-row-per-EDJEr breakdowns use a correlated MAX/MIN subquery, then fold in memory:
/// <c>GroupBy(...).First()</c> passes every InMemory test and throws on real PostgreSQL, and a
/// correlated <c>=</c> matches every tied row, so the fold is what keeps SC-003's tile counts equal.
///
/// The active-assignments tile (issue #633) is one row per non-internal client assignment, keyed to
/// the MAX end date across every SOW that assignment owns — a correlated MAX in the SELECT list
/// rather than the WHERE clause the other three breakdowns use it in.
/// </remarks>
public class CompassDashboardRepository(LeapDbContext context, IClientStatusDerivation statusDerivation)
    : ICompassDashboardRepository
{
    /// <inheritdoc />
    public async Task<SalesDashboardDto> GetCountsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var isCurrent = statusDerivation.IsCurrent(today);

        // FR-032: "active" is IsCurrent AND HasStarted. IsCurrent alone means "not ended" — it never
        // reads StartDate — so a future-dated assignment would satisfy it and wrongly count on the
        // beach tile. Chained as a second Where rather than combined into one expression: EF ANDs
        // them in SQL and the alternative needs expression-tree surgery for no gain.
        var hasStarted = statusDerivation.HasStarted(today);
        var isExpiring = statusDerivation.IsExpiringWithin(today, 90);
        var hasNoFollowOn = statusDerivation.HasNoFollowOn();

        // Issue #633: the tile counts ACTIVE CLIENT ASSIGNMENTS, not SOWs — an EDJEr with a current,
        // started assignment at a non-internal client counts whether or not that assignment carries a
        // SOW at all, and one assignment with several SOWs still counts once. FR-002's internal-client
        // exclusion carries over unchanged.
        var activeSowCount = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(isCurrent)
            .Where(hasStarted)
            .Where(a => !a.Client!.IsInternal)
            .CountAsync(cancellationToken);

        var expiringSowCount = await context.Set<Sow>()
            .AsNoTracking()
            .Where(isExpiring)
            .Where(hasNoFollowOn)
            .Where(statusDerivation.SowAssignmentIsOpenEndedAndActive(today))
            .CountAsync(cancellationToken);

        var confirmedRolloutCount = await RolloutEmployeeIds(today).CountAsync(cancellationToken);

        var beachCount = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(isCurrent)
            .Where(hasStarted)
            .Where(a => a.Client!.IsInternal)
            .Select(a => a.EmployeeId)
            .Distinct()
            .CountAsync(cancellationToken);

        return new SalesDashboardDto
        {
            AsOfDate = today,
            ActiveSowCount = activeSowCount,
            ExpiringSowCount = expiringSowCount,
            ConfirmedRolloutCount = confirmedRolloutCount,
            BeachCount = beachCount,
        };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DashboardBreakdownRowDto>> GetBreakdownAsync(
        DashboardCategory category,
        DateOnly today,
        CancellationToken cancellationToken) => category switch
        {
            DashboardCategory.ActiveSows => ActiveSowsBreakdown(today, cancellationToken),
            DashboardCategory.ExpiringSows => ExpiringSowsBreakdown(today, cancellationToken),
            DashboardCategory.ConfirmedRollouts => ConfirmedRolloutsBreakdown(today, cancellationToken),
            DashboardCategory.Beach => BeachBreakdown(today, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(category), category, "unrecognised dashboard category"),
        };

    // Active client assignments (issue #633 — was "active SOWs")

    /// <summary>
    /// One row per active, non-internal client assignment — NOT one row per SOW (issue #633). An
    /// EDJEr with two current SOWs on the SAME assignment used to produce two rows and double-count
    /// the tile; the fix is to key this breakdown on the assignment and take the MAX end date across
    /// every SOW it owns, so an assignment with a 12/31 follow-on SOW never shows the earlier 9/30 one.
    /// </summary>
    private async Task<IReadOnlyList<DashboardBreakdownRowDto>> ActiveSowsBreakdown(
        DateOnly today, CancellationToken cancellationToken)
    {
        // Mirrors ConfirmedRolloutsBreakdown's shape (ClientAssignment → Employee/Client/Coach) rather
        // than ExpiringSowsBreakdown's — this is per-ASSIGNMENT now. MaxSowEndDate is a correlated MAX
        // over every SOW on the assignment (not just ones that are themselves currently active), so a
        // past or future SOW still counts toward "the latest end date under this assignment". `(DateOnly?)`
        // makes an assignment with no SOW at all come back null rather than throwing on an empty Max().
        var rows = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            // FR-002: exclude internal-client assignments — see GetCountsAsync. The same filter on the
            // count and the breakdown keeps the tile's count equal to its breakdown's row count.
            .Where(a => !a.Client!.IsInternal)
            .Select(a => new
            {
                AssignmentId = a.Id,
                EmployeeId = a.EmployeeId,
                EmployeeName = a.Employee!.FirstName + " " + a.Employee.LastName,
                EmployeeType = a.Employee.EmployeeType != null
                    ? a.Employee.EmployeeType.TypeName
                    : string.Empty,
                ClientId = a.ClientId,
                ClientName = a.Client!.ClientName,
                a.StartDate,
                MaxSowEndDate = context.Set<Sow>()
                    .Where(s => s.ClientAssignmentId == a.Id)
                    .Select(s => (DateOnly?)s.SowEndDate)
                    .Max(),
                CoachName = a.Employee.Coach != null
                    ? a.Employee.Coach.FirstName + " " + a.Employee.Coach.LastName
                    : null,
                // See the expiring-SOWs projection.
                CoachId = a.Employee.Coach != null ? a.Employee.Coach.Id : (int?)null,
            })
            .ToListAsync(cancellationToken);

        // One row per assignment. No urgency axis — an assignment merely being active is not an alert
        // the way an expiry inside 90 days is (FR-006 fixes an order only for the two urgency
        // categories), so DaysUntil is null. Deterministic default order: EDJEr name (Ordinal), then
        // the max SOW end date, then assignment id, which is unique, so the order is total. Imposed in
        // memory over the materialised list rather than by a member of the constructed DTO.
        return [.. rows
            .OrderBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.MaxSowEndDate)
            .ThenBy(r => r.AssignmentId)
            .Select(r => new DashboardBreakdownRowDto
            {
                EmployeeId = r.EmployeeId,
                EmployeeName = r.EmployeeName,
                EmployeeType = r.EmployeeType,
                Clients = [new DashboardBreakdownClientDto { Id = r.ClientId, Name = r.ClientName }],
                Date = r.MaxSowEndDate,
                DaysUntil = null,
                CoachName = r.CoachName,
                CoachId = r.CoachId,
                // No longer meaningful — a row is now an assignment, which can own several SOWs.
                SowId = null,
                // The assignment start date, shown alongside Date's (max SOW end) date.
                StartDate = r.StartDate,
            })];
    }

    // Expiring SOWs

    private async Task<IReadOnlyList<DashboardBreakdownRowDto>> ExpiringSowsBreakdown(
        DateOnly today, CancellationToken cancellationToken)
    {
        var rows = await context.Set<Sow>()
            .AsNoTracking()
            .Where(statusDerivation.IsExpiringWithin(today, 90))
            .Where(statusDerivation.HasNoFollowOn())
            .Where(statusDerivation.SowAssignmentIsOpenEndedAndActive(today))
            .OrderBy(s => s.SowEndDate)
            .Select(s => new
            {
                SowId = s.Id,
                EmployeeId = s.ClientAssignment!.EmployeeId,
                EmployeeName = s.ClientAssignment.Employee!.FirstName + " "
                    + s.ClientAssignment.Employee.LastName,
                EmployeeType = s.ClientAssignment.Employee.EmployeeType != null
                    ? s.ClientAssignment.Employee.EmployeeType.TypeName
                    : string.Empty,
                ClientId = s.ClientAssignment.ClientId,
                ClientName = s.ClientAssignment.Client!.ClientName,
                s.SowEndDate,
                CoachName = s.ClientAssignment.Employee.Coach != null
                    ? s.ClientAssignment.Employee.Coach.FirstName + " "
                        + s.ClientAssignment.Employee.Coach.LastName
                    : null,
                // See the active-sows projection above.
                CoachId = s.ClientAssignment.Employee.Coach != null
                    ? s.ClientAssignment.Employee.Coach.Id
                    : (int?)null,
            })
            .ToListAsync(cancellationToken);

        // One row per SOW. FR-006's DaysUntil-ascending order is already applied in SQL above; ties on
        // the same end date break on client name, then EDJEr name (owner request, issue #693), with
        // the SOW id as a final deterministic key since two SOWs can share client, EDJEr and end date.
        return [.. rows
            .OrderBy(r => r.SowEndDate)
            .ThenBy(r => r.ClientName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.SowId)
            .Select(r => new DashboardBreakdownRowDto
            {
                EmployeeId = r.EmployeeId,
                EmployeeName = r.EmployeeName,
                EmployeeType = r.EmployeeType,
                Clients = [new DashboardBreakdownClientDto { Id = r.ClientId, Name = r.ClientName }],
                Date = r.SowEndDate,
                DaysUntil = r.SowEndDate.DayNumber - today.DayNumber,
                CoachName = r.CoachName,
                CoachId = r.CoachId,
                SowId = r.SowId,
            })];
    }

    // Confirmed rollouts

    /// <summary>
    /// Employee ids for whom every current non-internal-client assignment carries an end date (FR-004),
    /// shaped so an employee with no such assignment forms no group and cannot qualify (research D-2).
    /// </summary>
    /// <remarks>
    /// An internal-client assignment (bench time, not client-facing work) is excluded before the
    /// condition is evaluated, mirroring how the Beach tile splits on internal versus non-internal: an
    /// open-ended internal assignment does not block rollout status, and an EDJEr with only internal
    /// assignments still forms no group.
    /// </remarks>
    private IQueryable<int> RolloutEmployeeIds(DateOnly today) =>
        context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Where(a => !a.Client!.IsInternal)
            .GroupBy(a => a.EmployeeId)
            .Where(g => g.Count() == g.Count(a => a.EndDate != null))
            .Select(g => g.Key);

    private async Task<IReadOnlyList<DashboardBreakdownRowDto>> ConfirmedRolloutsBreakdown(
        DateOnly today, CancellationToken cancellationToken)
    {
        var rolloutEmployeeIds = RolloutEmployeeIds(today);

        // FR-011: where an EDJEr holds more than one active NON-INTERNAL assignment, the row shows the
        // LATEST end date and that assignment's client — the date they actually become available. An
        // internal-client assignment is excluded here too, so it can never be the row
        // shown nor set the MAX that decides the date. A correlated MAX subquery, not
        // GroupBy+OrderBy+First (see class remarks).
        var rows = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Where(a => !a.Client!.IsInternal)
            .Where(a => rolloutEmployeeIds.Contains(a.EmployeeId))
            .Where(a => a.EndDate == context.Set<ClientAssignment>()
                .Where(statusDerivation.HasStarted(today))
                .Where(statusDerivation.IsCurrent(today))
                .Where(b => b.EmployeeId == a.EmployeeId && !b.Client!.IsInternal)
                .Max(b => b.EndDate))
            .OrderBy(a => a.EndDate)
            .Select(a => new
            {
                a.EmployeeId,
                EmployeeName = a.Employee!.FirstName + " " + a.Employee.LastName,
                EmployeeType = a.Employee.EmployeeType != null ? a.Employee.EmployeeType.TypeName : string.Empty,
                a.ClientId,
                ClientName = a.Client!.ClientName,
                a.EndDate,
                CoachName = a.Employee.Coach != null
                    ? a.Employee.Coach.FirstName + " " + a.Employee.Coach.LastName
                    : null,
                // See the active-sows projection above.
                CoachId = a.Employee.Coach != null ? a.Employee.Coach.Id : (int?)null,
            })
            .ToListAsync(cancellationToken);

        // One row per EDJEr, even when several assignments tie on that latest end date: the query
        // above returns a row per tied assignment, and folding them here keeps this breakdown's row
        // count equal to the tile's distinct-EDJEr count (SC-003). In memory rather than a SQL GROUP
        // BY, because grouping or ordering by a member of a type constructed inside a projection is
        // the shape that passes every InMemory test and throws only against real PostgreSQL.
        return [.. GroupToOneRowPerEmployee(
                rows.Select(r => (
                    r.EmployeeId,
                    r.EmployeeName,
                    r.EmployeeType,
                    Client: new DashboardBreakdownClientDto { Id = r.ClientId, Name = r.ClientName },
                    Date: (DateOnly?)r.EndDate,
                    DaysUntil: r.EndDate is { } end ? (int?)(end.DayNumber - today.DayNumber) : null,
                    // Confirmed rollouts' Date lies in the future — DaysAvailable is the beach
                    // category's own field.
                    DaysAvailable: (int?)null,
                    r.CoachName,
                    r.CoachId)))
            .OrderBy(r => r.DaysUntil)
            .ThenBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeId)];
    }

    // Beach

    private async Task<IReadOnlyList<DashboardBreakdownRowDto>> BeachBreakdown(
        DateOnly today, CancellationToken cancellationToken)
    {
        // One row per EDJEr, keyed to their EARLIEST current internal-assignment start date — an
        // EDJEr concurrently on two DIFFERENT internal clients is the edge case; the correlated MIN
        // subquery keeps this to one row per person the same way the rollout breakdown does.
        var rows = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Where(a => a.Client!.IsInternal)
            .Where(a => a.StartDate == context.Set<ClientAssignment>()
                .Where(statusDerivation.HasStarted(today))
                .Where(statusDerivation.IsCurrent(today))
                .Where(b => b.EmployeeId == a.EmployeeId && b.Client!.IsInternal)
                .Min(b => b.StartDate))
            .Select(a => new
            {
                a.EmployeeId,
                EmployeeName = a.Employee!.FirstName + " " + a.Employee.LastName,
                // Resolved here as it is for the other three categories, even though
                // neither reader renders it for this one — the Sales Dashboard's beach grid has no
                // Type column and the Availability Report's §1 row shape has no such field. The
                // shared DTO is uniform across all four categories by AC-37, and a per-category
                // hole in it is what makes a later reader's blank cell look like a data problem.
                EmployeeType = a.Employee.EmployeeType != null ? a.Employee.EmployeeType.TypeName : string.Empty,
                a.ClientId,
                ClientName = a.Client!.ClientName,
                a.StartDate,
                CoachName = a.Employee.Coach != null
                    ? a.Employee.Coach.FirstName + " " + a.Employee.Coach.LastName
                    : null,
                // See the active-sows projection above.
                CoachId = a.Employee.Coach != null ? a.Employee.Coach.Id : (int?)null,
            })
            .ToListAsync(cancellationToken);

        // One row per EDJEr, folding the two-internal-clients-started-the-same-day tie the way the
        // rollout breakdown folds its own — see there for why this is in memory. Ordered by start date
        // ascending: the EDJEr available longest is the one to staff first, the same most-urgent-first
        // principle FR-006 states for the two categories it covers. FR-009 calls this section
        // "chronologically-sorted", and FR-010 requires the two to read from one derivation.
        return [.. GroupToOneRowPerEmployee(
                rows.Select(r => (
                    r.EmployeeId,
                    r.EmployeeName,
                    r.EmployeeType,
                    Client: new DashboardBreakdownClientDto { Id = r.ClientId, Name = r.ClientName },
                    Date: (DateOnly?)r.StartDate,
                    DaysUntil: (int?)null,
                    // How long this EDJEr has been on the bench — today minus the
                    // internal-assignment start date the row is already keyed on.
                    DaysAvailable: (int?)(today.DayNumber - r.StartDate.DayNumber),
                    r.CoachName,
                    r.CoachId)))
            .OrderBy(r => r.Date)
            .ThenBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeId)];
    }

    // Shared

    /// <summary>
    /// Folds the rows of a per-EDJEr breakdown into exactly one row each, naming every client that
    /// tied on the date keying the category.
    /// </summary>
    /// <remarks>
    /// The date, days-until, days-available and coach (name and id both) are taken from the first row
    /// of the group because a group only ever forms over rows that already agree on them — the query
    /// selected them BY that date, and an EDJEr has one coach. Clients are ordered by name so a tie
    /// renders in an order a viewer can predict from the screen rather than in whatever order the
    /// database returned.
    /// </remarks>
    private static IEnumerable<DashboardBreakdownRowDto> GroupToOneRowPerEmployee(
        IEnumerable<(int EmployeeId, string EmployeeName, string EmployeeType,
            DashboardBreakdownClientDto Client, DateOnly? Date, int? DaysUntil, int? DaysAvailable,
            string? CoachName, int? CoachId)> rows) =>
        rows
            .GroupBy(r => r.EmployeeId)
            .Select(group => new DashboardBreakdownRowDto
            {
                EmployeeId = group.Key,
                EmployeeName = group.First().EmployeeName,
                EmployeeType = group.First().EmployeeType,
                Clients = [.. group
                    .Select(r => r.Client)
                    .OrderBy(client => client.Name, StringComparer.Ordinal)
                    .ThenBy(client => client.Id)],
                Date = group.First().Date,
                DaysUntil = group.First().DaysUntil,
                DaysAvailable = group.First().DaysAvailable,
                CoachName = group.First().CoachName,
                CoachId = group.First().CoachId,
            });
}
