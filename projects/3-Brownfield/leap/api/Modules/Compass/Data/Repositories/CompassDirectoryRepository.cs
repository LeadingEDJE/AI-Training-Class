using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>
/// EF Core read-only repository backing the Compass directory boundary.
/// </summary>
/// <remarks>
/// Reads the Compass store: <see cref="Employee"/> is Compass's own entity on
/// <c>compass.employee</c>, the frozen legacy directory tables are never a fallback, and rows come
/// from <c>Set&lt;T&gt;()</c> (Option 2).
///
/// <c>EmployeeType</c> and <c>Coach</c> are included because the service projects them into the
/// published DTO and nothing lazy-loads here; <c>Coach</c> is one level only (FR-008: "id and
/// display name only, not a nested profile"). A client's status is never stored — it is derived at
/// read time from the shared <see cref="IClientStatusDerivation"/> that
/// <c>CompassReadRepository</c> also uses, so the two surfaces cannot disagree.
/// </remarks>
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
        // Distinct because the match key is lower(btrim(...)): two spellings of one address are one
        // key, and sending both would ask the database to consider a value twice.
        var keys = emails
            .Select(MatchKey)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Not merely an optimisation. `= ANY('{}')` matches nothing, so querying would be correct and
        // still cost a round trip on every caller whose rows all lack an email — and the boundary's
        // own contract test asserts the store is not touched.
        if (keys.Count == 0)
        {
            return [];
        }

        // Filters BEFORE projecting, and returns entities. An email predicate applied inside a
        // projection compiles to a per-row delegate EF cannot translate: it evaluates happily against
        // the in-memory provider and throws against PostgreSQL, so no unit test can see the
        // difference. Same trap as CompassReadRepository.GetEmployeeDetailAsync, and the sibling
        // ordering case.
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

    /// <summary>
    /// The employee read shape every projection on this boundary needs: no tracking, plus the two
    /// navigations <c>CompassEmployeeDto</c> reads.
    /// </summary>
    /// <remarks>
    /// Extracted when the email-keyed reads arrived and made three call sites of the same graph. The
    /// duplication mattered because it is silent: omitting <c>Include(e =&gt; e.Coach)</c> does
    /// not fail — it publishes a DTO whose <c>Coach</c> is null, which reads as "this EDJEr has no
    /// coach". <c>EmployeeType</c> degrades the same way, to an empty string.
    /// </remarks>
    private IQueryable<Employee> EmployeesWithTheirProjectionGraph()
        => context
            .Set<Employee>()
            .AsNoTracking()
            .Include(e => e.EmployeeType)
            .Include(e => e.Coach);

    /// <summary>
    /// Normalises a candidate address to the boundary's match key, or <c>null</c> when there is
    /// nothing to match on.
    /// </summary>
    /// <remarks>
    /// <c>Trim().ToLower()</c> on both sides, implementing <c>ux_employee_email_ci</c>'s
    /// <c>lower(btrim(email))</c> rule: matching more strictly would make a record that exists read as
    /// missing. Not expression-identical to that index — Npgsql renders <c>.Trim()</c> as two-argument
    /// <c>btrim(email, E' \t\n\r')</c> — so the index is unused and this is a sequential scan, left so
    /// at this table's size: making it index-eligible means dropping <c>Trim()</c> from the column side
    /// and no longer matching a stored address with stray whitespace, which <c>DirectoryBoundaryTests</c>
    /// asserts. Revisit if the table ever grows by orders of magnitude. <c>ToLower()</c> rather than
    /// <c>EF.Functions.ILike</c> so one predicate serves both providers.
    /// </remarks>
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

        // The same component CompassReadRepository.GetClientViewAsync uses — a scoped AnyAsync, never
        // an EndDate comparison written here. This is what makes the two Compass surfaces agree about
        // the same client on the same day rather than each deriving it independently.
        var isActive = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(c => c.Id == clientId)
            .AnyAsync(statusDerivation.IsActive(today), cancellationToken);

        // The second derived fact, which separates a client we never engaged (Inactive)
        // from one we have stopped working with (Former). Asked the same scoped-AnyAsync way, and for
        // the same reason: both predicates are Expressions the derivation owns, so restating either
        // here would be the second implementation BR-11 forbids.
        var hasEverBeenAssigned = await context
            .Set<Client>()
            .AsNoTracking()
            .Where(c => c.Id == clientId)
            .AnyAsync(statusDerivation.HasEverBeenAssigned(), cancellationToken);

        // Include(InvoiceFrequencyType) resolves the frequency's NAME in this round trip, so
        // CompassDirectoryService never issues a second query for it.
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
    /// <remarks>
    /// Three queries however many clients exist, measured against real Postgres by
    /// <c>BoundaryClientList_IssuesThreeQueries_HoweverManyClientsExist</c>: two scoped id projections
    /// answer "which are active" and "which have ever been assigned" set-wise, which is why
    /// <see cref="IClientStatusDerivation"/> tells list consumers to take the expression forms. The tuple
    /// is built after materialisation and <c>OrderBy</c> sorts an entity column — not the Npgsql trap.
    ///
    /// The order diverges from <c>GetClientDirectoryAsync</c>'s in-memory <c>StringComparer.Ordinal</c>
    /// sort, and this method's unit coverage runs on the InMemory provider, whose .NET default comparer
    /// agrees with neither. Harmless: nothing compares the two orders.
    /// </remarks>
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
    /// <remarks>
    /// Projected-member trap discipline (spec 009 Slice 5, the highest-risk shape in the feature):
    /// this method never constructs <see cref="Contracts.CompassAssignmentDto"/> or any other named type
    /// inside the query. It returns the <see cref="ClientAssignment"/> ENTITY with both
    /// <c>Include</c> chains loaded — <c>Client</c> (and its <c>InvoiceFrequencyType</c>), and the
    /// assignment's OWN <c>InvoiceFrequencyType</c> — so <c>CompassDirectoryService</c> can apply the
    /// <c>EffectiveInvoiceFrequency</c> precedence expression in memory, after materialization. There is
    /// nothing here for Npgsql to fail to translate: no ordering, filtering or grouping ever touches a
    /// member of a type this method builds itself.
    /// </remarks>
    public async Task<ClientAssignment?> GetAssignmentAsync(
        int assignmentId, CancellationToken cancellationToken)
        => await context
            .Set<ClientAssignment>()
            .AsNoTracking()
            .Include(a => a.Client!.InvoiceFrequencyType)
            .Include(a => a.InvoiceFrequencyType)
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);

    /// <inheritdoc />
    /// <remarks>Same projected-member-trap discipline as <see cref="GetAssignmentAsync"/> — the
    /// <c>OrderBy</c> below sorts the ENTITY's own <c>StartDate</c> column, never a member of a
    /// constructed type.</remarks>
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
    /// <remarks>Same projected-member-trap discipline as <see cref="GetAssignmentAsync"/> — the
    /// <c>OrderBy</c> below sorts the ENTITY's own <c>StartDate</c> column, never a member of a
    /// constructed type.</remarks>
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
    /// <remarks>
    /// No projected-member trap risk here: no join is needed to resolve a display value, so this
    /// orders the entity's own <c>SowStartDate</c> column directly, with nothing constructed inside
    /// the query.
    /// </remarks>
    public async Task<IReadOnlyList<Sow>> GetSowsByAssignmentAsync(
        int clientAssignmentId, CancellationToken cancellationToken)
        => await context
            .Set<Sow>()
            .AsNoTracking()
            .Where(s => s.ClientAssignmentId == clientAssignmentId)
            .OrderBy(s => s.SowStartDate)
            .ToListAsync(cancellationToken);
}
