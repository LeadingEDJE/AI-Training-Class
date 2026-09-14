using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Testcontainers.PostgreSql;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Support;

/// <summary>
/// Integration-test host for the REAL cookie + SAML-external sign-in pipeline. Unlike
/// <see cref="IntegrationTestFactory"/>, it does NOT swap authentication for
/// <c>TestAuthHandler</c> — it keeps Program.cs's genuine cookie/external-scheme registration so
/// the callback, CompleteSignIn, and cookie swap are exercised end to end. The
/// <see cref="ExternalSchemeSignInStartupFilter"/> stands in for Sustainsys; person resolution goes
/// through the real <c>people</c> table (auto-create on first sign-in), so a test seeds a row
/// directly via <see cref="LeapDbContext"/> when it needs one to already exist.
/// Runs under the "Testing" environment so DevBypass stays off and stub-login 404s.
/// </summary>
public sealed class SamlSignInTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:16")
        .WithUsername("timesheet")
        .WithPassword("testpass")
        .WithDatabase("timesheet_test")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Test-only external-scheme sign-in (Sustainsys stand-in). Never registered in the app.
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, ExternalSchemeSignInStartupFilter>();

            // Remove ALL EF Core descriptors, then register real MySQL via Testcontainers.
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
                options.UseNpgsql(_postgresContainer.GetConnectionString())
                       .UseSnakeCaseNamingConvention());

            // Strip Quartz so no jobs run during tests.
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
            var quartzHostedServices = services
                .Where(d => d.ImplementationType?.FullName?.Contains("Quartz") == true)
                .ToList();
            foreach (var descriptor in quartzHostedServices)
            {
                services.Remove(descriptor);
            }
        });

        builder.UseSetting("POSTGRES_HOST", _postgresContainer.Hostname);
        builder.UseSetting("POSTGRES_PORT", _postgresContainer.GetMappedPublicPort(5432).ToString());
        builder.UseSetting("POSTGRES_DB", "timesheet_test");
        builder.UseSetting("POSTGRES_USER", "timesheet");
        builder.UseSetting("POSTGRES_PASSWORD", "testpass");
        builder.UseSetting("StartupTasks:SeedReferenceData", "false");

        // Keep SAML unconfigured (Saml2 handler not registered) so /auth/login returns 503; the
        // pipeline is driven via the external-scheme cookie, not a live challenge. Blank the
        // section EXPLICITLY — a developer's gitignored appsettings.Local.json (real local SAML
        // config) is loaded by the app's configuration pipeline and would otherwise leak into
        // the test host and configure the handler (hermeticity, caught by the 503 test).
        builder.UseSetting("Saml2:EntityId", "");
        builder.UseSetting("Saml2:IdentityProvider:EntityId", "");
        builder.UseSetting("Saml2:IdentityProvider:MetadataLocation", "");
        builder.UseSetting("Saml2:IdentityProvider:MetadataXml", "");
        builder.UseSetting("GoogleAuth:AllowedDomain", "leadingedje.com");
        builder.UseSetting("GoogleAuth:Groups:0:GroupName", "Timesheet-SuperAdmin-dev");
        builder.UseSetting("GoogleAuth:Groups:0:Role", "SuperAdmin");
        // Authoritative org mapping: Timesheet-Admin-dev is the HR-role group;
        // Timesheet-Config-dev is the Admin-role group (there is no Timesheet-HR group).
        builder.UseSetting("GoogleAuth:Groups:1:GroupName", "Timesheet-Admin-dev");
        builder.UseSetting("GoogleAuth:Groups:1:Role", "HR");
        builder.UseSetting("GoogleAuth:Groups:2:GroupName", "Timesheet-Config-dev");
        builder.UseSetting("GoogleAuth:Groups:2:Role", "Admin");
        builder.UseSetting("GoogleAuth:Groups:3:GroupName", "Timesheet-Approval-dev");
        builder.UseSetting("GoogleAuth:Groups:3:Role", "Manager");

        builder.UseEnvironment("Testing");
        builder.UseSetting("DetailedErrors", "true");
    }

    public async ValueTask InitializeAsync()
    {
        await _postgresContainer.StartAndAwaitHostConnectivityAsync();

        var optionsBuilder = new DbContextOptionsBuilder<LeapDbContext>();
        optionsBuilder.UseNpgsql(_postgresContainer.GetConnectionString())
                      .UseSnakeCaseNamingConvention();
        await PostgreSqlContainerExtensions.MigrateWithRetryAsync(async () =>
        {
            await using var migrationContext = new LeapDbContext(optionsBuilder.Options);
            await migrationContext.Database.MigrateAsync();
        });

        _ = Services;
    }

    public new async ValueTask DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}
