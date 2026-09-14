using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Compass;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// <c>GET /api/compass/v1/admin/migration/provenance</c> — the legacy-to-Compass identifier map.
/// </summary>
/// <remarks>
/// <para>
/// This route is what lets the migration tool keep no database of its own. The tool used to
/// carry a SQLite crosswalk that was simultaneously the idempotency check, the foreign-key resolver
/// and the resume point, and that had to be backed up as a production-day gate because losing it lost
/// all three. Compass already stores the mapping in <c>legacy_tps_id</c>; reading it back made the
/// second copy redundant.
/// </para>
/// <para>
/// So the consequence of this route breaking is not a missing report — it is a migration that
/// silently re-creates everything it has already created. That is why it is worth its own file.
/// </para>
/// </remarks>
public class CompassMigrationProvenanceEndpointsTests(MigrationTokenFactory factory)
    : IClassFixture<MigrationTokenFactory>
{
    private const string Route = "/api/compass/v1/admin/migration/provenance";
    private const string ClientsRoute = "/api/compass/v1/admin/clients";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HttpClient AsMigrationPrincipal()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            MigrationTokenFactory.Token
        );
        return client;
    }

    [Fact]
    public async Task MigrationPrincipal_ReadsBackWhatItCreated()
    {
        // Arrange — ⚠️ this route is what lets the migration tool keep NO database of its own. It
        // used to carry a SQLite crosswalk that was the idempotency check, the foreign-key resolver
        // and the resume point, and that had to be backed up as a production-day gate. Compass
        // already knows all of it; if this read breaks, the tool silently re-creates everything it
        // has already created.
        var client = AsMigrationPrincipal();
        var legacyId = $"tps:Client:{Guid.NewGuid():N}";
        var name = $"Readback Co {Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync(
            ClientsRoute,
            new { clientName = name, isInternal = false, legacyTpsId = legacyId },
            Token
        );

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act
        var response = await client.GetAsync("/api/compass/v1/admin/migration/provenance", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ProvenanceResponse>(Token);

        body.ShouldNotBeNull();
        body.Entries.ShouldContain(entry => entry.LegacyTpsId == legacyId);
    }

    [Fact]
    public async Task ASuperAdmin_CannotReadProvenance()
    {
        // Arrange — the same identity-not-role gate as writing it. Provenance is deliberately absent
        // from every application-facing Compass surface, and this route is the one exception; a role
        // check would hand it to everyone holding the Compass root, which the migration principal
        // also holds.
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync("/api/compass/v1/admin/migration/provenance", Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private sealed record ProvenanceEntry(string LegacyTpsId, int CompassId);

    private sealed record ProvenanceResponse(IReadOnlyList<ProvenanceEntry> Entries);
}
