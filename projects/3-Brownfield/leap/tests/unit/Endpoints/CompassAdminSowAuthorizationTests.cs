using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Server-side authorization for the contract-period configuration route.
/// </summary>
/// <remarks>
/// <para>
/// This route carries more than the usual authorization weight: it is the only way to write a
/// <see cref="SowType.LegacyMigrated"/> period, and that type is exempt from BOTH partial database
/// rules on <c>compass.sow</c>. A caller who reaches it gains a permanent way past the overlap
/// constraint and the date-order CHECK.
/// </para>
/// <para>
/// The service-level restriction is proven by
/// <c>CompassSowLegacyMigratedRestrictionTests</c>. This file proves the same thing through
/// HTTP, because a service rule with no route covering it is a rule that a future handler can
/// forget to call.
/// </para>
/// </remarks>
public class CompassAdminSowAuthorizationTests
{
    private const string Sows = "/api/compass/v1/admin/sows";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompassSowRequest Smuggled(SowType type) =>
        new(
            ClientAssignmentId: 1,
            SowType: type,
            RateIncrease: false,
            SowStartDate: new DateOnly(2024, 1, 1),
            SowEndDate: new DateOnly(2024, 12, 31),
            Note: null
        );

    [Fact]
    public async Task Post_AsCompassAdmin_IsRefused()
    {
        // Arrange — the read-only role (AC-44, SC-006).
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.PostAsJsonAsync(Sows, Smuggled(SowType.InitialContract), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_AsATimesheetRoot_IsRefused()
    {
        // Arrange — Principle IV: Compass inherits nothing.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsTimesheetRootOnly();

        // Act
        var response = await client.PostAsJsonAsync(Sows, Smuggled(SowType.InitialContract), Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_LegacyMigrated_AsTheCompassRoot_IsRefusedWithExactly403()
    {
        // Arrange — ⚠️ THE ONE THAT MATTERS. A Compass Super Admin is the highest authority the
        // application offers, and must still be refused this type (Principle VIII: a
        // validation-bypass path is reachable only by the migration principal).
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(Sows, Smuggled(SowType.LegacyMigrated), Token);

        // Assert — the EXACT status. "Not 201" would also pass for a 404 or a 400, and would keep
        // passing if the restriction were replaced by an unrelated failure.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_ANonLegacyType_AsTheCompassRoot_ReachesTheHandler()
    {
        // Arrange — the other half: restricting LegacyMigrated must not restrict what the
        // application legitimately creates. Without this, a route refusing everything would pass
        // every refusal test above.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act — the assignment does not exist, so the handler answers 404. That is the proof: the
        // request got past authorization and into the handler.
        var response = await client.PostAsJsonAsync(Sows, Smuggled(SowType.InitialContract), Token);

        // Assert
        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_AsCompassAdmin_IsRefused()
    {
        // Arrange — ⚠️ the READ routes are gated identically to the writes, and that is deliberate.
        // They hang off CompassAdminRouteGroup, which attaches CompassSuperAdmin. A read-only
        // "Compass Admin" is refused here NOT because reading is dangerous, but because these routes
        // exist for the migration's verification rather than for any application screen — widening
        // them would be inventing a requirement.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.GetAsync(Sows, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_AsTheCompassRoot_IsPermitted()
    {
        // Arrange — the mirror. Without it, a route gated to nobody would pass every refusal test.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync(Sows, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
