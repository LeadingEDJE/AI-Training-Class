using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.AspNetCore.Hosting;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// A host that accepts a migration bearer token, so both sides of the identity gate are reachable.
/// </summary>
/// <remarks>
/// Its own factory rather than the shared one, matching the integration suite's
/// <c>MigrationTokenFactory</c> and for the same reason: configuring a token changes the
/// application's authentication wiring for every request the host serves — it registers the bearer
/// scheme and a forwarding policy scheme and makes the latter the default. Imposing that on every
/// other unit endpoint test to serve this file would be the wrong trade.
/// </remarks>
public sealed class MigrationTokenUnitFactory : TestWebApplicationFactory
{
    /// <summary>The token this host accepts. Not a secret; it exists only in this process.</summary>
    public const string MigrationToken = "unit-migration-token-not-a-real-secret";

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Auth:MigrationPrincipal:Token", MigrationToken);
    }
}

/// <summary>
/// The provenance read route — who may reach it, and what it returns.
/// </summary>
/// <remarks>
/// <para>
/// The route group is not the gate here, and that is the whole point of this file.
/// <c>/api/compass/v1/admin/migration/provenance</c> sits on <c>CompassAdminRouteGroup</c>, which
/// requires the Compass root — a role the migration principal shares with every Compass Super
/// Admin. So the group admits exactly the callers this route must exclude, and the handler's own
/// IDENTITY check is the only thing standing between a Super Admin and the full map from TPS
/// identifiers to Compass records. A test proving the group refuses a lesser role would say nothing
/// about that.
/// </para>
/// <para>
/// Covered at the unit level as well as through Testcontainers because the backend coverage gate
/// measures <c>tests/unit</c> alone; before this file the handler's refusal branch had no unit
/// coverage at all.
/// </para>
/// </remarks>
public class CompassMigrationProvenanceEndpointsTests
{
    private const string Route = "/api/compass/v1/admin/migration/provenance";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static HttpClient AsMigrationPrincipal(MigrationTokenUnitFactory factory)
    {
        // Deliberately NOT a role-header client: the bearer token IS the credential, and a migration
        // run in a deployed environment has no session to attach a role header to.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            MigrationTokenUnitFactory.MigrationToken
        );
        return client;
    }

    [Fact]
    public async Task Get_AsTheMigrationPrincipal_ReturnsTheProvenanceMap()
    {
        // Arrange
        await using var factory = new MigrationTokenUnitFactory();
        var client = AsMigrationPrincipal(factory);

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert — the shape matters as much as the status: the tool reads Entries directly, so a
        // 200 carrying a null body would fail at the caller rather than here.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CompassMigrationProvenanceResponse>(
            Token
        );
        body.ShouldNotBeNull();
        body.Entries.ShouldNotBeNull();
    }

    [Fact]
    public async Task Get_AsACompassSuperAdmin_IsRefused()
    {
        // Arrange — ⚠️ the assertion this file exists for. A Super Admin holds the SAME Compass root
        // role the migration principal does, so a gate keyed on the role admits them and provenance
        // — deliberately absent from every other Compass surface — leaks to anyone at a browser.
        await using var factory = new MigrationTokenUnitFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert — the exact status, never "not 200": under a misconfigured host a loose assertion
        // passes on a 401 and the gate silently stops being tested.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_AsACompassAdmin_IsRefused()
    {
        // Arrange — the read-only Compass role (AC-44). Refused by the route group before the
        // handler's identity check is even reached; asserted so a future regrouping of this route
        // onto a laxer policy fails here.
        await using var factory = new MigrationTokenUnitFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.GetAsync(Route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
