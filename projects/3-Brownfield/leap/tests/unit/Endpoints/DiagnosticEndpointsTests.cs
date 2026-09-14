using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Tests.Auth;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Endpoints;

/// <summary>
/// Tests for /_diag/secrets, which returns presence flags rather than values for the Google Docs,
/// Slack and SES configuration, and only outside Production.
/// </summary>
public class DiagnosticEndpointsTests
{
    [Fact]
    public async Task Diag_Secrets_InDevelopment_ReturnsAllPresentWhenConfigured()
    {
        // Arrange
        using var factory = new DiagnosticTestFactory(
            environment: "Development",
            configOverrides: new Dictionary<string, string?>
            {
                ["GoogleCredentials"] = "{\"type\": \"service_account\"}",
                ["Slack:BotToken"] = "fixture-present-marker",
                ["Email:SesRegion"] = "us-east-1",
            });
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/_diag/secrets", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        payload.GetProperty("google").GetString().ShouldBe("present");
        payload.GetProperty("slack").GetString().ShouldBe("present");
        payload.GetProperty("ses").GetString().ShouldBe("present");
    }

    [Fact]
    public async Task Diag_Secrets_InDevelopment_ReturnsAbsentWhenUnconfigured()
    {
        // Arrange
        using var factory = new DiagnosticTestFactory(
            environment: "Development",
            configOverrides: new Dictionary<string, string?>
            {
                ["GoogleCredentials"] = null,
                ["Slack:BotToken"] = null,
                ["Email:SesRegion"] = null,
            });
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/_diag/secrets", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        payload.GetProperty("google").GetString().ShouldBe("absent");
        payload.GetProperty("slack").GetString().ShouldBe("absent");
        payload.GetProperty("ses").GetString().ShouldBe("absent");
    }

    [Fact]
    public async Task Diag_Secrets_InProduction_Returns404()
    {
        // Arrange
        using var factory = new DiagnosticTestFactory(environment: "Production");
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/_diag/secrets", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Diag_Secrets_InStaging_ReturnsPresenceFlags()
    {
        // Arrange — ephemeral EKS previews set ASPNETCORE_ENVIRONMENT=Staging,
        // so the endpoint must be registered there (not just Development).
        // Production still 404s (covered above).
        using var factory = new DiagnosticTestFactory(
            environment: "Staging",
            configOverrides: new Dictionary<string, string?>
            {
                ["GoogleCredentials"] = "{\"type\": \"service_account\"}",
                ["Slack:BotToken"] = "fixture-present-marker",
                ["Email:SesRegion"] = null,
            });
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/_diag/secrets", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        payload.GetProperty("google").GetString().ShouldBe("present");
        payload.GetProperty("slack").GetString().ShouldBe("present");
        payload.GetProperty("ses").GetString().ShouldBe("absent");
    }

    [Fact]
    public async Task Diag_Secrets_DoesNotLeakSecretValues()
    {
        // Arrange
        const string leakCanaryFixture = "fixture-leak-canary-NEVER-APPEAR-IN-OUTPUT";
        using var factory = new DiagnosticTestFactory(
            environment: "Development",
            configOverrides: new Dictionary<string, string?>
            {
                ["Slack:BotToken"] = leakCanaryFixture,
            });
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/_diag/secrets", TestContext.Current.CancellationToken);
        var bodyText = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        bodyText.ShouldNotContain("NEVER-APPEAR-IN-OUTPUT");
    }
}

/// <summary>
/// Per-test WebApplicationFactory supporting custom environment + configuration overrides.
/// Mirrors the test-isolation strategy used by <see cref="TestWebApplicationFactory"/> but with
/// caller-controlled environment (Development vs Production) to exercise the IsDevelopment gate.
/// </summary>
internal sealed class DiagnosticTestFactory(
    string environment,
    IReadOnlyDictionary<string, string?>? configOverrides = null) : WebApplicationFactory<Program>
{
    private readonly string _environment = environment;
    private readonly IReadOnlyDictionary<string, string?> _configOverrides =
        configOverrides ?? new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        // AddHttpClient is registered unconditionally in Program.cs, so no connection-string
        // handling is needed here — the InMemory DbContext swap below is all that's required to
        // boot Program.cs without a real database.
        foreach (var kvp in _configOverrides)
        {
            builder.UseSetting(kvp.Key, kvp.Value);
        }

        builder.ConfigureServices(services =>
        {
            // Replace auth with TestAuthHandler (SuperAdmin claims) so the pipeline doesn't
            // require real JWT validation — the endpoint itself is unauthenticated, but the
            // rest of the pipeline expects auth services to resolve.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            // Swap MySQL DbContext for InMemory to allow Program.cs to boot without a DB.
            var efDescriptors = services
                .Where(d => d.ServiceType.FullName != null
                    && (d.ServiceType == typeof(DbContextOptions<LeapDbContext>)
                        || d.ServiceType == typeof(DbContextOptions)
                        || d.ServiceType == typeof(LeapDbContext)
                        || d.ServiceType.FullName.Contains("EntityFrameworkCore")
                        || d.ServiceType.FullName.Contains("NamingConventions")))
                .ToList();
            foreach (var descriptor in efDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<LeapDbContext>(options =>
                options.UseInMemoryDatabase($"DiagTestDb_{Guid.NewGuid():N}"));

            // Replace all EF-backed repositories with in-memory equivalents (same pattern as TestWebApplicationFactory).
            ReplaceSingleton<IAuditLogRepository, InMemoryAuditLogRepository>(services);
            ReplaceSingleton<IPersonRepository, InMemoryPersonRepository>(services);
            ReplaceSingleton<INotificationLogRepository, InMemoryNotificationLogRepository>(services);
            ReplaceSingleton<ISystemSettingRepository, InMemorySystemSettingRepository>(services);
            ReplaceSingleton<IUserRoleRepository, InMemoryUserRoleRepository>(services);

            // Strip Quartz — no background jobs during tests.
            var quartzDescriptors = services
                .Where(d => d.ServiceType.FullName != null
                    && (d.ServiceType.FullName.Contains("Quartz")
                        || d.ServiceType == typeof(ISchedulerFactory)
                        || d.ServiceType == typeof(IScheduler)))
                .ToList();
            foreach (var descriptor in quartzDescriptors)
            {
                services.Remove(descriptor);
            }
            var hostedQuartz = services
                .Where(d => d.ImplementationType?.FullName?.Contains("Quartz") == true)
                .ToList();
            foreach (var descriptor in hostedQuartz)
            {
                services.Remove(descriptor);
            }
        });

        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");
    }

    private static void ReplaceSingleton<TService, TImpl>(IServiceCollection services)
        where TService : class
        where TImpl : class, TService
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(TService));
        if (existing is not null)
        {
            services.Remove(existing);
        }
        services.AddSingleton<TService, TImpl>();
    }
}
