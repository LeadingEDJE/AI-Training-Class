using LeadingEDJE.Leap.Api.Modules.Compass;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Authorization;

/// <summary>
/// The Compass policies must name NO authentication scheme.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This looks like a test of nothing. It is a test of a regression that shipped.
/// </para>
/// <para>
/// Feature 010 needed the migration principal's bearer token to reach these policies, and the
/// obvious way to do that is to name <c>[cookie, MigrationPrincipal]</c> on each one. Naming a
/// scheme makes authorization re-authenticate through it — discarding whatever
/// <c>HttpContext.User</c> already held. The local <c>DevBypass</c> middleware sets that user
/// DIRECTLY rather than being a scheme, so the moment a migration token was configured, every
/// Compass route answered 401 for a local developer. Observed against a running stack.
/// </para>
/// <para>
/// The migration principal reaches these policies a different way: through the DEFAULT scheme, which
/// the composition root turns into a forwarding policy scheme when a token is configured. That keeps
/// the routing decision in one place and leaves the policies alone.
/// </para>
/// <para>
/// So: if a future change adds <c>AddAuthenticationSchemes(...)</c> to a Compass policy, this
/// test fails, and that is the whole point. The symptom it prevents appears only when a
/// migration token is set — which no local environment does by default — so nothing else would
/// catch it before a deployed run.
/// </para>
/// </remarks>
public class MigrationPrincipalPolicyTests
{
    private static AuthorizationPolicy? PolicyFor(string name)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCompassAuthorization();

        var provider = services.BuildServiceProvider();

        return provider
            .GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(name)
            .GetAwaiter()
            .GetResult();
    }

    [Theory]
    [InlineData(RolePolicy.CompassSuperAdmin)]
    [InlineData(RolePolicy.CompassAdmin)]
    [InlineData(RolePolicy.CompassOps)]
    [InlineData(RolePolicy.CompassSales)]
    public void EveryCompassPolicy_NamesNoAuthenticationScheme(string policyName)
    {
        // Act
        var policy = PolicyFor(policyName);

        // Assert — an empty scheme list means the policy reads HttpContext.User, which is what keeps
        // DevBypass working. See the type remarks for the regression this prevents.
        policy.ShouldNotBeNull();
        policy.AuthenticationSchemes.ShouldBeEmpty(
            $"{policyName} names an authentication scheme, which makes authorization re-authenticate "
                + "and discard the user DevBypass set — breaking every Compass route locally the "
                + "moment a migration token is configured."
        );
    }

    [Theory]
    [InlineData(RolePolicy.CompassSuperAdmin)]
    [InlineData(RolePolicy.CompassAdmin)]
    [InlineData(RolePolicy.CompassOps)]
    [InlineData(RolePolicy.CompassSales)]
    public void EveryCompassPolicy_IsRegistered(string policyName)
    {
        // Act / Assert — a policy that silently failed to register would deny everything, which
        // reads as an authorization bug rather than a wiring one.
        PolicyFor(policyName).ShouldNotBeNull();
    }

    [Fact]
    public void TheForwardingSchemeIsNamedDistinctlyFromTheHandlerScheme()
    {
        // Assert — they are two different things and conflating them produces an infinite forward:
        // a policy scheme that forwards to itself. Cheap to pin, confusing to debug.
        Platform.Auth.MigrationPrincipal.ForwardingScheme
            .ShouldNotBe(Platform.Auth.MigrationPrincipal.Scheme);
    }
}
