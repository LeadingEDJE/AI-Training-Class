using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <inheritdoc cref="ICompassSowRepository" />
public class CompassSowRepository(LeapDbContext context) : ICompassSowRepository
{
    /// <inheritdoc />
    /// <remarks>Issue #418 — newest contract start date first, ordered on the entity's own column.</remarks>
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
