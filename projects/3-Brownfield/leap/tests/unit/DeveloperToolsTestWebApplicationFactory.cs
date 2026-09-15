using LeadingEDJE.Leap.Api.Modules.Compass.Dtos;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LeadingEDJE.Leap.Api.Tests;

/// <summary>
/// A host with the destructive developer-tools surface switched ON, and the clear itself stubbed.
/// </summary>
/// <remarks>
/// <para>
/// The plain <see cref="TestWebApplicationFactory"/> deliberately does NOT have the surface.
/// That host runs in the <c>Testing</c> environment, so it loads <c>appsettings.json</c> (where
/// <c>DeveloperTools:Enabled</c> is <c>false</c>) and never <c>appsettings.Development.json</c>. That
/// is not an accident to work around — it means the default unit host proves the gate's OFF state for
/// free, and <c>CompassDeveloperToolsEndpointsTests</c> asserts exactly that.
/// </para>
/// <para>
/// Only the flag is changed, not the environment. The other half of the gate — the refusal in
/// Production — is a pure function exercised directly in <c>DeveloperToolsGateTests</c>; booting a
/// whole host as <c>Production</c> would additionally switch off DevBypass and stub-login and prove
/// something about those instead.
/// </para>
/// <para>
/// Why the clear is stubbed rather than real. This host is backed by the EF Core InMemory
/// provider, which has no <c>TRUNCATE</c> — the real repository refuses a non-relational provider
/// outright, by design and with its own test. So an endpoint test here can only be about what an
/// endpoint test should be about: routing, the authorization policy, the HTTP method, and the
/// response shape. That the clear EMPTIES ANYTHING is asserted where it can be —
/// <c>tests/integration/Endpoints/CompassDeveloperToolsEndpointsTests.cs</c>, against real PostgreSQL.
/// </para>
/// </remarks>
public sealed class DeveloperToolsTestWebApplicationFactory : TestWebApplicationFactory
{
    /// <summary>The counts <see cref="StubDataResetService"/> reports, for tests to assert against.</summary>
    public static readonly CompassDataClearedResponse StubResult =
        new(3, 4, 5, 97, 0, 28, 137, new DateTime(2026, 8, 26, 14, 30, 0, DateTimeKind.Utc));

    private sealed class StubDataResetService : ICompassDataResetService
    {
        public Task<CompassDataClearedResponse> ClearAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(StubResult);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting(DeveloperToolsGate.EnabledKey, "true");
        builder.ConfigureServices(services =>
        {
            services.RemoveAllOfType<ICompassDataResetService>();
            services.AddScoped<ICompassDataResetService, StubDataResetService>();
        });
    }
}
