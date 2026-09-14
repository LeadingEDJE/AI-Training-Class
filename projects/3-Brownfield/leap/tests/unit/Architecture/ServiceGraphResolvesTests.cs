using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// The application's service graph builds and resolves. Boots the real host and pulls services out
/// of a scope that used to sit on either side of a dependency-injection cycle.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists at all: nothing else in this repository would catch a cycle in the container
/// graph. <c>IEmployeeDirectory</c> and <c>ICallerDirectory</c> both used to be implemented inside a
/// feature module whose write side could (and once did) reach back into <c>IAuditService</c>, itself
/// a consumer of the read port — one class serving both directions closes a dependency-injection
/// cycle. Left uncaught, that cycle crashes <c>make dev-all</c> loudly, passes CI, and rolls out green
/// before serving 500s on every audited request. It gets that far because:
/// </para>
/// <list type="bullet">
///   <item><description>
///   Both test hosts call <c>UseEnvironment("Testing")</c>, and ASP.NET Core enables container
///   validation only under <c>IsDevelopment()</c>. <c>api/Program.cs</c> sets no
///   <c>UseDefaultServiceProvider</c> of its own.
///   </description></item>
///   <item><description>
///   The unit tests construct these services by hand, bypassing dependency injection
///   entirely.
///   </description></item>
///   <item><description>
///   The readiness probe is a raw <c>NpgsqlConnection</c> that deliberately never touches EF or DI,
///   so a broken container still reports healthy.
///   </description></item>
/// </list>
/// <para>
/// Plain resolution is the mechanism, deliberately — not <c>ValidateOnBuild</c>. The
/// container throws <c>A circular dependency was detected</c> on resolve, not only on build,
/// so cycle detection needs no options.
/// </para>
/// <para>
/// <c>IEmployeeDirectory</c> is now Platform-owned end to end (<c>PersonEmployeeDirectory</c>, reading
/// <c>people</c> directly) after the Timesheet/Ooto module retirement, so the cycle this test guarded
/// against can no longer arise through that seam. <c>ICallerDirectory</c> is still implemented by the
/// Compass module — the same "a module supplies a Platform contract" shape — and its own call-time
/// behaviour lives in <c>CompassCallerDirectoryTests</c>; this test proves only that the graph builds
/// with that edge in it.
/// </para>
/// </remarks>
public class ServiceGraphResolvesTests
{
    [Fact]
    public void TheApplicationServiceGraph_ResolvesTheDirectoryAndAuditSeam()
    {
        // Arrange
        using var factory = new TestWebApplicationFactory();

        // Forces the host to build; the container is not constructed until it has.
        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        // Act — a cycle throws here, on resolve, with no validation options required
        var reader = scope.ServiceProvider.GetRequiredService<IEmployeeDirectory>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        // The caller port, implemented by the Compass module.
        var caller = scope.ServiceProvider.GetRequiredService<ICallerDirectory>();

        // Assert
        reader.ShouldNotBeNull();
        audit.ShouldNotBeNull();
        notifications.ShouldNotBeNull();
        caller.ShouldNotBeNull();
    }

    /// <summary>
    /// The audit-trail access seam resolves, with no registered rules.
    /// </summary>
    /// <remarks>
    /// The Timesheet module owned the only <see cref="IAuditTrailAccessRule"/> implementation
    /// (per-entity-type ownership checks); with the module gone, <c>AuditTrailAccessPolicy</c> now
    /// resolves against an empty rule set. That is a legitimate state, not a regression: every entity
    /// type falls through to the role-based fallback in <c>AuditLogEndpoints</c> instead of an
    /// ownership rule. This test still proves the seam resolves — a duplicate rule registration would
    /// throw here, at resolve time, before it reached CI as a request-time 500.
    /// </remarks>
    [Fact]
    public void TheApplicationServiceGraph_ResolvesTheAuditTrailAccessSeam_WithNoRules()
    {
        // Arrange
        using var factory = new TestWebApplicationFactory();
        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        // Act — a cycle, or a duplicate entity type, throws here
        var policy = scope.ServiceProvider.GetRequiredService<IAuditTrailAccessPolicy>();
        var rules = scope.ServiceProvider.GetRequiredService<IEnumerable<IAuditTrailAccessRule>>()
            .ToList();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserContext>();

        // Assert
        policy.ShouldNotBeNull();
        currentUser.ShouldNotBeNull();
        rules.ShouldBeEmpty();
        policy.HasRule("Timesheet").ShouldBeFalse();
        policy.HasRule("UserRole").ShouldBeFalse();
    }
}
