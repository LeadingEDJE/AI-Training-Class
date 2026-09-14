using LeadingEDJE.Leap.Api.Platform.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Support;

/// <summary>
/// A single Program.cs host bound to an EXTERNALLY-OWNED MySQL database (the test class owns the
/// container). Unlike <see cref="SamlSignInTestFactory"/> — which starts and migrates its own
/// container — this factory takes a shared connection string so MULTIPLE instances can be pointed
/// at the SAME database. That is what proves cross-instance cookie survival: instance 1 issues an
/// encrypted session cookie, instance 2 (a distinct host with its own in-memory Data Protection
/// cache) must still decrypt it — only possible when the key ring is persisted to the shared DB
/// and both hosts share the application name (Program.cs SetApplicationName + PersistKeysToDbContext).
/// </summary>
public sealed class DataProtectionSignInFactory(
    string connectionString, string contentRoot,
    string? dataProtectionKeyDirectory = null)
    : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    private readonly string _connectionString = connectionString;
    private readonly string _contentRoot = contentRoot;
    // When non-null, drives Program.cs onto the shared file-system key-directory branch
    // (PersistKeysToFileSystem + DisableAutomaticKeyGeneration) instead of PersistKeysToDbContext.
    // This is the config key Helm 45-04 sets as Auth__DataProtectionKeyDirectory.
    private readonly string? _dataProtectionKeyDirectory = dataProtectionKeyDirectory;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // A DISTINCT content root per instance forces a distinct default Data Protection
        // application discriminator (it defaults to the content-root path), so two instances do
        // NOT share the machine-wide filesystem key ring — exactly like two pods with separate
        // ephemeral filesystems. The ONLY way a cookie can then cross instances is the shared,
        // DB-persisted key ring pinned to a common application name (Program.cs). Without that
        // wiring the cross-instance replay must fail, which is what makes this a real RED.
        builder.UseContentRoot(_contentRoot);

        builder.ConfigureServices(services =>
        {
            // Test-only external-scheme sign-in (Sustainsys stand-in). Never registered in the app.
            services.AddSingleton<IStartupFilter, ExternalSchemeSignInStartupFilter>();

            // Remove ALL EF Core descriptors, then register real PostgreSQL against the SHARED database.
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
                options.UseNpgsql(_connectionString)
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

        if (_dataProtectionKeyDirectory is not null)
        {
            builder.UseSetting("Auth:DataProtectionKeyDirectory", _dataProtectionKeyDirectory);
        }

        builder.UseSetting("POSTGRES_DB", "timesheet_test");
        builder.UseSetting("StartupTasks:SeedReferenceData", "false");

        builder.UseSetting("GoogleAuth:AllowedDomain", "leadingedje.com");
        builder.UseSetting("GoogleAuth:Groups:0:GroupName", "Timesheet-SuperAdmin-dev");
        builder.UseSetting("GoogleAuth:Groups:0:Role", "SuperAdmin");

        // Hermeticity: blank the Saml2 section so a developer's gitignored
        // appsettings.Local.json (real local SAML config) never configures the
        // handler inside test hosts.
        builder.UseSetting("Saml2:EntityId", "");
        builder.UseSetting("Saml2:IdentityProvider:EntityId", "");
        builder.UseSetting("Saml2:IdentityProvider:MetadataLocation", "");
        builder.UseSetting("Saml2:IdentityProvider:MetadataXml", "");
        builder.UseEnvironment("Testing");
        builder.UseSetting("DetailedErrors", "true");
    }
}
