using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>EF Core read-only repository backing the Compass directory boundary.</summary>
public class CompassDirectoryRepository(
    LeapDbContext context,
    IClientStatusDerivation statusDerivation,
    ICompassBusinessDate businessDate) : ICompassDirectoryRepository
{
    /// <inheritdoc />
    public async Task<Employee?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken)
        => await EmployeesWithTheirProjectionGraph()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

    /// <inheritdoc />
    public async Task<Employee?> GetEmployeeByEmailAsync(
        string? email, CancellationToken cancellationToken)
    {
        var key = MatchKey(email);
        if (key is null)
        {
            return null;
        }

        return await EmployeesWithTheirProjectionGraph()
            .FirstOrDefaultAsync(e => e.Email.Trim().ToLower() == key, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Employee>> GetEmployeesByEmailAsync(
        IReadOnlyCollection<string?> emails, CancellationToken cancellationToken)
    {
        var keys = emails
            .Select(MatchKey)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // A pure optimisation to skip an empty round trip.
        if (keys.Count == 0)
        {
            return [];
        }

        return await EmployeesWithTheirProjectionGraph()
            .Where(e => keys.Contains(e.Email.Trim().ToLower()))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Employee>> GetActiveEmployeesAsync(
        CancellationToken cancellationToken)
        => await EmployeesWithTheirProjectionGraph()
            .Where(e => e.IsActive)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(cancellationToken);

    /// <summary>The employee read shape every projection on this boundary needs, tracked for updates.</summary>
    private IQueryable<Employee> EmployeesWithTheirProjectionGraph()
        => context
            .Set<Employee>()
            .AsNoTracking()
            .Include(e => e.EmployeeType)
            .Include(e => e.Coach);

    /// <summary>Normalises a candidate address to the boundary's match key, or <c>null</c> when there is
    /// nothing to match on.</summary>
    private static string? MatchKey(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <inheritdoc />
    public async Task<IReadOnlyList<InvoiceFrequencyType>> GetInvoiceFrequenciesAsync(
        CancellationToken cancellationToken)
        => await context
            .Set<InvoiceFrequencyType>()
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.TypeName)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<(Client Client, string Status)?> GetClientAsync(
        int clientId, CancellationToken cancellationToken)
    {
        var today = businessDate.Today();

        var isActive = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(c => c.Id == clientId)
            .AnyAsync(statusDerivation.IsActive(today), cancellationToken);

        var hasEverBeenAssigned = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(c => c.Id == clientId)
            .AnyAsync(statusDerivation.HasEverBeenAssigned(), cancellationToken);

        var client = await context
            .Set<Client>()
            .AsNoTracking()
            .Include(c => c.InvoiceFrequencyType)
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        return client is null
            ? null
            : (client, statusDerivation.StatusOfClient(isActive, hasEverBeenAssigned).ToString());
    }

    /// <inheritdoc />
    /// <remarks>One query, with status resolved by a correlated subquery per row.</remarks>
    public async Task<IReadOnlyList<(Client Client, string Status)>> GetClientsAsync(
        CancellationToken cancellationToken)
    {
        var today = businessDate.Today();
        var clients = context.Set<Client>().AsNoTracking();

        var active = (await clients
                .Where(statusDerivation.IsActive(today))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var everAssigned = (await clients
                .Where(statusDerivation.HasEverBeenAssigned())
                .Select(c => c.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var rows = await clients
            .Include(c => c.InvoiceFrequencyType)
            .OrderBy(c => c.ClientName)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(client => (
                client,
                statusDerivation
                    .StatusOfClient(active.Contains(client.Id), everAssigned.Contains(client.Id))
                    .ToString())),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BillableTimeCategory>> GetBillableCategoriesAsync(
        int clientId, CancellationToken cancellationToken)
        => await context
            .Set<BillableTimeCategory>()
            .AsNoTracking()
            .Where(c => c.ClientId == clientId)
            .OrderBy(c => c.CategoryName)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<ClientAssignment?> GetAssignmentAsync(
        int assignmentId, CancellationToken cancellationToken)
        => await context
            .Set<ClientAssignment>()
            .AsNoTracking()
            .Include(a => a.Client!.InvoiceFrequencyType)
            .Include(a => a.InvoiceFrequencyType)
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientAssignment>> GetAssignmentsByEmployeeAsync(
        int employeeId, CancellationToken cancellationToken)
        => await context
            .Set<ClientAssignment>()
            .AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .Include(a => a.Client!.InvoiceFrequencyType)
            .Include(a => a.InvoiceFrequencyType)
            .OrderBy(a => a.StartDate)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientAssignment>> GetAssignmentsByClientAsync(
        int clientId, CancellationToken cancellationToken)
        => await context
            .Set<ClientAssignment>()
            .AsNoTracking()
            .Where(a => a.ClientId == clientId)
            .Include(a => a.Client!.InvoiceFrequencyType)
            .Include(a => a.InvoiceFrequencyType)
            .OrderBy(a => a.StartDate)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Sow>> GetSowsByAssignmentAsync(
        int clientAssignmentId, CancellationToken cancellationToken)
        => await context
            .Set<Sow>()
            .AsNoTracking()
            .Where(s => s.ClientAssignmentId == clientAssignmentId)
            .OrderBy(s => s.SowStartDate)
            .ToListAsync(cancellationToken);
}
