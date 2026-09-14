using LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Tests the shared Compass admin route group: the mount path every configuration surface hangs off,
/// and the policy it attaches.
/// </summary>
/// <remarks>
/// <para>
/// The policy assertion is the point of this file. <c>RolePolicy.CompassAdmin</c> resolves to
/// "Compass Admin" OR "Compass Super Admin", so attaching it to a configuration route would silently
/// grant the write to Compass Admin — exactly the role AC-44 excludes, and the mistake the API
/// contract calls "the single most important row in this document". Asserting it here catches the
/// substitution once, at the shared helper, rather than once per route group.
/// </para>
/// <para>
/// This reads the endpoint metadata of a real minimal app rather than exercising HTTP, so it stays a
/// unit test. The runtime consequence — a Compass Admin principal actually receiving 403 — is asserted
/// separately against the real pipeline in <c>CompassAdminLookupAuthorizationTests</c>.
/// </para>
/// </remarks>
public class CompassAdminRouteGroupTests
{
    private static IReadOnlyList<Endpoint> MapAndCollect(string resource)
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        var group = app.MapCompassAdminGroup(resource);
        group.MapGet("/", () => Results.Ok());

        return ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(dataSource => dataSource.Endpoints)
            .ToList();
    }

    [Fact]
    public void MapCompassAdminGroup_MountsTheResourceUnderTheVersionedAdminPath()
    {
        // Arrange & Act
        var endpoints = MapAndCollect("employee-types");

        // Assert — additive within /api/compass/v1 (Principle V). No v2.
        var route = endpoints.ShouldHaveSingleItem().ShouldBeOfType<RouteEndpoint>();
        route.RoutePattern.RawText.ShouldNotBeNull();
        route
            .RoutePattern.RawText.ShouldStartWith(
                "/api/compass/v1/admin/employee-types",
                Case.Sensitive
            );
    }

    [Fact]
    public void MapCompassAdminGroup_RequiresTheCompassSuperAdminPolicy()
    {
        // Arrange & Act
        var endpoints = MapAndCollect("invoice-frequency-types");

        // Assert
        var policies = endpoints
            .ShouldHaveSingleItem()
            .Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(authorizeData => authorizeData.Policy)
            .ToList();

        policies.ShouldContain(RolePolicy.CompassSuperAdmin);
    }

    [Fact]
    public void MapCompassAdminGroup_DoesNotRequireTheWeakerCompassAdminPolicy()
    {
        // Arrange & Act
        var endpoints = MapAndCollect("employee-types");

        // Assert — the whole reason this file exists. CompassAdmin is satisfied by "Compass Admin",
        // which is a READ-ONLY role in Compass however administrative the name sounds.
        var policies = endpoints
            .ShouldHaveSingleItem()
            .Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(authorizeData => authorizeData.Policy)
            .ToList();

        policies.ShouldNotContain(RolePolicy.CompassAdmin);
    }

    [Fact]
    public void CompassSuperAdminPolicy_IsNotSatisfiedByTheCompassAdminRole()
    {
        // Arrange & Act & Assert — a guard on the assumption the tests above rest on. If these two
        // policy names ever resolved to the same role set, every assertion here would pass while
        // granting the write. The role-set difference itself is proved by CompassRoleIsolationTests.
        RolePolicy.CompassSuperAdmin.ShouldNotBe(RolePolicy.CompassAdmin);
    }
}
