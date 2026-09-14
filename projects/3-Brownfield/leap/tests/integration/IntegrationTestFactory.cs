using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Data.SeedData;
using LeadingEDJE.Leap.Api.Platform.Services;
using LeadingEDJE.Leap.Api.Tests.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Testcontainers.PostgreSql;

namespace LeadingEDJE.Leap.Api.IntegrationTests;

public class IntegrationTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:16")
        .WithUsername("timesheet")
        .WithPassword("testpass")
        .WithDatabase("timesheet_test")
        .Build();

    /// <summary>
    /// Counts SQL commands against <c>compass.employee</c>, for the constant-read assertion in
    /// <c>OotoDirectoryEndpointsTests</c> (feature 018 FR-007). Inert unless a test uses it.
    /// </summary>
    public CompassEmployeeQueryCounter CompassEmployeeQueries { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Override authentication to use test handler (SuperAdmin by default)
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            // Remove ALL EF Core descriptors (same aggressive pattern as unit test factory)
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

            // Register real PostgreSQL via Testcontainers connection string.
            //
            // CompassEmployeeQueryCounter is attached unconditionally so a test can assert a QUERY
            // COUNT rather than a duration (feature 018 FR-007). It is inert until a test calls
            // Reset() and reads Count, and one interceptor instance is safe to share because
            // IClassFixture gives each test class its own factory.
            services.AddDbContext<LeapDbContext>(options =>
                options.UseNpgsql(_postgresContainer.GetConnectionString())
                       .UseSnakeCaseNamingConvention()
                       .AddInterceptors(CompassEmployeeQueries));

            // Remove Quartz scheduler services to prevent job execution during tests
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

        // Override Postgres config so the NpgsqlHealthCheck connects to the Testcontainers instance
        builder.UseSetting("POSTGRES_HOST", _postgresContainer.Hostname);
        builder.UseSetting("POSTGRES_PORT", _postgresContainer.GetMappedPublicPort(5432).ToString());
        builder.UseSetting("POSTGRES_DB", "timesheet_test");
        builder.UseSetting("POSTGRES_USER", "timesheet");
        builder.UseSetting("POSTGRES_PASSWORD", "testpass");
        builder.UseSetting("StartupTasks:SeedReferenceData", "false");

        // Hermeticity: blank the Saml2 section so a developer's gitignored
        // appsettings.Local.json (real local SAML config) never configures the
        // handler inside test hosts.
        builder.UseSetting("Saml2:EntityId", "");
        builder.UseSetting("Saml2:IdentityProvider:EntityId", "");
        builder.UseSetting("Saml2:IdentityProvider:MetadataLocation", "");
        builder.UseSetting("Saml2:IdentityProvider:MetadataXml", "");
        builder.UseEnvironment("Testing");
        builder.UseSetting("DetailedErrors", "true");

        // Developer tools ON for the integration host (2026-08-26). This is the ONLY suite that can
        // prove the Compass clear works: the statement it issues is a real Postgres TRUNCATE, and the
        // InMemory provider the unit suite runs on cannot execute it at all.
        //
        // The OFF state is NOT proven here, deliberately -- it is proven by the unit suite, whose
        // default host leaves the flag absent and asserts the routes answer 404 rather than 403
        // (CompassDeveloperToolsEndpointsTests). Turning the flag on here therefore removes no
        // coverage; it adds the half that only real PostgreSQL can supply.
        builder.UseSetting(DeveloperToolsGate.EnabledKey, "true");

        builder.ConfigureServices(services =>
        {
            // Replace exception handler to include full exception details in responses
            services.AddProblemDetails(options =>
            {
                options.CustomizeProblemDetails = ctx =>
                {
                    var exceptionFeature = ctx.HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                    if (exceptionFeature?.Error != null)
                    {
                        ctx.ProblemDetails.Detail = exceptionFeature.Error.ToString();
                    }
                };
            });
        });
    }

    public async ValueTask InitializeAsync()
    {
        await _postgresContainer.StartAndAwaitHostConnectivityAsync();

        // Apply migrations before starting the host so schema exists for tests.
        // Startup reference seeding is disabled in Testing so ResetDatabaseAsync
        // can produce a deterministic empty database.
        // UseLeapPostgres, NOT a bare UseNpgsql: the extension pins `Search Path=public` (issue #208),
        // and migrations are exactly where that matters. Postgres's default search_path is
        // `"$user", public`; this connects as the role `timesheet` and a schema of that name exists, so
        // unqualified DDL in a migration lands in `timesheet` instead of `public` -- the migration
        // reports success and changes nothing in the schema anyone reads.
        //
        // That is not hypothetical. #317's trigger migration was written unqualified, and because THIS
        // harness bypassed the pin while the design-time factory did not, it worked against leap_dev and
        // silently did nothing here: pg_proc held `public=OLD, timesheet=NEW` and the purge kept failing
        // with the pre-migration error. Qualifying that migration fixed it; this stops the next one
        // needing to be debugged the same way.
        var optionsBuilder = new DbContextOptionsBuilder<LeapDbContext>();
        optionsBuilder.UseLeapPostgres(_postgresContainer.GetConnectionString());
        await PostgreSqlContainerExtensions.MigrateWithRetryAsync(async () =>
        {
            await using var migrationContext = new LeapDbContext(optionsBuilder.Options);
            await migrationContext.Database.MigrateAsync();
        });

        // Now force host build — Program.cs seeder will find tables already exist
        _ = Services;
    }

    public new async ValueTask DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}
