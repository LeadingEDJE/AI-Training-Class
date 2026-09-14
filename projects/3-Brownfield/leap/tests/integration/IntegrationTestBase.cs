using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadingEDJE.Leap.Api.IntegrationTests;

#pragma warning disable IDE0290 // Primary constructor not possible: factory captured + used in property initializer (CS9124)
public abstract class IntegrationTestBase : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    protected HttpClient Client { get; }

    protected IntegrationTestBase(IntegrationTestFactory factory)
    {
        _factory = factory;
        Client = factory.CreateClient();
    }
#pragma warning restore IDE0290

    /// <summary>
    /// Returns a client authenticated with only the Manager privilege (no SuperAdmin/TimesheetProcessor).
    /// Use this to test authorization boundaries that elevated access would bypass.
    /// </summary>
    protected HttpClient CreateManagerOnlyClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.PrivilegeOverrideHeader, "EDJEr,Manager");
        return client;
    }

    protected IServiceProvider Services => _factory.Services;

    protected async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Empty every mapped table in a single Postgres statement. TRUNCATE ... CASCADE
        // resolves FK ordering itself (no per-table ordering, no FK-check toggling, and it all
        // runs on one connection/session), RESTART IDENTITY resets identity sequences, and
        // TRUNCATE bypasses the append-only BEFORE UPDATE/DELETE row triggers on audit_logs
        // (Postgres fires only statement-level TRUNCATE triggers). The result is a deterministic
        // empty database between tests.
        var identifiers = GetQualifiedTableIdentifiers(dbContext);

        // Table names come from EF model metadata (dbContext.Model), never user input.
        // Double-quote each identifier for safety; the metadata-derived list needs no parameters.
        var joined = string.Join(", ", identifiers);
        var sql = string.Concat("TRUNCATE TABLE ", joined, " RESTART IDENTITY CASCADE;");
        await dbContext.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>
    /// Projects every mapped entity type to a fully schema-qualified, double-quoted table identifier
    /// of the form <c>"schema"."table"</c>.
    /// </summary>
    /// <remarks>
    /// DO NOT "simplify" this back to the bare table name. Every table is in <c>public</c> today, so
    /// an unqualified <c>TRUNCATE</c> resolves correctly via <c>search_path</c> — which is exactly why
    /// this was safe until now and exactly why the breakage would be baffling. Phase 49 created the
    /// <c>compass</c> schema as the organizational namespace for the incoming Compass module. The
    /// moment the first Compass entity is mapped to that schema, an unqualified identifier would fail
    /// to resolve, and the symptom would be ~281 unrelated integration tests failing at their
    /// <c>ResetDatabaseAsync</c> call rather than anything that points at a schema problem.
    ///
    /// Emitting the schema explicitly is a no-op today (asserted by a unit test that every entity type
    /// in the current model resolves to <c>public</c>) and correct later.
    /// </remarks>
    private static IEnumerable<string> GetQualifiedTableIdentifiers(LeapDbContext dbContext)
    {
        return dbContext.Model.GetEntityTypes()
            .Where(t => t.GetTableName() != null)
            .Select(t => string.Concat(
                "\"", t.GetSchema() ?? "public", "\".\"", t.GetTableName(), "\""));
    }
}
