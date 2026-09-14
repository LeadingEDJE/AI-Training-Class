using System.Net;
using System.Net.Http.Json;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Server-side authorization for lookup administration — every route, every insufficient role.
/// </summary>
/// <remarks>
/// <para>
/// SC-006 is not satisfied by hiding a control. AC-44 requires read-only to be "enforced
/// server-side, not merely by hiding controls", and Principle IV says "read-only means read-only".
/// <c>compass-nav-permissions.ts</c> gates the <c>admin-config</c> nav key to Compass Super Admin, but
/// that is a usability affordance layered on top of these checks. Each route is therefore called
/// DIRECTLY here, with the interface bypassed entirely.
/// </para>
/// <para>
/// The role that matters most is Compass Admin. <c>RolePolicy.CompassAdmin</c> resolves to
/// "Compass Admin" OR "Compass Super Admin", so a route gated with that policy by mistake would admit
/// the very role AC-44 excludes — and would pass every other test in this feature.
/// </para>
/// <para>
/// FR-009 makes lookup administration Super Admin only, so these routes refuse Compass Admin on READS
/// as well as writes. That is narrower than the general configuration-read table in the API contract,
/// and deliberately so: the lookup GET routes serve the admin screen and the Super-Admin-only
/// configuration forms. Widening reads later is a group split, not a policy swap.
/// </para>
/// </remarks>
public class CompassAdminLookupAuthorizationTests
{
    private const string EmployeeTypes = "/api/compass/v1/admin/employee-types";
    private const string InvoiceFrequencyTypes = "/api/compass/v1/admin/invoice-frequency-types";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> WriteRoutes =>
        new() { EmployeeTypes, InvoiceFrequencyTypes };

    public static TheoryData<string> AllRoutes => new() { EmployeeTypes, InvoiceFrequencyTypes };

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task Post_AsCompassAdmin_IsRefused(string route)
    {
        // Arrange — the load-bearing case (FR-009, AC-44, SC-006).
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            route,
            new CreateCompassLookupRequest("Smuggled"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task Put_AsCompassAdmin_IsRefused(string route)
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.PutAsJsonAsync(
            $"{route}/1",
            new UpdateCompassLookupRequest("Smuggled", IsActive: false),
            Token
        );

        // Assert — 403 before the handler runs, so a non-existent id must NOT surface as 404 here.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Get_AsCompassAdmin_IsRefused(string route)
    {
        // Arrange — FR-009 scopes lookup administration, reads included, to the Compass root.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.GetAsync(route, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task Post_AsTheTimesheetRoot_IsRefused(string route)
    {
        // Arrange — Compass inherits NOTHING, not even root (Principle IV). A timesheet SuperAdmin is
        // deliberately not a Compass administrator.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsTimesheetRootOnly();

        // Act
        var response = await client.PostAsJsonAsync(
            route,
            new CreateCompassLookupRequest("Smuggled"),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task Post_Unauthenticated_IsChallenged(string route)
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");

        // Act
        var response = await client.PostAsJsonAsync(
            route,
            new CreateCompassLookupRequest("Smuggled"),
            Token
        );

        // Assert — 401 for "who are you", distinct from 403 for "not you".
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task Post_AsTheCompassRoot_IsPermitted(string route)
    {
        // Arrange — the positive control. Without it every assertion above could pass because the
        // routes reject everyone, or because they do not exist at all.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(
            route,
            new CreateCompassLookupRequest($"Permitted-{Guid.NewGuid():N}"[..20]),
            Token
        );

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
