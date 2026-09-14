using LeadingEDJE.Leap.Api.Platform.Authorization;

namespace LeadingEDJE.Leap.Api.Tests.Auth;

/// <summary>
/// HTTP clients acting as a chosen Compass role, for endpoint tests over
/// <see cref="TestWebApplicationFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// The integration project has an equivalent (<c>CompassPrincipalFactory</c>) over its own factory.
/// This is the unit-level counterpart; the two are deliberately not shared, because they build clients
/// for different factories.
/// </para>
/// <para>
/// Do not reach for the default client to represent "no Compass role".
/// <see cref="TestAuthHandler"/>'s fallback privileges are all nine TIMESHEET roles including root —
/// which satisfies no Compass policy, so it happens to be refused, but for a reason a reader would
/// mistake for the one under test. Say what you mean with <see cref="AsCompassAdmin"/> or
/// <see cref="AsTimesheetRootOnly"/>.
/// </para>
/// </remarks>
public static class CompassTestClients
{
    /// <summary>A client holding exactly the named roles.</summary>
    public static HttpClient AsRoles(this TestWebApplicationFactory factory, params string[] roles)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.PrivilegeOverrideHeader,
            string.Join(',', roles)
        );
        return client;
    }

    /// <summary>The Compass root — the only role permitted to write configuration.</summary>
    public static HttpClient AsCompassSuperAdmin(this TestWebApplicationFactory factory) =>
        factory.AsRoles(RolePolicy.CompassSuperAdminRole);

    /// <summary>
    /// Compass Admin — READ-ONLY in Compass however administrative the name sounds (AC-44). Every
    /// configuration write must refuse this principal.
    /// </summary>
    public static HttpClient AsCompassAdmin(this TestWebApplicationFactory factory) =>
        factory.AsRoles(RolePolicy.CompassAdminRole);

    /// <summary>
    /// The timesheet root and nothing else. Compass inherits nothing, not even root (Principle IV), so
    /// this must be refused everywhere in Compass.
    /// </summary>
    public static HttpClient AsTimesheetRootOnly(this TestWebApplicationFactory factory) =>
        factory.AsRoles(RolePolicy.SuperAdmin);

    /// <summary>
    /// Compass Ops — the role feature 006 introduces its first write surface for (FR-001, FR-006).
    /// </summary>
    /// <remarks>
    /// The integration side already has an equivalent (<c>CompassPrincipalFactory.AsCompassOps</c>);
    /// this is the unit-level counterpart, added for the same reason as the other named methods here —
    /// so a test's intent reads at the call site rather than through an unnamed role string.
    /// </remarks>
    public static HttpClient AsCompassOps(this TestWebApplicationFactory factory) =>
        factory.AsRoles(RolePolicy.CompassOpsRole);

    /// <summary>
    /// Compass Sales — read-only in Compass, same as Admin (AC-44). Holds the AC-16/FR-025 elevated-read
    /// entitlement but no write rights.
    /// </summary>
    public static HttpClient AsCompassSales(this TestWebApplicationFactory factory) =>
        factory.AsRoles(RolePolicy.CompassSalesRole);

    /// <summary>
    /// An authenticated caller holding NO privileges at all — Compass tier Baseline.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="AsTimesheetRootOnly"/>: both resolve to Baseline in Compass, but this
    /// one says "no roles" rather than "the wrong roles". The read surfaces (feature 005) grant this
    /// caller the two directories by AC-5 and AC-12, so it is a first-class principal there rather
    /// than a denial case.
    /// </remarks>
    public static HttpClient AsBaselineEdjer(this TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.NoPrivilegesHeader, "true");
        return client;
    }

    /// <summary>An unauthenticated caller.</summary>
    public static HttpClient AsAnonymous(this TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
        return client;
    }

    /// <summary>
    /// The response body as raw JSON.
    /// </summary>
    /// <remarks>
    /// Some assertions need the raw text rather than a deserialised object: FR-005 requires withheld
    /// data to be ABSENT, and a DTO with nullable properties cannot tell "absent" from "present and
    /// null".
    /// </remarks>
    public static async Task<string> GetRawJsonAsync(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
