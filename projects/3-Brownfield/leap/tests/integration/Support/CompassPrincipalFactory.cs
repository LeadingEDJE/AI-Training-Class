using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Support;

/// <summary>
/// Issues HTTP clients that act as a chosen Compass role, so a read or a write can be driven straight
/// at the API with the interface bypassed.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists (task T005). The integration-side counterpart to
/// <c>CompassPrincipalBuilder</c> (T004). That builder evaluates policies in-process against a
/// claims-only double; this one goes through the REAL pipeline — real authentication handler, real
/// <c>CompositeAuthorizationResolver</c>, real endpoint filters, real HTTP status codes. US5 asserts
/// that writes are refused *at the server*, which is a claim only this side can support.
/// </para>
/// <para>
/// The default principal is a timesheet SUPER ADMIN. <see cref="TestAuthHandler"/> stamps all
/// nine timesheet roles when no override header is present. For Compass that is harmless — a timesheet
/// root satisfies no Compass policy — but it means a test that forgets to choose a principal is not
/// testing what it looks like it is testing. Always pick one of the methods below explicitly.
/// </para>
/// <para>
/// ⚠️ Role strings are matched CASE-SENSITIVELY on both resolution paths.
/// <c>CompositeAuthorizationResolver</c> checks claims with <c>ClaimsPrincipal.HasClaim(type, value)</c>
/// (ordinal on the value) and then the database with <c>roles.Contains(ur.Role)</c> (default comparer,
/// also ordinal). The group-to-role *mapper* is case-INsensitive, so casing that works at sign-in does
/// not necessarily work here. Pass a <see cref="RolePolicy"/> constant, never a hand-typed literal —
/// <c>"compass admin"</c> resolves to nothing and turns an access test into a silent denial.
/// </para>
/// <para>
/// ⚠️ Every client shares ONE EdjeId (<see cref="TestEdjeId"/>), because the test auth handler
/// stamps a fixed one. The resolver's database fallback is keyed on exactly that id, so a
/// <c>user_roles</c> row written by one test grants that role to every client in every later test in the
/// same class until the table is truncated. Call <c>ResetDatabaseAsync()</c> in each test's Arrange —
/// which the integration convention already requires — and treat a surprising 200 in a denial test as a
/// leaked grant before anything else.
/// </para>
/// <para>
/// The database fallback is ADDITIVE and cannot be revoked by a header.
/// <see cref="GrantRoleViaDatabaseAsync"/> exists so US5/US6 can prove the fallback path is enforced too,
/// but note the asymmetry: choosing a principal with no Compass claim does NOT remove a role granted in
/// the database. That is the one way a Compass denial test can pass authorization it should have failed.
/// </para>
/// </remarks>
public static class CompassPrincipalFactory
{
    /// <summary>
    /// The EdjeId <see cref="TestAuthHandler"/> stamps on every test principal, and therefore the key the
    /// resolver's <c>user_roles</c> fallback reads.
    /// </summary>
    public static readonly Guid TestEdjeId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Issues a client holding exactly <paramref name="roles"/> and nothing else.</summary>
    /// <remarks>
    /// The general form. Prefer a named method below where one fits — the name is what makes a denial
    /// test's intent legible at the call site.
    /// </remarks>
    public static HttpClient AsRoles(this IntegrationTestFactory factory, params string[] roles)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.PrivilegeOverrideHeader, string.Join(',', roles));
        return client;
    }

    /// <summary>Issues a client holding the Compass root role — must reach every Compass function (AC-45, BR-17).</summary>
    public static HttpClient AsCompassSuperAdmin(this IntegrationTestFactory factory) =>
        factory.AsRoles(RolePolicy.CompassSuperAdminRole);

    /// <summary>
    /// Issues a client holding Compass Admin — the READ-ONLY role, despite the name.
    /// </summary>
    /// <remarks>
    /// This is the principal US5 exists for: it must read everything it is entitled to see and be refused
    /// every write, at the server. "Admin" here does not mean elevated.
    /// </remarks>
    public static HttpClient AsCompassAdmin(this IntegrationTestFactory factory) =>
        factory.AsRoles(RolePolicy.CompassAdminRole);

    /// <summary>Issues a client holding Compass Ops.</summary>
    public static HttpClient AsCompassOps(this IntegrationTestFactory factory) =>
        factory.AsRoles(RolePolicy.CompassOpsRole);

    /// <summary>Issues a client holding Compass Sales.</summary>
    public static HttpClient AsCompassSales(this IntegrationTestFactory factory) =>
        factory.AsRoles(RolePolicy.CompassSalesRole);

    /// <summary>
    /// Issues a client holding only the base EDJEr role — no Compass role of any kind.
    /// </summary>
    /// <remarks>
    /// The implicit-EDJEr tier of FR-012. Every authenticated EDJEr reaches this much and no more, so it
    /// is the floor every Compass write must refuse (T042).
    /// </remarks>
    public static HttpClient AsBaseEdjErOnly(this IntegrationTestFactory factory) =>
        factory.AsRoles(RolePolicy.EDJEr);

    /// <summary>
    /// Issues a client holding the nine timesheet roles including root, and no Compass role.
    /// </summary>
    /// <remarks>
    /// The direction that fails OPEN. A timesheet <c>SuperAdmin</c> is deliberately NOT a Compass root,
    /// and this is how that is asserted over HTTP rather than only against the policy registrations.
    /// </remarks>
    public static HttpClient AsTimesheetRoot(this IntegrationTestFactory factory) =>
        factory.AsRoles(
            RolePolicy.EDJEr, RolePolicy.Manager, RolePolicy.TimesheetProcessor, RolePolicy.Accounting,
            RolePolicy.HR, RolePolicy.Ops, RolePolicy.PayrollProcessor, RolePolicy.Admin,
            RolePolicy.SuperAdmin);

    /// <summary>
    /// Issues an authenticated client carrying NO privilege claims at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="AsAnonymous"/>, and the distinction is the point: this principal is
    /// authenticated, so it earns 403 rather than 401.
    /// </para>
    /// <para>
    /// Uses a dedicated header, and must. The obvious implementation — sending
    /// <c>X-Test-Privileges</c> with an empty value — does not work: <c>HttpClient</c> does not transmit
    /// an empty header value, so the override arrives ABSENT and <see cref="TestAuthHandler"/> falls back
    /// to all nine timesheet roles including root. The resulting principal is the most privileged one
    /// available while reading as the least, and against a Compass policy the two are indistinguishable.
    /// That false pass was real and was caught only by also probing a timesheet write; hence
    /// <see cref="TestAuthHandler.NoPrivilegesHeader"/>.
    /// </para>
    /// </remarks>
    public static HttpClient AsAuthenticatedWithNoRoles(this IntegrationTestFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.NoPrivilegesHeader, "true");
        return client;
    }

    /// <summary>Issues a client that is not authenticated at all — expect 401, never 403.</summary>
    public static HttpClient AsAnonymous(this IntegrationTestFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
        return client;
    }

    /// <summary>
    /// Grants <paramref name="role"/> through the <c>user_roles</c> table rather than a claim, exercising
    /// the resolver's additive database fallback.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reaches the path <c>CompassPrincipalBuilder</c> structurally cannot: its claims-only double has no
    /// database. Use it to prove a Compass policy is enforced against database-granted roles too, and to
    /// prove a denial is not quietly rescued by one.
    /// </para>
    /// <para>
    /// Writes directly through <see cref="IUserRoleRepository"/>, deliberately bypassing
    /// <c>IUserRoleService.AssignRoleAsync</c> (spec 002 T030): that service now refuses any
    /// <see cref="KnownRoles.Compass"/> role, because the same DB fallback this helper exists to exercise
    /// is exactly what let a Timesheet-gated admin endpoint mint standing Compass authority (FR-014). This
    /// helper's whole purpose is to plant a Compass role in the table regardless of how it might get
    /// there, so it must reach past that guard rather than honour it — it no longer writes an audit-log
    /// entry as a side effect (it did while going through the service; nothing in the tree asserts on
    /// that). Defaults to <see cref="TestEdjeId"/>, the id every test client carries.
    /// </para>
    /// </remarks>
    public static async Task GrantRoleViaDatabaseAsync(
        this IntegrationTestFactory factory,
        string role,
        Guid? edjeId = null,
        string actor = "system:integration-test",
        string reason = "T005 CompassPrincipalFactory database-fallback grant")
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRoleRepository>();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var targetEdjeId = edjeId ?? TestEdjeId;

        if (await repository.FindAsync(targetEdjeId, role) is not null)
        {
            return; // Idempotent, matching AssignRoleAsync's own contract.
        }

        await repository.AddAsync(new UserRole
        {
            EdjeId = targetEdjeId,
            Role = role,
            CreatedBy = actor,
            UpdatedBy = actor,
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Returns the roles currently granted in the database for <paramref name="edjeId"/>.
    /// </summary>
    /// <remarks>
    /// Assert this is EMPTY at the start of a denial test if a leaked grant is a plausible explanation for
    /// an unexpected pass. It is the cheapest way to tell "correctly refused" from "the table was dirty".
    /// </remarks>
    public static async Task<IReadOnlyList<string>> DatabaseRolesAsync(
        this IntegrationTestFactory factory, Guid? edjeId = null)
    {
        using var scope = factory.Services.CreateScope();
        var roleService = scope.ServiceProvider.GetRequiredService<IUserRoleService>();
        return await roleService.GetRolesAsync(edjeId ?? TestEdjeId);
    }
}
