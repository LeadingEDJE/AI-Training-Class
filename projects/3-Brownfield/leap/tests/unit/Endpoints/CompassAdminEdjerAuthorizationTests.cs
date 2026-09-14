using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Server-side authorization for EDJEr configuration — every route, every insufficient role.
/// </summary>
/// <remarks>
/// <para>
/// SC-006 is not satisfied by hiding a control. AC-44 requires read-only to be "enforced
/// server-side, not merely by hiding controls", and Principle IV says "read-only means read-only". Every
/// route below is called DIRECTLY, with the interface bypassed entirely.
/// </para>
/// <para>
/// The role that matters most is Compass Admin. <c>RolePolicy.CompassAdmin</c> resolves to
/// "Compass Admin" OR "Compass Super Admin", so a route gated with that policy by mistake would admit
/// exactly the role AC-44 excludes — and would pass every other test in this feature.
/// </para>
/// </remarks>
public class CompassAdminEdjerAuthorizationTests
{
    private const string Edjers = "/api/compass/v1/admin/edjers";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompassEdjerRequest Smuggled =>
        new(
            "Smuggled",
            "Edjer",
            new DateOnly(2020, 1, 6),
            "smuggled@example.test",
            EmployeeTypeId: 1,
            CoachEmployeeId: null,
            StateOfResidence: "OH",
            IsActive: true,
            TimesheetRequired: true,
            CanSubmitUnder40: false,
            IncludeInPayroll: true
        );

    [Fact]
    public async Task Post_AsCompassAdmin_IsRefused()
    {
        // Arrange — the load-bearing case (FR-018, AC-44, SC-006).
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.PostAsJsonAsync(Edjers, Smuggled, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_AsCompassAdmin_IsRefused_BeforeTheHandlerRuns()
    {
        // Arrange — the id is deliberately one that does not exist. A 404 here would mean the policy ran
        // AFTER the handler looked the record up, which leaks whether a given EDJEr exists to a role that
        // must not be writing at all.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.PutAsJsonAsync($"{Edjers}/999999", Smuggled, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.StatusCode.ShouldNotBe(
            HttpStatusCode.NotFound,
            "authorization must be decided before the handler reads anything"
        );
    }

    [Fact]
    public async Task Get_AsCompassAdmin_IsRefused()
    {
        // Arrange — these routes serve the Super-Admin-only configuration record, whose payload carries
        // the time-tracking flags BR-1 restricts. A Compass Admin reads EDJErs through the directory
        // boundary instead, which returns a DTO with no flags on it at all (FR-016).
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassAdmin();

        // Act
        var response = await client.GetAsync(Edjers, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_AsTheTimesheetRoot_IsRefused()
    {
        // Arrange — Compass inherits nothing, not even root (Principle IV). A timesheet SuperAdmin is
        // not a Compass administrator.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsTimesheetRootOnly();

        // Act
        var response = await client.PostAsJsonAsync(Edjers, Smuggled, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Compass Ops")]
    [InlineData("Compass Sales")]
    public async Task Post_AsAnotherCompassRole_IsRefused(string role)
    {
        // Arrange — spec A-1: only the Compass Super Admin writes anything in this stream. Ops's writes
        // begin with assignments in Stream 3.
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsRoles(role);

        // Act
        var response = await client.PostAsJsonAsync(Edjers, Smuggled, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_WithNoPrivilegesAtAll_IsRefused()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.NoPrivilegesHeader, "true");

        // Act
        var response = await client.PostAsJsonAsync(Edjers, Smuggled, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_Unauthenticated_IsChallenged()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");

        // Act
        var response = await client.PostAsJsonAsync(Edjers, Smuggled, Token);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The positive control.
    /// </summary>
    /// <remarks>
    /// Without it, every refusal above could pass because the routes reject everyone, or because they do
    /// not exist and the 403 comes from somewhere else entirely. This is the shape this repository has
    /// been bitten by: a gate that matches nothing passes rather than fails.
    /// </remarks>
    [Fact]
    public async Task Post_AsTheCompassRoot_IsPermitted()
    {
        // Arrange
        await using var factory = new TestWebApplicationFactory();
        var client = factory.AsCompassSuperAdmin();

        // Act
        var response = await client.PostAsJsonAsync(Edjers, Smuggled, Token);

        // Assert — not necessarily 201 (the seeded employee type may not exist in this factory), but
        // decidedly NOT a refusal: the policy admitted the caller and the handler answered.
        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// FR-016 / BR-1, asserted where it is actually reachable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The requirement is that the three time-tracking flags are absent from the payload for a
    /// role below Compass Super Admin, not merely hidden in the UI. Every route in this group already
    /// refuses such a role outright (above), so the surface where a lesser role CAN read an EDJEr is the
    /// directory boundary.
    /// </para>
    /// <para>
    /// Amended for spec 009 Slice 1. This used to assert <c>CompassEmployeeDto</c> had exactly
    /// three members and none of them a flag — true only while the boundary published one data kind.
    /// FR-004 now grows it additively with a nullable, tier-gated <c>TimeTracking</c> block, so the
    /// three flags are never flattened onto the DTO directly (still asserted below) but the guarantee
    /// against a lesser role SEEING them is now on <c>TimeTracking</c> itself: it must be nullable and
    /// carry <c>[JsonIgnore(WhenWritingNull)]</c>, so an under-privileged caller gets no key at all, not
    /// a null one — a null would still tell them the field exists. Exercised end-to-end (real HTTP,
    /// real JSON) by <c>CompassEmployeeBoundaryTests</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBoundaryDto_WithholdsTimeTrackingFlags_UnlessTheViewerIsSuperAdmin()
    {
        // Arrange / Act
        var members = typeof(CompassEmployeeDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        // Assert -- the flags are never flattened onto the top-level DTO.
        members.ShouldNotContain(nameof(CompassEdjerDto.TimesheetRequired));
        members.ShouldNotContain(nameof(CompassEdjerDto.CanSubmitUnder40));
        members.ShouldNotContain(nameof(CompassEdjerDto.IncludeInPayroll));

        // Assert -- TimeTracking itself is the withholding mechanism: nullable, and omitted (not
        // null) when absent.
        var timeTracking = typeof(CompassEmployeeDto).GetProperty(nameof(CompassEmployeeDto.TimeTracking));
        timeTracking.ShouldNotBeNull();
        timeTracking.PropertyType.ShouldBe(typeof(CompassEmployeeTimeTrackingDto));

        var jsonIgnore = timeTracking
            .GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: false)
            .Cast<JsonIgnoreAttribute>()
            .SingleOrDefault();
        jsonIgnore.ShouldNotBeNull(
            "TimeTracking must be OMITTED, not null, for a role below Super Admin (FR-007)");
        jsonIgnore.Condition.ShouldBe(JsonIgnoreCondition.WhenWritingNull);
    }
}
