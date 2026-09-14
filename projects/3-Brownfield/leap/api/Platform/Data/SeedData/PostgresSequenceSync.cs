using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LeadingEDJE.Leap.Api.Platform.Data.SeedData;

/// <summary>
/// Resyncs Postgres identity sequences to MAX(id) after explicit-ID inserts, which do not advance the
/// sequence on Postgres, so a later default insert would collide with SQLSTATE 23505.
/// </summary>
/// <remarks>
/// Its caller, <c>CompassDirectorySeeder</c>, is named as prose rather than a <c>cref</c>: that seeder
/// lives in <c>api/Modules/Compass/</c>, and <c>PlatformPurityTests</c> treats a cref as code, so
/// binding to it would earn this Platform file an allowlist entry. Prose creates no binding.
/// </remarks>
public static class PostgresSequenceSync
{
    /// <summary>Sets every int/long identity-keyed table's sequence to MAX(id) (or a fresh state for empty tables). No-op on non-relational providers (unit-test InMemory).</summary>
    public static async Task ResyncIdentitySequencesAsync(LeapDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsRelational())
        {
            return;
        }

        foreach (var sql in BuildResyncStatements(context.Model))
        {
            await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }

    /// <summary>
    /// Builds the <c>setval</c> statement for every int/long identity-keyed table in the model. Split
    /// from the async executor so the statement shaping is unit-testable without a live connection.
    /// </summary>
    internal static IReadOnlyList<string> BuildResyncStatements(IModel model) =>
        model.GetEntityTypes()
            .Select(entityType => (entityType, tableName: entityType.GetTableName(), primaryKey: entityType.FindPrimaryKey()))
            .Where(x => x.tableName != null && x.primaryKey != null && x.primaryKey.Properties.Count == 1)
            .Select(x => (tableName: x.tableName!, keyProperty: x.primaryKey!.Properties[0], schema: x.entityType.GetSchema()))
            .Where(x =>
                (x.keyProperty.ClrType == typeof(int) || x.keyProperty.ClrType == typeof(long))
                && x.keyProperty.ValueGenerated == ValueGenerated.OnAdd)
            .Select(x => (x.tableName, x.schema, columnName: x.keyProperty.GetColumnName(StoreObjectIdentifier.Table(x.tableName, x.schema))))
            .Where(x => x.columnName != null)
            // Identifiers come from EF model metadata, never user input; quote them for safety.
            // setval(..., MAX(id), true) makes the next value MAX+1; for an empty table
            // setval(..., 1, false) makes it 1. Schema-qualify whenever the entity declares a schema:
            // an unqualified `compass` table resolves to a non-existent public.employee and errors.
            // `public` stays unqualified — normalising it would rewrite 20+ statements for no gain.
            .Select(x => (Qualified: Qualify(x.tableName, x.schema), x.columnName))
            .Select(x =>
                $"SELECT setval(pg_get_serial_sequence('{x.Qualified}', '{x.columnName}'), " +
                $"GREATEST((SELECT COALESCE(MAX(\"{x.columnName}\"), 0) FROM {x.Qualified}), 1), " +
                $"(SELECT COUNT(*) > 0 FROM {x.Qualified}));")
            .ToList();

    /// <summary>
    /// Renders a quoted table identifier, schema-qualified only when <paramref name="schema"/> is set.
    /// A null schema means EF's default schema (<c>public</c> here).
    /// </summary>
    private static string Qualify(string tableName, string? schema) =>
        schema is null ? $"\"{tableName}\"" : $"\"{schema}\".\"{tableName}\"";
}
