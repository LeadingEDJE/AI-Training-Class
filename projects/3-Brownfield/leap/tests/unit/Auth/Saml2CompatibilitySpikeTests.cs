using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Sustainsys.Saml2.AspNetCore2;
using Sustainsys.Saml2.Metadata;

namespace LeadingEDJE.Leap.Api.Tests.Auth;

/// <summary>
/// Phase 41-01 hard-gate spike: proves Sustainsys.Saml2.AspNetCore2 2.11.0 (net8 target)
/// registers and serves SP metadata on the .NET 10 runtime without a boot-time or handler-API
/// break. This is the #1 phase risk; downstream plans consume the handler abstractly, so this
/// test pins the compatibility contract (app boots + GET /Saml2 returns SP metadata XML).
/// It mirrors TPS's two-cookie registration shape (external holding-pen cookie + AddSaml2).
/// </summary>
public class Saml2CompatibilitySpikeTests
{
    [Fact]
    public async Task Saml2Handler_RegistersAndServesSpMetadata_OnNet10()
    {
        // Arrange: minimal host mirroring TPS's two-cookie SAML registration.
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddAuthentication()
                            .AddCookie("saml-external")
                            .AddSaml2(options =>
                            {
                                options.SignInScheme = "saml-external";
                                options.SPOptions.EntityId = new EntityId("https://localhost/Saml2");
                            });
                    })
                    .Configure(app =>
                    {
                        // The Sustainsys handler serves SP metadata from its module path via the
                        // authentication middleware — no endpoint routing/authorization needed.
                        app.UseAuthentication();
                    });
            })
            .StartAsync(TestContext.Current.CancellationToken);

        var client = host.GetTestClient();

        // Act: request the SP metadata endpoint Sustainsys serves at the module root.
        var response = await client.GetAsync("/Saml2", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: the handler boots and emits SP metadata describing this service provider.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("EntityDescriptor");
        body.ShouldContain("https://localhost/Saml2");
    }
}
