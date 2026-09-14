using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Support;

/// <summary>
/// Self-tests for <see cref="CompassPrincipalFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Why a test helper gets its own tests. US5 asserts that Compass writes are REFUSED. A client
/// that cannot issue a write, or that carries no roles because a header silently failed to attach, is
/// refused too — for the wrong reason, permanently green. So this file proves both directions: that a
/// chosen principal is genuinely refused, and that the same machinery genuinely succeeds when the
/// principal is entitled. Without the positive controls the denial assertions are unfalsifiable.
/// </para>
/// <para>
/// One probe surface. Compass currently exposes exactly ONE endpoint here and it is a READ
/// (<c>GET /api/compass/v1/employees/{id}</c>, gated on the Compass Admin policy). The Timesheet-write
/// positive control this file once paired it with retired with the Timesheet module; <c>CompassAuthorizationTests</c>
/// carries the write-surface tripwires against Compass's own write routes now that Compass has some.
/// </para>
/// <para>
/// The discriminator throughout is 404 versus 403 versus 401: a random GUID means an entitled
/// caller reaches the handler and gets 404, so 404 proves authorization passed, 403 proves it was
/// refused after authentication, and 401 proves it was never authenticated. Asserting "not 403" alone
/// would be satisfied by a 500.
/// </para>
/// </remarks>
#pragma warning disable IDE0290 // Primary constructor not possible: factory captured for client creation
public class CompassPrincipalFactoryTests : IntegrationTestBase
{
    private readonly IntegrationTestFactory _factory;

    public CompassPrincipalFactoryTests(IntegrationTestFactory factory) : base(factory)
    {
        _factory = factory;
    }
#pragma warning restore IDE0290

    private const string CompassReadUrl = "/api/compass/v1/employees";

    // A well-formed but absent identifier: these tests assert AUTHORIZATION outcomes, so the id only
    // has to satisfy the {id:int} route constraint and reach the handler. It must not be a Guid —
    // that no longer binds since the boundary moved to Compass's integer key.
    private static string SomeCompassEmployee() => $"{CompassReadUrl}/424242";

    // ---------------------------------------------------------------- entitled principals (positive controls)

    [Fact]
    public async Task AsCompassAdmin_ReachesTheCompassReadHandler()
    {
        // Arrange — Compass Admin is the role the one existing Compass endpoint is gated to.
        await ResetDatabaseAsync();
        var client = _factory.AsCompassAdmin();

        // Act
        var response = await client.GetAsync(SomeCompassEmployee(), TestContext.Current.CancellationToken);

        // Assert — 404 means the handler ran and found no such employee: authorization PASSED.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AsCompassSuperAdmin_ReachesTheCompassReadHandler()
    {
        // Arrange — the Compass root satisfies the other Compass policies (AC-45, BR-17).
        await ResetDatabaseAsync();
        var client = _factory.AsCompassSuperAdmin();

        // Act
        var response = await client.GetAsync(SomeCompassEmployee(), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- refused principals

    [Theory]
    [InlineData(RolePolicy.CompassOpsRole)]
    [InlineData(RolePolicy.CompassSalesRole)]
    public async Task ACompassRoleOutsideAdmin_IsRefusedTheCompassRead(string compassRole)
    {
        // Arrange
        await ResetDatabaseAsync();
        var client = _factory.AsRoles(compassRole);

        // Act
        var response = await client.GetAsync(SomeCompassEmployee(), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AsBaseEdjErOnly_IsRefusedTheCompassRead()
    {
        // Arrange — the implicit-EDJEr floor every Compass write must refuse (T042).
        await ResetDatabaseAsync();
        var client = _factory.AsBaseEdjErOnly();

        // Act
        var response = await client.GetAsync(SomeCompassEmployee(), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AsTimesheetRoot_IsRefusedTheCompassRead()
    {
        // Arrange — the direction that fails OPEN, over HTTP. Note this same client succeeds at the
        // timesheet write above, so the refusal here is specific to Compass and not a broken principal.
        await ResetDatabaseAsync();
        var client = _factory.AsTimesheetRoot();

        // Act
        var response = await client.GetAsync(SomeCompassEmployee(), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AsAuthenticatedWithNoRoles_IsForbidden_Not_Unauthorized()
    {
        // Arrange — proves the empty-header trick actually attaches and yields zero privilege claims. If
        // the header were dropped, TestAuthHandler would fall back to ALL NINE timesheet roles and this
        // would still be 403 for Compass, so this alone cannot distinguish the two states; it stays a
        // useful assertion (authenticated, so 403 and never 401) even without that second probe.
        await ResetDatabaseAsync();
        var client = _factory.AsAuthenticatedWithNoRoles();

        // Act
        var compass = await client.GetAsync(SomeCompassEmployee(), TestContext.Current.CancellationToken);

        // Assert
        compass.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AsAnonymous_IsUnauthorized_Not_Forbidden()
    {
        // Arrange — 401 and 403 are different claims about the system; conflating them hides whether
        // authentication or authorization did the refusing.
        await ResetDatabaseAsync();
        var client = _factory.AsAnonymous();

        // Act
        var response = await client.GetAsync(SomeCompassEmployee(), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ARoleStringWithWrongCasing_ResolvesToNothing()
    {
        // Arrange — documents the ordinal-comparison trap executably. Both resolution paths compare the
        // role string ordinally, while the group-to-role mapper is case-insensitive, so casing that is
        // accepted at sign-in is NOT accepted here. This is why the factory's docs insist on RolePolicy
        // constants rather than literals.
        await ResetDatabaseAsync();
        var client = _factory.AsRoles("compass admin");

        // Act
        var response = await client.GetAsync(SomeCompassEmployee(), TestContext.Current.CancellationToken);

        // Assert — refused, despite differing from the real role only by casing.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- the database fallback (what T004 cannot reach)

    [Fact]
    public async Task GrantRoleViaDatabaseAsync_SatisfiesACompassPolicy_WithNoPrivilegeClaim()
    {
        // Arrange — the resolver's additive user_roles fallback, which CompassPrincipalBuilder's
        // claims-only double structurally cannot exercise. The principal carries NO Compass claim.
        await ResetDatabaseAsync();
        await _factory.GrantRoleViaDatabaseAsync(RolePolicy.CompassAdminRole);
        var client = _factory.AsAuthenticatedWithNoRoles();

        // Act
        var response = await client.GetAsync(SomeCompassEmployee(), TestContext.Current.CancellationToken);

        // Assert — 404 means authorization passed on the database path alone.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GrantRoleViaDatabaseAsync_LeaksAcrossPrincipals_BecauseTheEdjeIdIsShared()
    {
        // Arrange — the trap, asserted rather than described. Every test client carries the SAME EdjeId,
        // the fallback is additive, and a header cannot revoke a database grant. So a Compass grant makes
        // even a base-EDJEr-only client pass a Compass policy.
        await ResetDatabaseAsync();
        await _factory.GrantRoleViaDatabaseAsync(RolePolicy.CompassAdminRole);
        var client = _factory.AsBaseEdjErOnly();

        // Act
        var response = await client.GetAsync(SomeCompassEmployee(), TestContext.Current.CancellationToken);

        // Assert — NOT Forbidden. A denial test that skipped ResetDatabaseAsync would fail here and the
        // reason would look like a policy bug rather than a dirty table. That is why this is a test.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DatabaseRolesAsync_IsEmptyAfterReset_AndReportsAGrant()
    {
        // Arrange
        await ResetDatabaseAsync();

        // Act & Assert — empty after a reset, so "no leaked grant" is checkable.
        (await _factory.DatabaseRolesAsync()).ShouldBeEmpty();

        // Act & Assert — and it reports what was granted.
        await _factory.GrantRoleViaDatabaseAsync(RolePolicy.CompassOpsRole);
        (await _factory.DatabaseRolesAsync()).ShouldBe([RolePolicy.CompassOpsRole]);
    }

    [Fact]
    public async Task GrantRoleViaDatabaseAsync_IsIdempotent()
    {
        // Arrange — it delegates to the real AssignRoleAsync, which is idempotent. Asserted so a repeated
        // grant in a later test's Arrange cannot throw on the unique index.
        await ResetDatabaseAsync();

        // Act
        await _factory.GrantRoleViaDatabaseAsync(RolePolicy.CompassSalesRole);
        await _factory.GrantRoleViaDatabaseAsync(RolePolicy.CompassSalesRole);

        // Assert — one row, not two.
        (await _factory.DatabaseRolesAsync()).ShouldBe([RolePolicy.CompassSalesRole]);
    }

    [Fact]
    public async Task TestEdjeId_MatchesTheIdTheAuthHandlerStamps()
    {
        // Arrange — if these ever diverge, GrantRoleViaDatabaseAsync writes a row keyed to a principal
        // that does not exist and every fallback test silently reverts to claims-only. The grant would
        // appear to do nothing, which is a genuinely baffling failure. Asserted end-to-end rather than by
        // comparing two constants: the grant is keyed on TestEdjeId and read back through the pipeline.
        await ResetDatabaseAsync();
        await _factory.GrantRoleViaDatabaseAsync(RolePolicy.CompassAdminRole, CompassPrincipalFactory.TestEdjeId);

        // Act
        var response = await _factory.AsAuthenticatedWithNoRoles()
            .GetAsync(SomeCompassEmployee(), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
