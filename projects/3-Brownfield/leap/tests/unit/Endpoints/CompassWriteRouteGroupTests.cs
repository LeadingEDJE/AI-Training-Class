using LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Write;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Tests the shared Compass write route group: the mount point every assignment/SOW write surface
/// hangs off, and the policy it attaches (feature 006, research R-7).
/// </summary>
/// <remarks>
/// <para>
/// Why this is a NEW group rather than <c>CompassAdminRouteGroup</c>.
/// <c>MapCompassAdminGroup</c> hardcodes <see cref="RolePolicy.CompassSuperAdmin"/>, and
/// <c>CompassAdminRouteGroupTests</c> already fails the build if <see cref="RolePolicy.CompassAdmin"/>
/// is substituted there. This feature's writes need a THIRD shape — <see cref="RolePolicy.CompassOps"/>
/// or the Compass root — so the group is policy-parameterised rather than reusing the admin one.
/// </para>
/// <para>
/// The surface, not just the policy, is the point of this file. ADR-008 rule 1 forbids serving a
/// viewer-dependent payload (SOW notes and the rate-increase indicator, FR-018) from
/// <c>/api/compass/v1</c>, so this group mounts on the Compass APPLICATION surface, alongside `005`'s
/// read routes — never under the published boundary.
/// </para>
/// </remarks>
public class CompassWriteRouteGroupTests
{
    private static IReadOnlyList<Endpoint> MapAndCollect(string resource, string policy)
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        var group = app.MapCompassWriteGroup(resource, policy);
        group.MapGet("/", () => Results.Ok());

        return ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(dataSource => dataSource.Endpoints)
            .ToList();
    }

    [Fact]
    public void MapCompassWriteGroup_MountsTheResourceOnTheApplicationSurface()
    {
        // Arrange & Act
        var endpoints = MapAndCollect("assignments", RolePolicy.CompassOps);

        // Assert
        var route = endpoints.ShouldHaveSingleItem().ShouldBeOfType<RouteEndpoint>();
        route.RoutePattern.RawText.ShouldNotBeNull();
        route.RoutePattern.RawText.ShouldStartWith("/api/compass/assignments", Case.Sensitive);
    }

    [Fact]
    public void MapCompassWriteGroup_IsNotMountedUnderTheVersionedBoundary()
    {
        // Arrange & Act — ADR-008 rule 1: a viewer-dependent payload must not be served from
        // /api/compass/v1. Asserting the negative here catches a copy-paste from
        // CompassAdminRouteGroup at the one shared mount point, rather than once per route group.
        var endpoints = MapAndCollect("assignments", RolePolicy.CompassOps);

        var route = endpoints.ShouldHaveSingleItem().ShouldBeOfType<RouteEndpoint>();
        route.RoutePattern.RawText.ShouldNotBeNull();
        route.RoutePattern.RawText.ShouldNotStartWith("/api/compass/v1", Case.Sensitive);
    }

    [Fact]
    public void MapCompassWriteGroup_RequiresTheSuppliedPolicy()
    {
        // Arrange & Act
        var endpoints = MapAndCollect("assignments", RolePolicy.CompassOps);

        // Assert
        var policies = endpoints
            .ShouldHaveSingleItem()
            .Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(authorizeData => authorizeData.Policy)
            .ToList();

        policies.ShouldContain(RolePolicy.CompassOps);
    }

    [Fact]
    public void MapCompassWriteGroup_DoesNotRequireTheReadOnlyCompassAdminPolicy()
    {
        // Arrange & Act
        var endpoints = MapAndCollect("assignments", RolePolicy.CompassOps);

        // Assert — the whole reason this file exists alongside CompassAdminRouteGroupTests.
        // RolePolicy.CompassAdmin resolves to "Compass Admin" OR "Compass Super Admin", and Compass
        // Admin is READ-ONLY in Compass however administrative the name sounds (AC-44). Attaching it
        // here would be a privilege escalation dressed as a refactor.
        var policies = endpoints
            .ShouldHaveSingleItem()
            .Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(authorizeData => authorizeData.Policy)
            .ToList();

        policies.ShouldNotContain(RolePolicy.CompassAdmin);
    }

    [Fact]
    public void CompassOpsPolicy_IsNotSatisfiedByTheCompassAdminRole()
    {
        // Arrange & Act & Assert — a guard on the assumption the test above rests on.
        RolePolicy.CompassOps.ShouldNotBe(RolePolicy.CompassAdmin);
    }

    [Fact]
    public void MapCompassWriteGroup_IsPolicyParameterised_NotHardcodedToOneRole()
    {
        // Arrange & Act — the property CompassAdminRouteGroup deliberately does NOT have: this group
        // must accept whichever policy its caller supplies, because a future write surface (e.g. a
        // Compass Sales-gated one) needs a different policy on the SAME mount convention.
        var opsEndpoints = MapAndCollect("assignments", RolePolicy.CompassOps);
        var salesEndpoints = MapAndCollect("leads", RolePolicy.CompassSales);

        // Assert
        var opsPolicies = opsEndpoints
            .ShouldHaveSingleItem()
            .Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .ToList();
        var salesPolicies = salesEndpoints
            .ShouldHaveSingleItem()
            .Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .ToList();

        opsPolicies.ShouldContain(RolePolicy.CompassOps);
        salesPolicies.ShouldContain(RolePolicy.CompassSales);
    }
}
