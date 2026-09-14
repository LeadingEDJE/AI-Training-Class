using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Support;

/// <summary>
/// Shared queries against the LIVE Postgres catalog, for tests that assert schema shape rather than
/// application behaviour.
/// </summary>
/// <remarks>
/// <para>
/// Why raw catalog SQL rather than EF metadata. Feature 003 moves tables between schemas and
/// drops six of them. Asserting any of that through <c>DbContext.Model</c> would prove only that EF
/// believes its own configuration — the same mistake
/// <c>docs/platform/adding-a-module.md</c> § 3 step 4 warns about when it says reading generated C#
/// is not verification. <c>CompassSchemaFromErdTests</c> established the pattern; this type is the
/// reusable form of it.
/// </para>
/// <para>
/// Deliberately thin. One method, because one caller needs one query today. The plan
/// (task T006) sketched table-to-schema and column-type helpers alongside this, and they are NOT
/// here: they have no consumer until the schema move lands, and the project's development
/// philosophy is explicit that abstractions are extracted when a pattern appears, not before. Add
/// them in the release that first needs them.
/// </para>
/// </remarks>
public static class CatalogAssertions
{
    /// <summary>
    /// Returns every foreign key whose referencing table and referenced table live in DIFFERENT
    /// schemas, formatted as <c>source_schema.source_table.column -&gt; target_schema.target_table</c>.
    /// An empty result is the expected state for this platform.
    /// </summary>
    /// <remarks>
    /// Owner decision D-3: cross-module references are unenforced and resolved through the Compass
    /// directory contract, never by a database constraint. A cross-schema foreign key is the
    /// concrete shape that decision forbids — it re-couples two modules at the storage layer and
    /// turns a future Compass extraction from a transport swap into a rewrite (ADR-004, NFR E8).
    /// System schemas are excluded so the query measures application tables only.
    /// </para>
    /// <para>
    /// Join on <c>constraint_schema</c>, NOT on <c>table_schema</c>. This is the whole
    /// correctness of the query and the first version got it wrong. In
    /// <c>information_schema.constraint_column_usage</c> the <c>table_schema</c> column is the
    /// schema of the REFERENCED table, while <c>constraint_schema</c> is the schema that owns the
    /// constraint (the referencing side). Joining <c>tc.table_schema = ccu.table_schema</c> is
    /// therefore an implicit "both sides live in the same schema" filter — it silently discards
    /// precisely the cross-schema rows this method exists to find, and the query returns empty no
    /// matter how many violations exist.
    /// </para>
    /// <para>
    /// That version was caught by introducing a real cross-schema foreign key against a live
    /// database and observing that the query returned zero rows. It would otherwise have shipped as
    /// a gate that could never fail. Verify any change to this SQL the same way — by construction,
    /// against a live catalog, never by reading it.
    /// </para>
    /// </remarks>
    public static async Task<List<string>> GetCrossSchemaForeignKeysAsync(
        LeapDbContext context,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                tc.table_schema || '.' || tc.table_name || '.' || kcu.column_name
                || ' -> ' || ccu.table_schema || '.' || ccu.table_name AS "Value"
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.constraint_schema = kcu.constraint_schema
            JOIN information_schema.constraint_column_usage ccu
                ON tc.constraint_name = ccu.constraint_name
                AND tc.constraint_schema = ccu.constraint_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
                AND tc.table_schema NOT IN ('pg_catalog', 'information_schema')
                AND ccu.table_schema NOT IN ('pg_catalog', 'information_schema')
                AND tc.table_schema <> ccu.table_schema
            ORDER BY 1;
            """;

        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Returns every application base table as <c>schema.table</c>, excluding system schemas.
    /// </summary>
    /// <remarks>
    /// Added in R3, when the schema move gave it its first consumer — the plan sketched it alongside
    /// the foreign-key query in T006 and it was deliberately left out until then.
    /// </remarks>
    public static async Task<List<string>> GetQualifiedTablesAsync(
        LeapDbContext context,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT table_schema || '.' || table_name AS "Value"
            FROM information_schema.tables
            WHERE table_type = 'BASE TABLE'
                AND table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY 1;
            """;

        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Returns every updatable view in <c>public</c> as <c>view_name</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The compatibility views OD-1 requires must be **automatically updatable** — a single-table
    /// <c>SELECT *</c> view, which PostgreSQL lets you INSERT/UPDATE/DELETE through with no rule or
    /// trigger. That property is the whole reason the previously-running application keeps working
    /// across the schema move, so it is asserted from the catalog
    /// (<c>information_schema.views.is_updatable</c>) rather than assumed from the view's shape.
    /// </para>
    /// </remarks>
    public static async Task<List<string>> GetUpdatablePublicViewsAsync(
        LeapDbContext context,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT table_name AS "Value"
            FROM information_schema.views
            WHERE table_schema = 'public'
                AND is_updatable = 'YES'
            ORDER BY 1;
            """;

        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the column names a relation in <c>public</c> exposes, view or table alike.
    /// </summary>
    /// <remarks>
    /// A <c>SELECT *</c> view fixes its column list when it is created, so a view standing over a
    /// table that later gains a column keeps the shape it had — reads through it silently omit the
    /// new column. Nothing about the view's definition reveals that, which is why the answer has to
    /// come from the catalog.
    /// </remarks>
    public static async Task<List<string>> GetPublicRelationColumnsAsync(
        LeapDbContext context,
        string relation,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public'
                AND table_name = {0}
            ORDER BY 1;
            """;

        return await context.Database
            .SqlQueryRaw<string>(sql, relation)
            .ToListAsync(cancellationToken);
    }
}
