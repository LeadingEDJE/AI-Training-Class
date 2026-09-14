using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <inheritdoc cref="ICompassSowRepository" />
public class CompassSowRepository(LeapDbContext context) : ICompassSowRepository
{
    /// <inheritdoc />
    /// <remarks>Issue #632 — oldest contract start date first, ordered on the entity's own column.</remarks>
    public async Task<IReadOnlyList<Sow>> GetByAssignmentIdAsync(
        int clientAssignmentId,
        CancellationToken cancellationToken) =>
        await context
            .Set<Sow>()
            .AsNoTracking()
            .Where(sow => sow.ClientAssignmentId == clientAssignmentId)
            .OrderBy(sow => sow.SowStartDate)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Sow?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await context.Set<Sow>().FirstOrDefaultAsync(sow => sow.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Sow sow, CancellationToken cancellationToken) =>
        await context.Set<Sow>().AddAsync(sow, cancellationToken);

    /// <inheritdoc />
    public Task RemoveAsync(Sow sow, CancellationToken cancellationToken)
    {
        context.Set<Sow>().Remove(sow);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Two SIBLING periods intersect when neither ends before the other starts — the candidate's own
    /// dates against the existing period's dates, never against a business date (research R-1). The
    /// existing exclusion constraint (<c>ex_sow_no_overlap_per_assignment</c>) is the same test over a
    /// Postgres <c>daterange</c>; this pre-check exists to name the conflicting period in a message
    /// before that constraint would otherwise raise an untranslated <c>23P01</c>.
    /// </remarks>
    public async Task<IReadOnlyList<Sow>> GetOverlappingAsync(
        int clientAssignmentId,
        DateOnly candidateStartDate,
        DateOnly candidateEndDate,
        int? excludingSowId,
        CancellationToken cancellationToken) =>
        await context
            .Set<Sow>()
            .AsNoTracking()
            .Where(sow => sow.ClientAssignmentId == clientAssignmentId)
            .Where(sow => excludingSowId == null || sow.Id != excludingSowId)
            .Where(sow => sow.SowStartDate <= candidateEndDate && candidateStartDate <= sow.SowEndDate)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> AssignmentExistsAsync(
        int clientAssignmentId,
        CancellationToken cancellationToken
    ) =>
        await context
            .Set<ClientAssignment>()
            .AnyAsync(a => a.Id == clientAssignmentId, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Ordered on an anonymous projection, DTO constructed in memory afterwards. Ordering
    /// by a member of a type constructed inside the projection is untranslatable by Npgsql and
    /// answers HTTP 500, while every unit test passes because the InMemory provider evaluates the
    /// expression tree in .NET.
    /// </remarks>
    public async Task<IReadOnlyList<CompassSowDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var rows = await context
            .Set<Sow>()
            .AsNoTracking()
            .Select(s => new
            {
                s.Id,
                s.ClientAssignmentId,
                s.SowType,
                s.RateIncrease,
                s.HasPassedApplicationValidation,
                s.SowStartDate,
                s.SowEndDate,
                s.Note,
            })
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new CompassSowDto(
                row.Id,
                row.ClientAssignmentId,
                row.SowType,
                row.RateIncrease,
                row.HasPassedApplicationValidation,
                row.SowStartDate,
                row.SowEndDate,
                row.Note
            )),
        ];
    }

    /// <inheritdoc />
    public async Task<CompassSowDto?> GetAsync(int id, CancellationToken cancellationToken) =>
        await context
            .Set<Sow>()
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new CompassSowDto(
                s.Id,
                s.ClientAssignmentId,
                s.SowType,
                s.RateIncrease,
                s.HasPassedApplicationValidation,
                s.SowStartDate,
                s.SowEndDate,
                s.Note
            ))
            .FirstOrDefaultAsync(cancellationToken);
}
