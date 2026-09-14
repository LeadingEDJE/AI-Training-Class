using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;

/// <summary>
/// Counts and empties the five Compass operational tables.
/// </summary>
/// <remarks>
/// Reached through <c>Set&lt;T&gt;()</c>, not a <c>DbSet</c> property (Option 2).
/// No <c>SaveChangesAsync</c>, and none is needed:
/// <c>TRUNCATE</c> is DDL-adjacent and bypasses the change tracker, so this repository never has
/// anything to save.
///
/// It does own a transaction, which is a different question from owning a save. The rule keeping
/// <c>SaveChangesAsync</c> in the service layer is about who decides a unit of work has finished;
/// this transaction exists to make a lock span two statements — a property of the SQL itself,
/// invisible and unenforceable from the service — so it belongs with the SQL.
/// </remarks>
/// <param name="context">The single application context.</param>
public sealed class CompassDataResetRepository(LeapDbContext context) : ICompassDataResetRepository
{
    /// <summary>
    /// The tables to empty, CHILD FIRST.
    /// </summary>
    /// <remarks>
    /// The order does not matter to <c>TRUNCATE</c> — one statement empties all five simultaneously —
    /// but it is written child-first anyway so the dependency direction is legible to a reader
    /// checking that the list is complete. <c>internal</c> so the unit suite can pin the exact set.
    /// </remarks>
    internal static readonly Type[] TablesToClear =
    [
        typeof(BillableTimeCategory),
        typeof(Sow),
        typeof(ClientAssignment),
        typeof(Employee),
        typeof(Client),
    ];

    /// <summary>The only schema this repository will ever issue a TRUNCATE against.</summary>
    internal const string CompassSchema = "compass";

    /// <summary>
    /// Counts the rows in each of the five tables.
    /// </summary>
    /// <remarks>
    /// <c>internal</c>, not part of the interface, and never called on its own. It is only
    /// meaningful while <see cref="ClearAsync"/>'s lock is held — see the remarks there. Exposing it
    /// would invite exactly the two-round-trip sequence that lock exists to prevent. It stays
    /// reachable so the unit suite can assert the counting itself, which the InMemory provider runs
    /// perfectly well.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-table counts.</returns>
    internal async Task<CompassTableRowCounts> CountAsync(CancellationToken cancellationToken) =>
        new(
            BillableTimeCategories: await context.Set<BillableTimeCategory>().CountAsync(cancellationToken),
            Sows: await context.Set<Sow>().CountAsync(cancellationToken),
            ClientAssignments: await context.Set<ClientAssignment>().CountAsync(cancellationToken),
            Employees: await context.Set<Employee>().CountAsync(cancellationToken),
            Clients: await context.Set<Client>().CountAsync(cancellationToken));

    /// <inheritdoc />
    /// <remarks>
    /// The lock is what makes the returned counts true, and it has to come first: counting and
    /// truncating are two statements, so under <c>READ COMMITTED</c> a row inserted between them is
    /// destroyed by the truncate and absent from the count, and the audit entry — the only surviving
    /// record of what was destroyed — then understates it. The unit that must be atomic is
    /// count-then-truncate, not the truncate alone, and a transaction by itself does not close the
    /// window: only holding <c>ACCESS EXCLUSIVE</c>, the mode <c>TRUNCATE</c> takes anyway, across
    /// both statements does. All five tables are locked in one statement, in a fixed order, so two
    /// concurrent clears cannot deadlock.
    /// </remarks>
    public async Task<CompassTableRowCounts> ClearAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.Database.ExecuteSqlRawAsync(BuildLockStatement(context.Model), cancellationToken);
        var counts = await CountAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildTruncateStatement(context.Model), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return counts;
    }

    /// <summary>
    /// Renders the <c>LOCK TABLE</c> that must be held before the counts are taken.
    /// </summary>
    /// <param name="model">The EF model to resolve table names and schemas from.</param>
    /// <returns>The complete statement, every identifier schema-qualified and quoted.</returns>
    /// <remarks>
    /// <c>ACCESS EXCLUSIVE</c> deliberately — the same mode <c>TRUNCATE</c> takes. A weaker mode would
    /// still admit the concurrent <c>INSERT</c> this exists to exclude.
    /// </remarks>
    internal static string BuildLockStatement(IModel model) =>
        $"LOCK TABLE {QualifiedIdentifiers(model)} IN ACCESS EXCLUSIVE MODE";

    /// <summary>
    /// Renders the <c>TRUNCATE</c> for the five Compass tables from EF model metadata.
    /// </summary>
    /// <param name="model">The EF model to resolve table names and schemas from.</param>
    /// <returns>The complete statement, every identifier schema-qualified and quoted.</returns>
    /// <remarks>
    /// One statement, and no <c>CASCADE</c> — both are safety properties, not shorthand. Postgres
    /// refuses to truncate a table another references unless every referencing table is named in the
    /// same command, and naming all five satisfies every relationship among them, including
    /// <c>employee</c>'s coach self-reference. A later Compass table with a foreign key into one of
    /// these therefore fails loudly instead of being quietly emptied, and <c>CASCADE</c> would also
    /// take both lookup tables, which are FK parents here. <c>RESTART IDENTITY</c>, because half a
    /// reset is worse than none. Identifiers come from EF metadata, never typed out — a hand-written
    /// name is a guess about a derived value. Split out so it can be asserted without a database.
    /// </remarks>
    internal static string BuildTruncateStatement(IModel model) =>
        $"TRUNCATE TABLE {QualifiedIdentifiers(model)} RESTART IDENTITY";

    /// <summary>
    /// The five tables as one comma-separated list of schema-qualified, quoted identifiers.
    /// </summary>
    /// <remarks>
    /// Shared by the lock and the truncate so the two statements cannot come to name different sets
    /// of tables — which would silently reopen the window the lock is there to close.
    /// </remarks>
    private static string QualifiedIdentifiers(IModel model) =>
        string.Join(", ", TablesToClear.Select(clrType => QualifiedTableName(model, clrType)));

    /// <summary>Resolves an entity's schema-qualified, quoted table identifier from EF metadata.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the entity is not mapped to a table, or is mapped somewhere other than
    /// <see cref="CompassSchema"/>. The second check is the important one: this builds a
    /// <c>TRUNCATE</c>, so a model change that quietly relocated a Compass entity to <c>public</c>
    /// would otherwise point a destructive statement at a table this has no business touching.
    /// </exception>
    private static string QualifiedTableName(IModel model, Type clrType)
    {
        var entityType = model.FindEntityType(clrType)
            ?? throw new InvalidOperationException(
                $"{clrType.Name} is not part of the model; Compass data cannot be cleared safely.");

        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"{clrType.Name} is not mapped to a table; Compass data cannot be cleared safely.");

        var schema = entityType.GetSchema();
        if (!string.Equals(schema, CompassSchema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{clrType.Name} is mapped to schema '{schema ?? "public"}', not '{CompassSchema}'. "
                    + "Refusing to clear Compass data against an unexpected schema.");
        }

        return $"\"{schema}\".\"{tableName}\"";
    }
}
