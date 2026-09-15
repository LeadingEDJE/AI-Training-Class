using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Data;

/// <summary>
/// Pins the premise that made Phase 49's schema-qualification of the integration-test reset a
/// provable no-op: EVERY mapped entity type in today's model lives in the <c>public</c> schema.
/// </summary>
/// <remarks>
/// <para>
/// <c>IntegrationTestBase.ResetDatabaseAsync</c> used to emit bare, unqualified table names in its
/// <c>TRUNCATE</c> and lean on <c>search_path</c> to resolve them to <c>public</c>. Phase 49 changed
/// it to emit <c>"schema"."table"</c> so that the first Compass entity mapped to the new
/// <c>compass</c> schema does not break every integration test at once. This test is the evidence
/// that the change altered no behaviour at the time it was made: if every entity type resolves to
/// <c>public</c>, then qualifying with the schema produces the same set of tables the unqualified
/// form resolved to.
/// </para>
/// <para>
/// That day has arrived (issue #50). The predecessor of this test asserted that EVERY mapped
/// entity resolved to <c>public</c>, and said so: "When the Compass team adds their first entity
/// mapped to <c>compass</c>, this test SHOULD start failing. That is the signal to update it to
/// assert the new expected split (public + compass), not a reason to revert the qualification." The
/// Compass ERD schema mapped seven tables into <c>compass</c>, so the assertion below is now the
/// split rather than the no-op — and the qualification it justified is what stops
/// <c>ResetDatabaseAsync</c> silently skipping those seven tables.
/// </para>
/// <para>
/// Timesheet and Ooto are gone (module removal, 2026-09): both schemas' tables were dropped with the
/// modules that owned them, so the allowed-schema check below is public + compass only, and the
/// counts pin both at zero rather than dropping the assertion — a regression that brings either
/// schema back should fail here, not silently pass an unrelated reset path.
/// </para>
/// <para>
/// The model is built against the real Npgsql provider (no connection is opened — EF builds the model
/// from configuration alone) because the InMemory provider ignores schemas entirely and would make
/// this assertion vacuous.
/// </para>
/// </remarks>
public class TruncateIdentifierSchemaQualificationTests
{
    private const string ModelOnlyConnectionString =
        "Host=localhost;Database=model-only;Username=none;Password=none";

    [Fact]
    public void EveryMappedEntityType_ResolvesToPublicOrCompass_AndCompassIsQualified()
    {
        // Arrange — build the real relational model without connecting to a database.
        var options = new DbContextOptionsBuilder<LeapDbContext>()
            .UseLeapPostgres(ModelOnlyConnectionString)
            .Options;
        using var context = new LeapDbContext(options);

        // Act — mirror the projection ResetDatabaseAsync performs.
        var identifiers = context.Model.GetEntityTypes()
            .Where(t => t.GetTableName() != null)
            .Select(t => string.Concat(
                "\"", t.GetSchema() ?? "public", "\".\"", t.GetTableName(), "\""))
            .ToList();

        // Assert — a non-empty list (a vacuous pass over zero entity types would prove nothing),
        // split across exactly the two schemas this database uses.
        identifiers.ShouldNotBeEmpty();
        identifiers.ShouldAllBe(
            identifier =>
                identifier.StartsWith("\"public\".\"", StringComparison.Ordinal)
                || identifier.StartsWith("\"compass\".\"", StringComparison.Ordinal),
            "Every mapped table is in public or compass. A third schema means someone added one "
                + "without updating the reset path or this test.");

        // Every non-public table MUST appear schema-qualified. Unqualified, TRUNCATE would resolve
        // through search_path, silently not truncate them, and leave rows behind — surfacing as
        // failures in unrelated tests rather than as anything pointing at a schema problem.
        //
        // This test is doing exactly what its own header said it would: it was written to FAIL when a
        // new schema appeared, with the instruction to update the expected split rather than revert
        // the qualification. Feature 003 added two.
        Count(identifiers, "compass").ShouldBe(
            9, "the Compass ERD maps seven tables into compass, plus the technical-skills feature's "
                + "two (skill, employee_skill)");
        Count(identifiers, "timesheet").ShouldBe(
            0, "the Timesheet module and its schema were removed; a non-zero count means either it "
                + "came back or this reset path needs to schema-qualify a third schema again");
        Count(identifiers, "ooto").ShouldBe(
            0, "the OOTO module and its schema were removed; a non-zero count means either it came "
                + "back or this reset path needs to schema-qualify a third schema again");
    }

    private static int Count(IEnumerable<string> identifiers, string schema) =>
        identifiers.Count(i => i.StartsWith($"\"{schema}\".\"", StringComparison.Ordinal));
}
