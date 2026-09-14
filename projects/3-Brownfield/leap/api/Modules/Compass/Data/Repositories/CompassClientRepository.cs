using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>
/// Client and billable-time-category data access over the single application context.
/// </summary>
/// <remarks>
/// Reaches data through <c>context.Set&lt;T&gt;()</c> rather than a <c>DbSet</c> property, because
/// Compass declares none on the context — Option 2. No method
/// persists; that boundary is the service's (Principle III), reached through
/// <see cref="ICompassUnitOfWork"/>.
///
/// It does not extend <c>Repository&lt;T&gt;</c>, for the same reason
/// <see cref="CompassEmployeeRepository"/> does not: the base class's members match none of these
/// signatures, so inheritance buys no reuse while bringing a public <c>DeleteAsync</c> that neither a
/// client nor a category may ever have (Principle VIII).
/// </remarks>
public class CompassClientRepository(
    LeapDbContext context,
    IClientStatusDerivation statusDerivation
) : ICompassClientRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// A LEFT join, expressed as a <c>GroupJoin</c> + <c>SelectMany</c> with <c>DefaultIfEmpty()</c>.
    /// The invoice-frequency default is optional, so an inner join — the shape
    /// <see cref="CompassEmployeeRepository.GetAllWithTypeNameAsync"/> uses, where the FK is required
    /// — would silently omit every client that has no default: a filter wearing a join's clothes, and
    /// it would look correct in every test whose fixtures all set a cadence. Projected into an
    /// anonymous type and turned into the tuple afterwards, per the "Project into a named type LAST"
    /// rule. The status half is a separate set-wise query rather than a
    /// correlated subquery per row — see the comment in the body.
    /// </remarks>
    public async Task<
        IReadOnlyList<(
            Client Client,
            string? InvoiceFrequencyTypeName,
            bool HoldsCurrentAssignment,
            bool HasEverBeenAssigned
        )>
    > GetAllWithFrequencyNameAsync(DateOnly today, CancellationToken cancellationToken)
    {
        // Derived SET-WISE: ONE query answers "which of these hold a current assignment" for the whole
        // set, exactly as CompassReadRepository.GetClientDirectoryAsync does. Per-row evaluation is the
        // N+1 the contract (§ 2) forbids against AC-NFR-4, and the command-count test in the
        // integration suite fails if this ever becomes one query per client.
        var activeIds = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(statusDerivation.IsActive(today))
            .Select(client => client.Id)
            .ToListAsync(cancellationToken);

        var active = activeIds.ToHashSet();

        // The second derived fact, set-wise for the same reason: one query answers "which of
        // these have ever been assigned" for the whole set. THREE queries now, and still three however
        // many clients exist — which is the property AdminClientList_DerivesStatusSetWise... asserts
        // (it compares 4 clients against 30, not against a fixed number).
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
                // `frequencyType == null ? null : frequencyType.TypeName` rather than `?.` — an
                // expression tree may not contain a null-propagating operator (CS8072), and the
                // DefaultIfEmpty() above is exactly what makes the value nullable here.
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

        // Two existence checks rather than one projected row: both predicates are Expressions the
        // derivation owns, and composing them into a single Select would mean restating one of the
        // rules here — which is what BR-11's single-implementation gate forbids.
        return (
            await client.AnyAsync(statusDerivation.IsActive(today), cancellationToken),
            await client.AnyAsync(statusDerivation.HasEverBeenAssigned(), cancellationToken)
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Tracked deliberately — the service mutates what this returns and then saves. An
    /// <c>AsNoTracking</c> read here would make every update silently do nothing.
    /// <para>
    /// The categories are <c>Include</c>d because the full record carries them (AC-23's form manages
    /// them inline), and because the service reads them to build the audit change list.
    /// </para>
    /// </remarks>
    public async Task<Client?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await context
            .Set<Client>()
            .Include(client => client.BillableTimeCategories)
            .FirstOrDefaultAsync(client => client.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Compared trimmed and ordinally, matching <c>ux_client_client_name</c> exactly — see the interface
    /// remarks for why this pre-check is deliberately NOT case-insensitive.
    /// </remarks>
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
    /// <remarks>
    /// The client id is part of the WHERE, not a hint: a category id belonging to another client must
    /// resolve to nothing, so that guessing an id cannot reach someone else's configuration.
    /// </remarks>
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
    /// <remarks>
    /// Case-insensitive via <c>ToLower()</c> — Npgsql translates it to SQL <c>lower()</c> and the
    /// in-memory provider evaluates it, so the same comparison holds under both suites. Scoped to one
    /// client, which is the rule itself (FR-025).
    /// </remarks>
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
