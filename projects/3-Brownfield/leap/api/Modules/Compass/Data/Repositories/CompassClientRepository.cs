using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>Client and billable-time-category data access, extending the shared repository base.</summary>
public class CompassClientRepository(
    LeapDbContext context,
    IClientStatusDerivation statusDerivation
) : ICompassClientRepository
{
    /// <inheritdoc />
    public async Task<
        IReadOnlyList<(
            Client Client,
            string? InvoiceFrequencyTypeName,
            bool HoldsCurrentAssignment,
            bool HasEverBeenAssigned
        )>
    > GetAllWithFrequencyNameAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var activeIds = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(statusDerivation.IsActive(today))
            .Select(client => client.Id)
            .ToListAsync(cancellationToken);

        var active = activeIds.ToHashSet();

        var everAssignedIds = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(statusDerivation.HasEverBeenAssigned())
            .Select(client => client.Id)
            .ToListAsync(cancellationToken);

        var everAssigned = everAssignedIds.ToHashSet();

        var rows = await context
            .Set<Client>()
            .AsNoTracking()
            .GroupJoin(
                context.Set<InvoiceFrequencyType>().AsNoTracking(),
                client => client.InvoiceFrequencyTypeId,
                frequencyType => frequencyType.Id,
                (client, frequencyTypes) => new { Client = client, FrequencyTypes = frequencyTypes }
            )
            .SelectMany(
                row => row.FrequencyTypes.DefaultIfEmpty(),
                (row, frequencyType) =>
                    new
                    {
                        row.Client,
                        InvoiceFrequencyTypeName = frequencyType == null
                            ? null
                            : frequencyType.TypeName,
                    }
            )
            .OrderBy(row => row.Client.ClientName)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row =>
                (
                    row.Client,
                    row.InvoiceFrequencyTypeName,
                    active.Contains(row.Client.Id),
                    everAssigned.Contains(row.Client.Id)
                )
            ),
        ];
    }

    /// <inheritdoc />
    public async Task<(bool HoldsCurrentAssignment, bool HasEverBeenAssigned)> GetStatusFactsAsync(
        int clientId,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        IQueryable<Client> client = context
            .Set<Client>()
            .AsNoTracking()
            .Where(row => row.Id == clientId);

        return (
            await client.AnyAsync(statusDerivation.IsActive(today), cancellationToken),
            await client.AnyAsync(statusDerivation.HasEverBeenAssigned(), cancellationToken)
        );
    }

    /// <inheritdoc />
    /// <remarks>Uses <c>AsNoTracking</c> since this read is never followed by a save.</remarks>
    public async Task<Client?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await context
            .Set<Client>()
            .Include(client => client.BillableTimeCategories)
            .FirstOrDefaultAsync(client => client.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ClientNameExistsAsync(
        string clientName,
        int? excludingId,
        CancellationToken cancellationToken
    )
    {
        var candidate = clientName.Trim();
        return await context
            .Set<Client>()
            .AsNoTracking()
            .AnyAsync(
                client => client.Id != excludingId && client.ClientName == candidate,
                cancellationToken
            );
    }

    /// <inheritdoc />
    public async Task<bool> ActiveInvoiceFrequencyTypeExistsAsync(
        int invoiceFrequencyTypeId,
        CancellationToken cancellationToken
    ) =>
        await context
            .Set<InvoiceFrequencyType>()
            .AsNoTracking()
            .AnyAsync(
                frequencyType =>
                    frequencyType.Id == invoiceFrequencyTypeId && frequencyType.IsActive,
                cancellationToken
            );

    /// <inheritdoc />
    public async Task<BillableTimeCategory?> GetCategoryAsync(
        int clientId,
        int categoryId,
        CancellationToken cancellationToken
    ) =>
        await context
            .Set<BillableTimeCategory>()
            .FirstOrDefaultAsync(
                category => category.Id == categoryId && category.ClientId == clientId,
                cancellationToken
            );

    /// <inheritdoc />
    /// <remarks>Case-sensitive, scoped to one client.</remarks>
    public async Task<bool> CategoryNameExistsAsync(
        int clientId,
        string categoryName,
        int? excludingId,
        CancellationToken cancellationToken
    )
    {
        var candidate = categoryName.Trim().ToLowerInvariant();
        return await context
            .Set<BillableTimeCategory>()
            .AsNoTracking()
            .AnyAsync(
                category =>
                    category.ClientId == clientId
                    && category.Id != excludingId
                    && category.CategoryName.ToLower() == candidate,
                cancellationToken
            );
    }

    /// <inheritdoc />
    public async Task AddAsync(Client client, CancellationToken cancellationToken) =>
        await context.Set<Client>().AddAsync(client, cancellationToken);

    /// <inheritdoc />
    public async Task AddCategoryAsync(
        BillableTimeCategory category,
        CancellationToken cancellationToken
    ) => await context.Set<BillableTimeCategory>().AddAsync(category, cancellationToken);
}
