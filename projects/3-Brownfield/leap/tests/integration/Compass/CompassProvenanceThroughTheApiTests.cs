using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Compass;

/// <summary>
/// An <see cref="IntegrationTestFactory"/> with a migration token configured.
/// </summary>
/// <remarks>
/// Its own factory, and therefore its own container, because configuring the token changes the
/// application's authentication wiring for every request the host serves — it registers the bearer
/// scheme and a forwarding policy scheme, and makes the latter the default. Sharing the suite's
/// factory would impose that on every other Compass test to serve three.
/// </remarks>
public sealed class MigrationTokenFactory : IntegrationTestFactory
{
    /// <summary>The token this host accepts. Not a secret; it exists only in this process.</summary>
    public const string Token = "integration-migration-token-not-a-real-secret";

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Auth:MigrationPrincipal:Token", Token);
    }
}

/// <summary>
/// Provenance travelling the whole way: an HTTP create through the real Compass API, landing in
/// <c>legacy_tps_id</c>.
/// </summary>
/// <remarks>
/// <para>
/// Neither half is provable in isolation, which is why this exists.
/// <c>CompassLegacyProvenanceGuardTests</c> proves the gate decides correctly given a principal, and
/// <c>CompassLegacyProvenanceTests</c> proves the column and its unique index exist. Only a request
/// that actually carries the migration token shows the DTO field, the endpoint guard, the service
/// mapping and the column agreeing — a break anywhere between them leaves both other suites green.
/// </para>
/// <para>
/// It also covers the regression this feature's own token nearly caused before. Naming
/// authentication schemes on the Compass policies made authorization re-authenticate and discard
/// <c>HttpContext.User</c>, so every Compass route answered 401 to a local developer the moment a
/// migration token was configured — a symptom that appears ONLY with a token set, which no other
/// suite has. The Super Admin case below runs against a token-configured host and expects a normal
/// 201, so a recurrence fails here.
/// </para>
/// </remarks>
public class CompassProvenanceThroughTheApiTests(MigrationTokenFactory factory)
    : IClassFixture<MigrationTokenFactory>
{
    private const string ClientsRoute = "/api/compass/v1/admin/clients";
    private const string EdjersRoute = "/api/compass/v1/admin/edjers";
    private const string AssignmentsRoute = "/api/compass/assignments";
    private const string Categories = "billable-time-categories";

    /// <summary>
    /// The title <see cref="CompassLegacyProvenance.Refusal"/> puts on its ProblemDetails.
    /// </summary>
    /// <remarks>
    /// ⚠️ Asserted on every refusal below, and not decoration. Authorization also answers
    /// <c>403</c>, so a test that checked only the status code would pass just as happily on a route
    /// where the Compass Super Admin has no authority at all — the guard could be deleted and the
    /// assertion would not notice. This title is emitted by the guard and by nothing else, so it is
    /// what distinguishes "refused for supplying provenance" from "refused for being you".
    /// </remarks>
    private const string RefusalTitle = "Legacy provenance may only be set by the migration principal";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HttpClient AsMigrationPrincipal()
    {
        // Deliberately NOT AsCompassSuperAdmin(): the bearer token is the whole credential, and a
        // migration run in a deployed environment has no session to add a role header to.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            MigrationTokenFactory.Token
        );
        return client;
    }

    private async Task<string?> ProvenanceOfAsync(string clientName)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        var rows = await context
            .Database.SqlQueryRaw<string?>(
                @"SELECT legacy_tps_id AS ""Value"" FROM compass.client WHERE client_name = {0};",
                clientName
            )
            .ToListAsync(Token);

        return rows.SingleOrDefault();
    }

    [Fact]
    public async Task MigrationPrincipal_CreatesAClientCarryingItsTpsOrigin()
    {
        // Arrange
        var client = AsMigrationPrincipal();
        var name = $"Provenance Co {Guid.NewGuid():N}";

        // Act
        var response = await client.PostAsJsonAsync(
            ClientsRoute,
            new
            {
                clientName = name,
                isInternal = false,
                legacyTpsId = $"tps-client-{Guid.NewGuid():N}",
            },
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ProvenanceOfAsync(name)).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ASuperAdmin_SupplyingProvenance_IsRefused()
    {
        // Arrange — a Compass Super Admin, holding the Compass root role. ⚠️ This is the assertion
        // that makes the guard identity-based rather than role-based: the migration principal
        // carries that SAME role, so anything keyed on the role would let this straight through.
        var client = factory.AsCompassSuperAdmin();
        var name = $"Fabricated Co {Guid.NewGuid():N}";

        // Act
        var response = await client.PostAsJsonAsync(
            ClientsRoute,
            new { clientName = name, isInternal = false, legacyTpsId = "tps-client-fabricated" },
            Token
        );

        // Assert — refused, and the record NOT created. A guard that returned 403 after the write
        // would leave a fabricated origin behind while reporting failure.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ProvenanceOfAsync(name)).ShouldBeNull();
    }

    [Fact]
    public async Task ASuperAdmin_OmittingProvenance_CreatesNormally()
    {
        // Arrange — ⚠️ the regression guard. This is an ORDINARY Compass write against a host that
        // has a migration token configured. It answered 401 before the forwarding policy scheme
        // replaced named schemes on the Compass policies, and no other suite configures a token, so
        // this is the only place that failure can surface.
        var client = factory.AsCompassSuperAdmin();
        var name = $"Ordinary Co {Guid.NewGuid():N}";

        // Act
        var response = await client.PostAsJsonAsync(
            ClientsRoute,
            new { clientName = name, isInternal = false },
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ProvenanceOfAsync(name)).ShouldBeNull();
    }

    /// <summary>Creates a client as the migration principal and returns its id and name.</summary>
    private async Task<(int Id, string Name)> SeedMigratedClientAsync()
    {
        var name = $"Seeded Co {Guid.NewGuid():N}";

        var response = await AsMigrationPrincipal()
            .PostAsJsonAsync(
                ClientsRoute,
                new
                {
                    clientName = name,
                    isInternal = false,
                    legacyTpsId = $"tps-client-{Guid.NewGuid():N}",
                },
                Token
            );

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        // ToListAsync, not SingleAsync: SingleAsync makes EF wrap this raw SQL in a subquery, and a
        // trailing semicolon inside a subquery is a 42601 syntax error. ProvenanceOfAsync above does
        // the same thing for the same reason.
        var ids = await context
            .Database.SqlQueryRaw<int>(
                @"SELECT client_id AS ""Value"" FROM compass.client WHERE client_name = {0};",
                name
            )
            .ToListAsync(Token);

        return (ids.Single(), name);
    }

    /// <summary>
    /// The provenance guard applies to UPDATE, not only to create.
    /// </summary>
    /// <remarks>
    /// It did not, and the request contract said otherwise. <c>CompassClientRequest</c> and
    /// <c>CompassEdjerRequest</c> are bound by BOTH the create and the update handler, and
    /// <see cref="CompassLegacyProvenance.MaySet"/> was called only in create — so a Compass Super
    /// Admin could send <c>legacyTpsId</c> on a <c>PUT</c> and get <c>200 OK</c> while the value was
    /// quietly discarded. That is the exact "silent discard" the field's own documentation, and
    /// <c>JsonUnmappedMemberHandling.Disallow</c> on these types, exist to prevent: the caller is
    /// told they recorded an origin when nothing was recorded.
    /// </remarks>
    [Fact]
    public async Task ASuperAdmin_SupplyingProvenanceOnUpdate_IsRefused()
    {
        // Arrange
        var (id, name) = await SeedMigratedClientAsync();
        var original = await ProvenanceOfAsync(name);
        original.ShouldNotBeNullOrWhiteSpace();

        // Act
        var response = await factory
            .AsCompassSuperAdmin()
            .PutAsJsonAsync(
                $"{ClientsRoute}/{id}",
                new
                {
                    clientName = name,
                    isInternal = false,
                    legacyTpsId = "tps-client-fabricated-on-update",
                },
                Token
            );

        // Assert — refused, and the stored origin untouched.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ProvenanceOfAsync(name)).ShouldBe(original);
    }

    /// <summary>
    /// An ordinary update that never mentions provenance still succeeds.
    /// </summary>
    /// <remarks>
    /// The regression guard for the fix above: <see cref="CompassLegacyProvenance.MaySet"/> answers
    /// true for an ABSENT value, so adding the guard to update must not make the Compass SPA's own
    /// edit — which never sends the field — start failing. Without this, "refuse provenance on
    /// update" could be implemented as "refuse update" and no test would notice.
    /// </remarks>
    [Fact]
    public async Task ASuperAdmin_UpdatingWithoutProvenance_Succeeds()
    {
        // Arrange
        var (id, name) = await SeedMigratedClientAsync();
        var original = await ProvenanceOfAsync(name);

        // Act
        var response = await factory
            .AsCompassSuperAdmin()
            .PutAsJsonAsync(
                $"{ClientsRoute}/{id}",
                new { clientName = name, isInternal = true },
                Token
            );

        // Assert — allowed, and provenance is untouched because update never writes it.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ProvenanceOfAsync(name)).ShouldBe(original);
    }

    /// <summary>
    /// The migration principal may still PUT a record carrying its provenance.
    /// </summary>
    /// <remarks>
    /// This is the migration's coach pass, and it is why the guard cannot simply refuse the field
    /// on update for everyone. <c>Loader</c>'s second pass re-sends the SAME
    /// <c>CompassEdjerRequest</c> it created the EDJEr with — <c>payload.Request with {
    /// CoachEmployeeId = ... }</c> — so the body still carries <c>legacyTpsId</c>. A blanket refusal
    /// would fail every coach update and strand the migration halfway. The value is not re-applied
    /// (provenance is immutable once set); it is simply not grounds for refusal from this caller.
    /// </remarks>
    [Fact]
    public async Task MigrationPrincipal_MayUpdateARecordCarryingProvenance()
    {
        // Arrange
        var (id, name) = await SeedMigratedClientAsync();
        var original = await ProvenanceOfAsync(name);

        // Act
        var response = await AsMigrationPrincipal()
            .PutAsJsonAsync(
                $"{ClientsRoute}/{id}",
                new
                {
                    clientName = name,
                    isInternal = false,
                    legacyTpsId = original,
                },
                Token
            );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ProvenanceOfAsync(name)).ShouldBe(original);
    }

    /// <summary>
    /// A second record reusing a legacy identifier is told the IDENTIFIER collided, not the name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the half no unit test can prove. The service decides which message to send from
    /// <c>CompassDuplicateKeyException.ConstraintName</c>, and the unit tests supply that name from a
    /// fabricated exception. Whether Npgsql actually populates
    /// <c>PostgresException.ConstraintName</c> on a real unique violation is provider behaviour — if it
    /// came back null the fix would silently do nothing and every unit test would still pass.
    /// </para>
    /// <para>
    /// The two clients carry DIFFERENT names on purpose. The name pre-check therefore passes and the
    /// write reaches the database, which is the only way to reach the constraint that has no
    /// pre-check at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task MigrationPrincipal_ReusingALegacyIdentifier_IsToldTheIdentifierCollided()
    {
        // Arrange
        var client = AsMigrationPrincipal();
        var legacyId = $"tps-client-{Guid.NewGuid():N}";
        var firstName = $"Provenance First {Guid.NewGuid():N}";
        var secondName = $"Provenance Second {Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync(
            ClientsRoute,
            new
            {
                clientName = firstName,
                isInternal = false,
                legacyTpsId = legacyId,
            },
            Token
        );
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act
        var second = await client.PostAsJsonAsync(
            ClientsRoute,
            new
            {
                clientName = secondName,
                isInternal = false,
                legacyTpsId = legacyId,
            },
            Token
        );

        // Assert
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await second.Content.ReadAsStringAsync(Token);
        body.ShouldContain(legacyId);
        body.ShouldNotContain(secondName, Case.Insensitive);
        (await ProvenanceOfAsync(secondName)).ShouldBeNull("the refused record must not exist");
    }

    // ------------------------------------------------------------------ the other three route families
    //
    // The guard is one line repeated at four handlers, and the clients create/update above reach only
    // two of them. These cover the remaining four: an edjer create, an edjer update, an assignment
    // create, and a billable-time-category add. Every one was uncovered by both suites.
    //
    // None of them seeds anything, deliberately. The guard is answered at the endpoint boundary
    // BEFORE any lookup or validation, so a deliberately absent id is the sharper test: without the
    // guard these requests answer 404 or 400, never 403 with this title.

    [Fact]
    public async Task ASuperAdmin_SupplyingProvenanceOnAnEdjerCreate_IsRefused()
    {
        // Arrange — the same identity-not-role point as the clients case: this principal holds the
        // Compass root role the migration principal also holds.
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            EdjersRoute,
            new
            {
                firstName = "Fabricated",
                lastName = "Origin",
                hireDate = "2026-01-05",
                email = $"fabricated.origin.{Guid.NewGuid():N}@example.test",
                employeeTypeId = 1,
                coachEmployeeId = (int?)null,
                stateOfResidence = "OH",
                isActive = true,
                timesheetRequired = true,
                canSubmitUnder40 = false,
                includeInPayroll = true,
                legacyTpsId = "tps-edjer-fabricated",
            },
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(RefusalTitle);
    }

    [Fact]
    public async Task ASuperAdmin_SupplyingProvenanceOnAnEdjerUpdate_IsRefused()
    {
        // Arrange — an id that does not exist. That is the point: the guard precedes the lookup, so
        // this must be 403-with-the-title and not 404.
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PutAsJsonAsync(
            $"{EdjersRoute}/987654",
            new
            {
                firstName = "Fabricated",
                lastName = "Origin",
                hireDate = "2026-01-05",
                email = $"fabricated.update.{Guid.NewGuid():N}@example.test",
                employeeTypeId = 1,
                coachEmployeeId = (int?)null,
                stateOfResidence = "OH",
                isActive = true,
                timesheetRequired = true,
                canSubmitUnder40 = false,
                includeInPayroll = true,
                legacyTpsId = "tps-edjer-fabricated-update",
            },
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(RefusalTitle);
    }

    [Fact]
    public async Task ASuperAdmin_SupplyingProvenanceOnAnAssignmentCreate_IsRefused()
    {
        // Arrange
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            AssignmentsRoute,
            new
            {
                employeeId = 987654,
                clientId = 987654,
                startDate = "2026-01-05",
                endDate = (string?)null,
                note = (string?)null,
                legacyTpsId = "tps-assignment-fabricated",
            },
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(RefusalTitle);
    }

    [Fact]
    public async Task ASuperAdmin_SupplyingProvenanceOnABillableCategoryAdd_IsRefused()
    {
        // Arrange — the fourth handler, on the client sub-resource rather than the client itself.
        // The clients create and update above do NOT reach it.
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            $"{ClientsRoute}/987654/{Categories}",
            new { categoryName = "Fabricated Category", legacyTpsId = "tps-category-fabricated" },
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(RefusalTitle);
    }
}
