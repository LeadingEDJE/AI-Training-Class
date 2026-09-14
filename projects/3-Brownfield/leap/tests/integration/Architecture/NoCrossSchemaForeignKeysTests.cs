using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Architecture;

/// <summary>
/// Asserts that no foreign key crosses a schema boundary, against the LIVE Postgres catalog.
/// </summary>
/// <remarks>
/// <para>
/// The invariant used to hold by accident of layout; now it holds by decision. Before R3 every
/// table was in <c>public</c> or <c>compass</c> and nothing spanned them. R3 gave Timesheet and OOTO
/// their own schemas, which turned OOTO's pre-existing employee foreign key into a cross-schema one —
/// so from here the rule is enforced against a real, enumerated exception rather than against an
/// empty set.
/// </para>
/// <para>
/// Why it must be unenforced (owner decision D-3). A cross-schema foreign key is attractive:
/// the database would guarantee that every OOTO event points at a real Compass employee. It is
/// rejected because it couples two modules at the storage layer. Constitution Principle V requires
/// a consuming module to reach the directory through <c>IDirectory</c> and not through its tables,
/// and ADR-004's stated goal is that extracting Compass later is a transport swap rather than a
/// rewrite (NFR E8). A foreign key spends that option — you cannot move a table to another process
/// while another schema's constraint depends on it.
/// </para>
/// <para>
/// What replaces it. Nothing, at the database layer. Referential integrity for cross-module
/// references becomes the application's responsibility, which is a real cost of D-3 and is recorded
/// as such: FR-015 requires an unresolvable reference to be detectable rather than silent.
/// </para>
/// <para>
/// Run this at every release of feature 003, not only at the end. The failure it guards
/// against is a foreign key introduced casually during the schema move — EF will happily generate
/// one if a navigation property survives a re-point, and it is far cheaper to catch in the release
/// that adds it than after the legacy tables are dropped.
/// </para>
/// </remarks>
public class NoCrossSchemaForeignKeysTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    /// <summary>
    /// The cross-schema foreign keys that exist today and are accepted, each enumerated exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One entry, added by R3. Moving OOTO's tables into the <c>ooto</c> schema turned its
    /// pre-existing employee foreign key into a cross-schema one:
    /// <c>ooto.out_of_office_events.employee_id</c> to <c>public.employees</c>, with
    /// <c>ON DELETE CASCADE</c>. Nothing was added — the constraint is older than this feature.
    /// </para>
    /// <para>
    /// That constraint is kept deliberately (D-7). OOTO still owns the legacy reference, and the
    /// foreign key is the only thing keeping its events referentially valid. Do NOT drop it to make
    /// this gate pass, and do NOT rewrite OOTO to avoid it. It leaves when the legacy reference
    /// does. What still blocks OOTO's move is counted in <c>severance-record.md</c> § 1b and
    /// nowhere else.
    /// </para>
    /// <para>
    /// OOTO now stores a SECOND employee reference, into <c>compass.employee</c>, and this list
    /// deliberately does NOT grow for it. That reference carries no constraint at all: D-3 and
    /// Principle V keep cross-module referential integrity in the application, because a foreign key
    /// spends the option of moving the directory to another process, and the entry above is a
    /// grandfathered accident rather than a precedent for adding more. What replaces it is
    /// detectability, in BOTH directions: the report enumerates references that resolve to nobody
    /// AND references that disagree with what the directories say now — the second being the
    /// failure this constraint would have prevented, and the one that reads as healthy if you only
    /// count nulls. The scope text this list was asked to grow for could not have been satisfied
    /// as written anyway —
    /// <see cref="EveryAcceptedCrossSchemaForeignKey_StillExists"/> fails on a listed exception with
    /// no matching constraint in the catalog, so adding the string without the constraint turns this
    /// gate red. <see cref="TheDetector_RejectsAConstraintOnTheOotoCompassEmployeeReference"/> is the
    /// fence on the other direction.
    /// </para>
    /// <para>
    /// Enumerated, never pattern-matched. A regex over <c>ooto.*</c> would also admit a NEW
    /// cross-schema key added there later, which is the whole thing this gate exists to catch.
    /// </para>
    /// </remarks>
    private static readonly string[] AcceptedCrossSchemaForeignKeys =
    [
        // Empty since #428 dropped ooto.out_of_office_events.employee_id -> public.employees, the last
        // accepted cross-schema foreign key: OOTO now owns events by the Compass int, unenforced (D-3).
        // TheDetector_FindsACrossSchemaForeignKey_WhenOneExists still proves the check can fail.
    ];

    [Fact]
    public async Task NoUnexpectedForeignKey_CrossesASchemaBoundary()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var crossSchemaForeignKeys = await CatalogAssertions.GetCrossSchemaForeignKeysAsync(
            context,
            TestContext.Current.CancellationToken);

        var unexpected = crossSchemaForeignKeys
            .Where(fk => !AcceptedCrossSchemaForeignKeys.Contains(fk, StringComparer.Ordinal))
            .ToList();

        // Assert — the message carries the offending keys, so a failure names what to remove
        // rather than only reporting a count.
        unexpected.ShouldBeEmpty(
            "Cross-module references must be unenforced and resolved through IDirectory (D-3, FR-013). "
                + "Offending foreign keys: "
                + string.Join("; ", unexpected));
    }

    [Fact]
    public async Task EveryAcceptedCrossSchemaForeignKey_StillExists()
    {
        // Arrange — the mirror of the stale-allowlist rule. An accepted exception that has
        // disappeared means either the migration landed (delete the entry) or something dropped a
        // constraint that OOTO's referential integrity depends on. Either way it must be noticed,
        // not silently tolerated.
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        // Act
        var actual = await CatalogAssertions.GetCrossSchemaForeignKeysAsync(
            context,
            TestContext.Current.CancellationToken);

        var missing = AcceptedCrossSchemaForeignKeys
            .Where(fk => !actual.Contains(fk, StringComparer.Ordinal))
            .ToList();

        // Assert
        missing.ShouldBeEmpty(
            "an accepted cross-schema foreign key no longer exists. If OOTO migrated to Compass, "
                + "delete the entry. If not, a constraint OOTO's integrity depends on was dropped. "
                + "Missing: " + string.Join("; ", missing));
    }

    /// <summary>
    /// Proves the detector can actually fail, by introducing a real cross-schema foreign key and
    /// asserting it is found.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, the assertion above is worthless. It passes today because no violation
    /// exists — which is indistinguishable from passing because the query cannot find one. That is
    /// not hypothetical here: the first version of this query joined
    /// <c>constraint_column_usage</c> on <c>table_schema</c> rather than <c>constraint_schema</c>,
    /// which is an implicit same-schema filter, and it returned zero rows against a deliberately
    /// planted violation. It was caught by running exactly this experiment by hand. Encoding it as
    /// a test means the next person to touch the SQL cannot reintroduce the fault silently.
    /// </para>
    /// <para>
    /// The probe schema is created and dropped inside the test. <c>ResetDatabaseAsync</c> truncates
    /// EF-mapped tables only, so it would not clean this up — hence the <c>finally</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheDetector_FindsACrossSchemaForeignKey_WhenOneExists()
    {
        // Arrange
        await ResetDatabaseAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE SCHEMA _crossschema_probe;
                CREATE TABLE _crossschema_probe.probe (
                    id integer PRIMARY KEY,
                    audit_log_id integer REFERENCES public.audit_logs(id)
                );
                """,
                TestContext.Current.CancellationToken);

            // Act
            var detected = await CatalogAssertions.GetCrossSchemaForeignKeysAsync(
                context,
                TestContext.Current.CancellationToken);

            // Assert
            detected.ShouldContain("_crossschema_probe.probe.audit_log_id -> public.audit_logs");
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                "DROP SCHEMA IF EXISTS _crossschema_probe CASCADE;",
                CancellationToken.None);
        }
    }
}
