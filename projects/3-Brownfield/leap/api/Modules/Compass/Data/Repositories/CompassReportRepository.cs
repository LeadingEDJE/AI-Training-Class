using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>Report-only Compass queries.</summary>
/// <remarks>
/// The query lives here, not in the service, because
/// <c>CompassBoundaryTests.RuleTwo_CompassEndpointsAndServices_ReferenceNoDataContext</c> forbids a
/// data context in a Compass service and FR-021 requires the duration summing be set-based.
///
/// Aggregate in SQL, construct the DTO in memory: ordering by a member of a type constructed inside
/// the projection does not translate, and the InMemory provider cannot catch it, so the unit suite
/// would stay green while the route answered HTTP 500. The grouped projection is anonymous and <see
/// cref="AssignmentDurationRowDto"/> is built afterwards, which is also what lets <see
/// cref="AssignmentDurationFormat"/> run at all.
/// </remarks>
public class CompassReportRepository(LeapDbContext context, IClientStatusDerivation statusDerivation)
    : ICompassReportRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AssignmentDurationRowDto>> GetAssignmentDurationAsync(
        DateOnly today, CancellationToken cancellationToken)
    {
        // The qualifying set: pairs holding at least one active assignment (FR-032), begun and not
        // ended. Two Wheres on the inner set rather than one combined expression, so the predicates
        // compose without expression-tree surgery. Internal ("beach") clients are excluded here
        // rather than on `contributing`, which already matches its rows to `activePairs` by
        // (EmployeeId, ClientId) — unlike the Assignment Start lookup below, which keeps them.
        var activePairs = context.Set<ClientAssignment>()
            .Where(statusDerivation.IsCurrent(today))
            .Where(statusDerivation.HasStarted(today))
            .Where(a => !a.Client!.IsInternal);

        // Only STARTED assignments contribute. That is also why no zero floor is needed: a
        // not-yet-started assignment would yield a negative span, and excluding it is exactly
        // equivalent to FR-015's "floored at zero".
        var contributing = context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(statusDerivation.HasStarted(today))
            .Where(a => activePairs.Any(
                active => active.EmployeeId == a.EmployeeId && active.ClientId == a.ClientId));

        // FR-015's span rule as two sums, not one conditional: Sum(min(EndDate, today) - StartDate
        // + 1) needs an `EndDate > today` comparison that ClientStatusSingleDerivationTests fails the
        // build on outside the derivation (FR-017, BR-11). So gross = Σ (EndDate ?? today) - StartDate
        // + 1 over-counts a future end date by exactly unserved = Σ EndDate - today over IsFutureDated
        // — the legs FR-015 credits elapsed tenure only — and TotalDays = gross - unserved.
        var gross = await contributing
            .GroupBy(a => new
            {
                a.EmployeeId,
                EmployeeName = a.Employee!.FirstName + " " + a.Employee.LastName,
                a.ClientId,
                ClientName = a.Client!.ClientName,
                CoachName = a.Employee.Coach != null
                    ? a.Employee.Coach.FirstName + " " + a.Employee.Coach.LastName
                    : null,
                // Rendered as its own column following the name, replacing an inline-badge
                // treatment — the same treatment GetAssignmentStartsAsync gives it below.
                EmployeeType = a.Employee.EmployeeType != null
                    ? a.Employee.EmployeeType.TypeName
                    : null,
            })
            .Select(g => new
            {
                g.Key.EmployeeId,
                g.Key.EmployeeName,
                g.Key.ClientId,
                g.Key.ClientName,
                g.Key.CoachName,
                g.Key.EmployeeType,
                Days = g.Sum(a =>
                    ((a.EndDate ?? today).DayNumber - a.StartDate.DayNumber) + 1),
            })
            .ToListAsync(cancellationToken);

        var unserved = await contributing
            .Where(statusDerivation.IsFutureDated(today))
            .GroupBy(a => new { a.EmployeeId, a.ClientId })
            .Select(g => new
            {
                g.Key.EmployeeId,
                g.Key.ClientId,
                Days = g.Sum(a => a.EndDate!.Value.DayNumber - today.DayNumber),
            })
            .ToListAsync(cancellationToken);

        // TWO commands, constant regardless of data size — which is what FR-021's invariance test
        // (T061a) requires. Not one command per row, and not one per pair.
        var unservedByPair = unserved.ToDictionary(x => (x.EmployeeId, x.ClientId), x => x.Days);

        // Ordering and DTO construction happen HERE, after materialization, deliberately: ordering by a
        // member of a type constructed inside the projection does not translate, and the InMemory
        // provider cannot catch it. It is also the only place
        // AssignmentDurationFormat can run.
        return
        [
            .. gross
                .Select(row =>
                {
                    var totalDays = row.Days
                        - unservedByPair.GetValueOrDefault((row.EmployeeId, row.ClientId));

                    return new AssignmentDurationRowDto
                    {
                        EmployeeId = row.EmployeeId,
                        EmployeeName = row.EmployeeName,
                        EmployeeType = row.EmployeeType,
                        ClientId = row.ClientId,
                        ClientName = row.ClientName,
                        CoachName = row.CoachName,
                        TotalDays = totalDays,
                        DurationDisplay = AssignmentDurationFormat.Describe(totalDays),
                    };
                })
                .OrderByDescending(row => row.TotalDays)
                .ThenBy(row => row.EmployeeName, StringComparer.Ordinal)
                .ThenBy(row => row.ClientName, StringComparer.Ordinal),
        ];
    }

    // Assignment Start lookup (AC-40)

    /// <inheritdoc />
    /// <remarks>
    /// Inclusive at both ends (<c>&gt;= from</c>, <c>&lt;= to</c>): a half-open upper bound would
    /// silently drop every assignment starting on the last day of the requested range. Sorted
    /// earliest-first, then by EDJEr name, so the result is deterministic.
    ///
    /// Ordered and projected so the sort applies to
    /// an anonymous projection over source columns and the named DTO is built in memory afterwards,
    /// because the alternative answers HTTP 500 against Postgres while InMemory evaluates it
    /// happily. The inline start-date filter is deliberate —
    /// <c>ClientStatusSingleDerivationTests</c> fences <c>EndDate</c> comparisons only.
    /// </remarks>
    public async Task<IReadOnlyList<AssignmentStartRowDto>> GetAssignmentStartsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var rows = await context.Set<ClientAssignment>()
            .AsNoTracking()
            .Where(a => a.StartDate >= from && a.StartDate <= to)
            .Select(a => new
            {
                EmployeeName = a.Employee!.FirstName + " " + a.Employee.LastName,
                // Rendered as its own column following the name — the same treatment
                // GetAssignmentDurationAsync gives it above.
                EmployeeType = a.Employee.EmployeeType != null
                    ? a.Employee.EmployeeType.TypeName
                    : null,
                ClientName = a.Client!.ClientName,
                a.StartDate,
            })
            .OrderBy(row => row.StartDate)
            .ThenBy(row => row.EmployeeName)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new AssignmentStartRowDto
        {
            EmployeeName = row.EmployeeName,
            EmployeeType = row.EmployeeType,
            ClientName = row.ClientName,
            StartDate = row.StartDate,
        })];
    }

    // SOW Extension Report

    /// <inheritdoc />
    /// <remarks>
    /// Inclusive at both ends, matching <see cref="GetAssignmentStartsAsync"/> — a half-open upper
    /// bound would silently drop every extension starting on the last day of the requested range.
    /// Sorted oldest extension start date first, then by EDJEr name, so two extensions starting on
    /// the same day do not swap places between calls.
    ///
    /// Ordered and projected so the sort applies to
    /// an anonymous projection over source columns and the named DTO is built in memory afterwards
    /// — see the class remarks and <see cref="GetAssignmentStartsAsync"/> for why.
    /// </remarks>
    public async Task<IReadOnlyList<SowExtensionRowDto>> GetSowExtensionsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var rows = await context.Set<Sow>()
            .AsNoTracking()
            .Where(s => s.SowType == SowType.SowExtension)
            .Where(s => s.SowStartDate >= from && s.SowStartDate <= to)
            .Select(s => new
            {
                EmployeeName = s.ClientAssignment!.Employee!.FirstName + " " + s.ClientAssignment.Employee.LastName,
                EmployeeType = s.ClientAssignment.Employee.EmployeeType != null
                    ? s.ClientAssignment.Employee.EmployeeType.TypeName
                    : null,
                ClientName = s.ClientAssignment.Client!.ClientName,
                s.SowStartDate,
            })
            .OrderBy(row => row.SowStartDate)
            .ThenBy(row => row.EmployeeName)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new SowExtensionRowDto
        {
            EmployeeName = row.EmployeeName,
            EmployeeType = row.EmployeeType,
            ClientName = row.ClientName,
            ExtensionStartDate = row.SowStartDate,
        })];
    }
}
