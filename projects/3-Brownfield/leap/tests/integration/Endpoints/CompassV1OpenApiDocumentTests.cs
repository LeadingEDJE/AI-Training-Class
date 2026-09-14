using System.Net;
using System.Text.Json;

using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// The <c>compass-v1</c> OpenAPI document is the published <c>/api/compass/v1</c> boundary, isolated
/// from the whole-API document so the OpenAPI diff gate (#141) has a stable, minimal surface to diff.
/// It MUST contain every path under <c>/api/compass/v1/</c> and NOTHING else — no timesheet routes,
/// and not even the unversioned Compass read surfaces (<c>/api/compass/team-directory</c>), which are
/// not part of the versioned contract.
/// </summary>
public class CompassV1OpenApiDocumentTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CompassV1Document_IsServed()
    {
        // Act
        var response = await Client.GetAsync(
            "/openapi/compass-v1.json", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CompassV1Document_ContainsOnlyVersionedCompassPaths()
    {
        // Act
        var json = await Client.GetStringAsync(
            "/openapi/compass-v1.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var paths = doc.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToList();

        // Assert — the versioned Compass boundary is present...
        paths.ShouldNotBeEmpty();
        paths.ShouldContain(p => p.StartsWith("/api/compass/v1/", StringComparison.Ordinal));

        // ...and every path in the document is under it — no timesheet routes, no unversioned
        // Compass read surfaces, nothing else that would make the diff surface unstable.
        paths.ShouldAllBe(p => p.StartsWith("/api/compass/v1/", StringComparison.Ordinal));
    }

    /// <summary>
    /// FR-029/SC-010 (spec 009 T081): the document must be sufficient to construct a correct call
    /// WITHOUT reading Compass's source. A null <c>summary</c> fails that on sight, so every one of
    /// the eight published boundary GET operations must carry one.
    /// </summary>
    [Fact]
    public async Task CompassV1Document_EveryPublishedBoundaryOperation_HasANonEmptySummary()
    {
        // Arrange — the eight read-only Directory boundary routes this feature publishes (excludes
        // the /admin/* configuration surface, which is a different, out-of-scope surface).
        string[] boundaryPaths =
        [
            "/api/compass/v1/employees/{id}",
            "/api/compass/v1/employees/{id}/assignments",
            "/api/compass/v1/invoice-frequencies",
            "/api/compass/v1/clients/{id}",
            "/api/compass/v1/clients/{id}/billable-categories",
            "/api/compass/v1/clients/{id}/assignments",
            "/api/compass/v1/assignments/{id}",
            "/api/compass/v1/assignments/{id}/sows",
        ];
        boundaryPaths.Length.ShouldBe(8, "non-vacuity guard — a shrunk list would pass trivially");

        // Act
        var json = await Client.GetStringAsync(
            "/openapi/compass-v1.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var paths = doc.RootElement.GetProperty("paths");

        // Assert
        foreach (var path in boundaryPaths)
        {
            var get = paths.GetProperty(path).GetProperty("get");
            get.TryGetProperty("summary", out var summary).ShouldBeTrue($"{path} has no summary property");
            summary.GetString().ShouldNotBeNullOrWhiteSpace($"{path}'s summary is empty");
        }
    }

    /// <summary>
    /// <see cref="LeadingEDJE.Leap.Api.Platform.Services.OpenApi.PrivateHandlerXmlDocOperationTransformer"/>
    /// must never fill <c>summary</c> OR <c>description</c> on the <c>/api/compass/v1/admin/*</c>
    /// configuration surface (features 004/006/010) — that surface's internal <c>&lt;summary&gt;</c>/
    /// <c>&lt;remarks&gt;</c> comments are dense maintainer-facing engineering rationale (issue/AC/FR
    /// citations), never written for an external consumer of this CI-gated, published contract.
    /// </summary>
    /// <remarks>
    /// A regression test for a finding from spec 009 Phase 5's adversarial review: before the fix, these
    /// three PREVIOUSLY-BLANK operations picked up their handlers' internal doc comments verbatim as a
    /// side effect of the transformer applying document-wide with no path exclusion — two leaked a
    /// <c>summary</c> naming an internal acceptance-criterion id ("AC-25", "AC-26"), and the third leaked
    /// a <c>description</c> reading "SC-002 requires every migrated relationship checked at 100%… the
    /// reconciliation report could only ever say 'unverified'".
    /// </remarks>
    [Fact]
    public async Task CompassV1Document_AdminSurfaceOperations_NeverGainSummaryOrDescriptionFromThePrivateHandlerTransformer()
    {
        // Arrange — the three admin routes the review found had picked up a summary or description as a
        // side effect. Non-vacuity: all three must actually be present in the document, or this proves
        // nothing about the fix.
        (string Path, string Method)[] previouslyLeakedOperations =
        [
            ("/api/compass/v1/admin/sows", "get"),
            ("/api/compass/v1/admin/employee-types", "get"),
            ("/api/compass/v1/admin/invoice-frequency-types", "get"),
        ];
        previouslyLeakedOperations.Length.ShouldBe(3, "non-vacuity guard — a shrunk list would pass trivially");

        // Act
        var json = await Client.GetStringAsync(
            "/openapi/compass-v1.json", TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var paths = doc.RootElement.GetProperty("paths");

        // Assert
        foreach (var (path, method) in previouslyLeakedOperations)
        {
            paths.TryGetProperty(path, out var pathItem).ShouldBeTrue(
                $"{path} not found in the compass-v1 document — the fixture this test depends on moved");
            var operation = pathItem.GetProperty(method);

            operation.TryGetProperty("summary", out var summary).ShouldBeFalse(
                $"{method.ToUpperInvariant()} {path} carries a summary — the admin surface must never be "
                + "filled by PrivateHandlerXmlDocOperationTransformer. Value found: "
                + (summary.ValueKind == JsonValueKind.Undefined ? "(n/a)" : summary.GetString()));
            operation.TryGetProperty("description", out var description).ShouldBeFalse(
                $"{method.ToUpperInvariant()} {path} carries a description — the admin surface must never "
                + "be filled by PrivateHandlerXmlDocOperationTransformer. Value found: "
                + (description.ValueKind == JsonValueKind.Undefined ? "(n/a)" : description.GetString()));
        }
    }
}
