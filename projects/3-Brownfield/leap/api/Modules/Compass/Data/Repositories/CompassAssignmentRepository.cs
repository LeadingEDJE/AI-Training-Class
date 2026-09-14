using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <inheritdoc cref="ICompassAssignmentRepository" />
public class CompassAssignmentRepository(LeapDbContext context, IClientStatusDerivation statusDerivation)
    : ICompassAssignmentRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientAssignment>> GetAllAsync(CancellationToken cancellationToken) =>
        await context
            .Set<ClientAssignment>()
            .AsNoTracking()
            .Include(a => a.Employee)
            .Include(a => a.Client)
            .ThenInclude(c => c!.InvoiceFrequencyType)
            .Include(a => a.InvoiceFrequencyType)
            .Include(a => a.Sows)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<ClientAssignment?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await context
            .Set<ClientAssignment>()
            .Include(a => a.Employee)
            .Include(a => a.Client)
            .ThenInclude(c => c!.InvoiceFrequencyType)
            .Include(a => a.InvoiceFrequencyType)
            .Include(a => a.Sows)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(ClientAssignment assignment, CancellationToken cancellationToken) =>
        await context.Set<ClientAssignment>().AddAsync(assignment, cancellationToken);

    /// <inheritdoc />
    public Task RemoveAsync(ClientAssignment assignment, CancellationToken cancellationToken)
    {
        context.Set<ClientAssignment>().Remove(assignment);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> ActiveInvoiceFrequencyTypeExistsAsync(
        int invoiceFrequencyTypeId,
        CancellationToken cancellationToken) =>
        await context
            .Set<InvoiceFrequencyType>()
            .AsNoTracking()
            .AnyAsync(t => t.Id == invoiceFrequencyTypeId && t.IsActive, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientAssignment>> GetOpenAssignmentsByEmployeeAsync(
        int employeeId,
        CancellationToken cancellationToken) =>
        await context
            .Set<ClientAssignment>()
            .AsNoTracking()
            .Include(a => a.Client)
            .Where(a => a.EmployeeId == employeeId && a.EndDate == null)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Employee?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken) =>
        await context
            .Set<Employee>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

    /// <inheritdoc />
    public async Task<Client?> GetClientAsync(int clientId, CancellationToken cancellationToken) =>
        await context
            .Set<Client>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientPickerRowDto>> GetClientPickerRowsAsync(
        DateOnly today,
        CancellationToken cancellationToken)
    {
        IQueryable<Client> clients = context.Set<Client>().AsNoTracking();

        // Derived SET-WISE: one query answers "which of these are active" for the whole set — the
        // same pattern CompassReadRepository.GetClientDirectoryAsync uses, for the same AC-NFR-4
        // reason. Never filtered — the O6 regression test requires a zero-assignment client to
        // appear here even though it derives Inactive.
        var activeIds = await clients
            .Where(statusDerivation.IsActive(today))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        var active = activeIds.ToHashSet();

        // The second derived fact, also set-wise. Still never a filter: a Former client is as
        // selectable as an Inactive one, and O6 requires the zero-assignment client to appear too.
        var everAssignedIds = await clients
            .Where(statusDerivation.HasEverBeenAssigned())
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        var everAssigned = everAssignedIds.ToHashSet();

        var rows = await clients
            .Select(c => new { c.Id, c.ClientName })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(c => new ClientPickerRowDto
        {
            Id = c.Id,
            ClientName = c.ClientName,
            DerivedStatus = statusDerivation
                .StatusOfClient(active.Contains(c.Id), everAssigned.Contains(c.Id))
                .ToString(),
        })];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EdjerPickerRowDto>> GetActiveEdjerPickerRowsAsync(
        CancellationToken cancellationToken) =>
        await context
            .Set<Employee>()
            .AsNoTracking()
            .Where(e => e.IsActive)
            .Select(e => new EdjerPickerRowDto
            {
                Id = e.Id,
                DisplayName = e.FirstName + " " + e.LastName,
            })
            .ToListAsync(cancellationToken);
}
