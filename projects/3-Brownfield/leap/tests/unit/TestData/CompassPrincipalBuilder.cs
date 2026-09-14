using System.Security.Claims;
using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Platform.Auth;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.TestData;

/// <summary>
/// Builds an authenticated Compass principal from either an explicit role set or a Google group set
/// plus an environment flag, together with an authorization service carrying the REAL policy
/// registrations.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists (task T004). Five stories in Stream 1 — US3, US4, US5, US6 and US8 — each
/// need to vary a principal's role set and its environment. Built once here so they vary the same
/// thing the same way, rather than five files each hand-rolling a <see cref="ClaimsIdentity"/>.
/// </para>
/// <para>
/// Two entry paths, and the difference matters.
/// </para>
/// <list type="bullet">
/// <item>
/// <see cref="WithRoles"/> takes roles that are ALREADY resolved and stamps them straight into
/// <c>Privilege</c> claims. The environment flag is irrelevant on this path — resolution has already
/// happened — so a test that wants to prove environment scoping must not use it.
/// </item>
/// <item>
/// <see cref="WithGoogleGroups"/> takes Google group names and resolves them through the REAL
/// <see cref="GoogleAuthService.MapGroupsToRoles"/> against the REAL
/// <c>api/appsettings.Development.json</c> mappings, honouring <see cref="InProduction"/> /
/// <see cref="InNonProduction"/>. This is the path where a <c>-dev</c> group genuinely grants nothing
/// in production, because the production code decides that and not this builder.
/// </item>
/// </list>
/// <para>
/// No mocking framework. <see cref="ClaimsOnlyAuthorizationResolver"/> is a hand-written
/// in-memory double. NSubstitute is referenced by this project but
/// is deliberately not used here.
/// </para>
/// <para>
/// What this does NOT prove — read before trusting it. The double resolves roles from
/// <c>Privilege</c> claims only. The real <c>CompositeAuthorizationResolver</c> also unions the
/// <c>user_roles</c> table, so this builder proves policy WIRING and claim-level resolution, never the
/// database fallback. An endpoint-level check against the real resolver is T005's integration factory.
/// </para>
/// <para>
/// The timesheet half of the policy set is a hand-maintained mirror. Those 18 policies are
/// registered inline in <c>api/Program.cs</c> with no extension method to call, so they are copied here
/// and CAN drift from the composition root. The Compass half has no such problem: it calls the module's
/// own <c>AddCompassAuthorization()</c>, so it is the code under test rather than a copy of it. If a
/// timesheet policy changes shape, this list needs the same edit.
/// </para>
/// <para>
/// Never let a role set resolve to empty by accident. An empty set satisfies no policy, so a
/// "refused" assertion passes for the wrong reason and keeps passing forever. Assert
/// <see cref="CompassPrincipal.Roles"/> is what you expected, or call
/// <see cref="CompassPrincipal.ShouldHoldExactly"/>, whenever the point of the test is a denial.
/// </para>
/// </remarks>
public sealed class CompassPrincipalBuilder
{
    /// <summary>A stable identity, so a failure message never varies between runs.</summary>
    public static readonly Guid DefaultEdjeId = new("7f3d1c92-4b6a-4f21-9e58-2c0a5d8b1f44");

    /// <summary>The default email — domain-valid, so a domain check is never the reason a test fails.</summary>
    public const string DefaultEmail = "compass.principal@leadingedje.com";

    /// <summary>The default display name.</summary>
    public const string DefaultDisplayName = "Compass Test Principal";

    private readonly List<string> _explicitRoles = [];
    private readonly List<string> _googleGroups = [];
    private List<GroupRoleMapping>? _groupMappings;
    private bool _isProduction = true;
    private bool _isAuthenticated = true;
    private Guid _edjeId = DefaultEdjeId;
    private string _email = DefaultEmail;
    private string _displayName = DefaultDisplayName;

    /// <summary>Starts a new builder. Reads as <c>CompassPrincipalBuilder.A().WithRoles(...).Build()</c>.</summary>
    public static CompassPrincipalBuilder A() => new();

    /// <summary>
    /// Adds ALREADY-RESOLVED role strings, stamped directly into <c>Privilege</c> claims.
    /// </summary>
    /// <remarks>
    /// The environment flag does not apply here. Pass a role string from
    /// <see cref="RolePolicy"/> — a bare literal risks a casing or spacing drift that resolves to
    /// nothing and silently turns an access assertion into a denial.
    /// </remarks>
    public CompassPrincipalBuilder WithRoles(params string[] roles)
    {
        _explicitRoles.AddRange(roles);
        return this;
    }

    /// <summary>
    /// Adds Google group names to be resolved through the real <see cref="GoogleAuthService"/> under the
    /// configured environment.
    /// </summary>
    /// <remarks>
    /// Use this — not <see cref="WithRoles"/> — whenever the environment is the subject of the test. A
    /// group that maps to nothing in the configured environment contributes nothing, which is the
    /// behaviour under test rather than a builder quirk.
    /// </remarks>
    public CompassPrincipalBuilder WithGoogleGroups(params string[] groups)
    {
        _googleGroups.AddRange(groups);
        return this;
    }

    /// <summary>
    /// Overrides the group-to-role mappings, replacing the real <c>appsettings.Development.json</c> set.
    /// </summary>
    /// <remarks>
    /// For asserting the MECHANISM rather than the shipped configuration — for instance that a mapping
    /// whose group name lacks the <c>-dev</c> suffix is honoured only in production. Prefer the default
    /// real configuration for anything that should track what actually ships.
    /// </remarks>
    public CompassPrincipalBuilder WithGroupMappings(params GroupRoleMapping[] mappings)
    {
        _groupMappings = [.. mappings];
        return this;
    }

    /// <summary>Resolves groups as production, where bare group names are honoured. The default.</summary>
    public CompassPrincipalBuilder InProduction()
    {
        _isProduction = true;
        return this;
    }

    /// <summary>Resolves groups as non-production, where only <c>-dev</c>-suffixed names are honoured.</summary>
    public CompassPrincipalBuilder InNonProduction()
    {
        _isProduction = false;
        return this;
    }

    /// <summary>
    /// Produces an identity that is NOT authenticated, for asserting an anonymous caller reaches nothing.
    /// </summary>
    /// <remarks>
    /// An identity with no authentication type reports <c>IsAuthenticated == false</c>. Any roles
    /// configured are still stamped as claims, so a test can prove that holding a privilege claim
    /// without an authenticated identity still grants nothing.
    /// </remarks>
    public CompassPrincipalBuilder Unauthenticated()
    {
        _isAuthenticated = false;
        return this;
    }

    /// <summary>Overrides the identity claims. Every argument is optional.</summary>
    public CompassPrincipalBuilder WithIdentity(
        Guid? edjeId = null, string? email = null, string? displayName = null)
    {
        _edjeId = edjeId ?? _edjeId;
        _email = email ?? _email;
        _displayName = displayName ?? _displayName;
        return this;
    }

    /// <summary>Resolves the role set, builds the principal, and wires the real authorization policies.</summary>
    public CompassPrincipal Build()
    {
        var roles = ResolveRoles();

        var identity = _isAuthenticated
            ? new ClaimsIdentity(AuthConstants.Settings.LeadingEdjeAuthenticationType)
            : new ClaimsIdentity();

        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.EdjeIdClaim, _edjeId.ToString()));
        identity.AddClaim(new Claim(System.Security.Claims.ClaimTypes.Email, _email));
        identity.AddClaim(new Claim(AuthConstants.ClaimTypes.DisplayNameClaim, _displayName));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(AuthConstants.ClaimTypes.PrivilegeClaim, role));
        }

        var provider = BuildAuthorizationProvider();
        var user = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { RequestServices = provider, User = user };

        return new CompassPrincipal(user, httpContext, provider, roles);
    }

    /// <summary>
    /// Resolves the effective role set: explicit roles unioned with whatever the real mapper grants the
    /// configured groups in the configured environment.
    /// </summary>
    private IReadOnlyList<string> ResolveRoles()
    {
        var resolved = new List<string>(_explicitRoles);

        if (_googleGroups.Count > 0)
        {
            var options = new GoogleAuthOptions
            {
                AllowedDomain = "leadingedje.com",
                Groups = _groupMappings ?? LoadDevelopmentGroupMappings(),
            };

            // The production mapper, not a re-implementation of it: environment scoping is derived
            // inside GoogleAuthService from the "-dev" suffix, so this builder cannot get it wrong.
            var mapper = new GoogleAuthService(
                Microsoft.Extensions.Options.Options.Create(options));
            resolved.AddRange(mapper.MapGroupsToRoles(_googleGroups, _isProduction));
        }

        return [.. resolved.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Resolves roles purely from the principal's <c>Privilege</c> claims — no database.</summary>
    private sealed class ClaimsOnlyAuthorizationResolver : IAuthorizationResolver
    {
        public Task<bool> HasAnyRoleAsync(ClaimsPrincipal user, params string[] roles)
        {
            var held = HeldRoles(user);
            return Task.FromResult(roles.Any(held.Contains));
        }

        public Task<IReadOnlyList<string>> GetRolesAsync(ClaimsPrincipal user)
            => Task.FromResult<IReadOnlyList<string>>([.. HeldRoles(user)]);

        private static HashSet<string> HeldRoles(ClaimsPrincipal user) =>
            user.FindAll(AuthConstants.ClaimTypes.PrivilegeClaim)
                .Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a provider whose authorization service carries the platform's timesheet policies
    /// AND the Compass module's own registration extension.
    /// </summary>
    /// <remarks>
    /// Both halves are present on purpose: isolation is asserted in both directions, and a Compass-only
    /// policy set could not detect a Compass role leaking into timesheet.
    /// </remarks>
    private static ServiceProvider BuildAuthorizationProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAuthorizationResolver, ClaimsOnlyAuthorizationResolver>();

        services.AddAuthorization(options =>
        {
            void Single(string policy, params string[] roles) =>
                options.AddPolicy(policy, p => p.RequireAssertion(RoleAssertions.RequireLocalRole(roles)));

            Single(RolePolicy.EDJEr, "EDJEr");
            Single(RolePolicy.Manager, "Manager");
            Single(RolePolicy.TimesheetProcessor, "TimesheetProcessor");
            Single(RolePolicy.Accounting, "Accounting");
            Single(RolePolicy.HR, "HR");
            Single(RolePolicy.Ops, "Ops");
            Single(RolePolicy.PayrollProcessor, "PayrollProcessor");
            Single(RolePolicy.Admin, "Admin");
            Single(RolePolicy.SuperAdmin, "SuperAdmin");
            Single(RolePolicy.ManagerOrProcessor, "Manager", "TimesheetProcessor", "SuperAdmin");
            Single(RolePolicy.HROrSuperAdmin, "HR", "SuperAdmin");
            Single(RolePolicy.ProcessorOrAdmin, "TimesheetProcessor", "Admin", "SuperAdmin");
            Single(RolePolicy.OpsOrSuperAdmin, "Ops", "SuperAdmin");
            Single(RolePolicy.AccountingOrSuperAdmin, "Accounting", "SuperAdmin");
            Single(RolePolicy.PayrollProcessorOrSuperAdmin, "PayrollProcessor", "SuperAdmin");
            Single(RolePolicy.InvoiceAccess, "Accounting", "TimesheetProcessor", "SuperAdmin");
            Single(RolePolicy.BalanceAccess, "Ops", "HR", "SuperAdmin");
            Single(RolePolicy.ReportDownload, "Accounting", "TimesheetProcessor", "HR", "Ops", "PayrollProcessor", "SuperAdmin");
        });

        // The module's OWN registration extension — the code under test, not a copy of it.
        services.AddCompassAuthorization();

        return services.BuildServiceProvider();
    }

    /// <summary>Reads the eight real Compass mappings (and every other one) from the dev configuration.</summary>
    private static List<GroupRoleMapping> LoadDevelopmentGroupMappings()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(DevelopmentSettingsPath(), optional: false)
            .Build();

        var options = config.GetSection("GoogleAuth").Get<GoogleAuthOptions>();
        options.ShouldNotBeNull("api/appsettings.Development.json has no bindable GoogleAuth section");
        return options.Groups;
    }

    /// <summary>
    /// Walks up from the test output directory to the repository root and returns the path to
    /// <c>api/appsettings.Development.json</c>.
    /// </summary>
    /// <remarks>
    /// Anchored on <c>leap.slnx</c> — the only solution file — so it does not depend on how deep the
    /// build output sits.
    /// </remarks>
    public static string DevelopmentSettingsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "leap.slnx")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("could not locate the repository root from the test output directory");
        return Path.Combine(dir.FullName, "api", "appsettings.Development.json");
    }
}

/// <summary>
/// A built Compass principal: the claims principal, an <see cref="HttpContext"/> carrying it, the
/// authorization service, and the role set that was actually resolved.
/// </summary>
/// <remarks>
/// <see cref="Roles"/> is exposed deliberately. A test asserting refusal must be able to prove the
/// principal held the roles it meant to hold, otherwise an empty set produces the same green result.
/// </remarks>
public sealed class CompassPrincipal(
    ClaimsPrincipal user,
    HttpContext httpContext,
    ServiceProvider provider,
    IReadOnlyList<string> roles) : IDisposable
{
    /// <summary>The claims principal, carrying one <c>Privilege</c> claim per resolved role.</summary>
    public ClaimsPrincipal User { get; } = user;

    /// <summary>
    /// An <see cref="HttpContext"/> holding <see cref="User"/> and the service provider.
    /// </summary>
    /// <remarks>
    /// Required as the <c>resource</c> argument to <c>AuthorizeAsync</c>:
    /// <see cref="RoleAssertions.RequireLocalRole"/> resolves the resolver off
    /// <c>HttpContext.RequestServices</c> and throws without it.
    /// </remarks>
    public HttpContext HttpContext { get; } = httpContext;

    /// <summary>The authorization service, carrying the real timesheet and Compass policies.</summary>
    public IAuthorizationService Authorization { get; } =
        provider.GetRequiredService<IAuthorizationService>();

    /// <summary>The role strings this principal actually resolved to. May legitimately be empty.</summary>
    public IReadOnlyList<string> Roles { get; } = roles;

    /// <summary>Whether the identity is authenticated.</summary>
    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    /// <summary>Evaluates a policy against this principal through the real authorization service.</summary>
    public async Task<bool> SatisfiesAsync(string policy) =>
        (await Authorization.AuthorizeAsync(User, HttpContext, policy)).Succeeded;

    /// <summary>
    /// Asserts the resolved role set is exactly the expected one, ignoring order and casing.
    /// </summary>
    /// <remarks>
    /// Call this before asserting a denial. It is what stops "refused" from being indistinguishable
    /// from "held no roles because the builder was misconfigured".
    /// </remarks>
    public void ShouldHoldExactly(params string[] expected)
    {
        Roles.OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ShouldBe(
                expected.OrderBy(r => r, StringComparer.OrdinalIgnoreCase),
                $"resolved [{string.Join(", ", Roles)}], expected [{string.Join(", ", expected)}]");
    }

    /// <summary>Disposes the service provider built for this principal.</summary>
    public void Dispose() => provider.Dispose();
}
