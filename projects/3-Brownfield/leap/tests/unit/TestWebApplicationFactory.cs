using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Tests.Auth;
using LeadingEDJE.Leap.Api.Tests.TestDoubles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace LeadingEDJE.Leap.Api.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The in-memory database this factory's host uses, fixed for the factory's lifetime.
    /// </summary>
    /// <remarks>
    /// Held in a field on purpose. This used to be <c>Guid.NewGuid()</c> inlined in the
    /// <c>AddDbContext</c> options lambda, which does NOT give "a unique name per factory instance" as
    /// the comment there claimed: <c>AddDbContext</c>'s <c>optionsLifetime</c> defaults to Scoped, so the
    /// lambda ran once per REQUEST and every request got its own empty database. Any test that wrote
    /// through EF in one request and read it back in the next saw nothing.
    /// </remarks>
    private readonly string _dbName = $"TestDb_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Override authentication to use test handler (SuperAdmin by default)
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            // Remove all EF Core / DbContext registrations to prevent a provider conflict
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

            // Replace real repository registrations with InMemory test doubles below.
            services.RemoveAllOfType<IAuditLogRepository>();
            services.RemoveAllOfType<IPersonRepository>();
            services.RemoveAllOfType<IUserRoleRepository>();
            services.RemoveAllOfType<ISystemSettingRepository>();
            services.RemoveAllOfType<INotificationLogRepository>();

            // Register in-memory repositories (Singleton for test isolation)
            services.AddSingleton<IAuditLogRepository, InMemoryAuditLogRepository>();
            services.AddSingleton<IPersonRepository, InMemoryPersonRepository>();
            services.AddSingleton<INotificationLogRepository, InMemoryNotificationLogRepository>();
            services.AddSingleton<ISystemSettingRepository, InMemorySystemSettingRepository>();
            services.AddSingleton<IUserRoleRepository, InMemoryUserRoleRepository>();

            // Disable Quartz scheduler in tests to prevent background job interference
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

            // Remove Quartz hosted services to prevent job execution during tests
            var hostedServiceDescriptors = services
                .Where(d => d.ImplementationType?.FullName?.Contains("Quartz") == true)
                .ToList();
            foreach (var descriptor in hostedServiceDescriptors)
            {
                services.Remove(descriptor);
            }

            // Register an in-memory EF Core DbContext for services that need it.
            // Use unique name per factory instance to avoid ReferenceDataSeeder race conditions.
            // The InMemory provider ignores real transactions; endpoints that open one for the
            // relational (Postgres) path would otherwise throw the TransactionIgnoredWarning inside
            // host tests. Ignore it so those endpoints stay testable.
            services.AddDbContext<LeapDbContext>(options =>
                options.UseInMemoryDatabase(_dbName)
                       .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        });

        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");

        // Hermeticity: blank the Saml2 section so a developer's gitignored
        // appsettings.Local.json (real local SAML config) never configures the
        // handler inside test hosts.
        builder.UseSetting("Saml2:EntityId", "");
        builder.UseSetting("Saml2:IdentityProvider:EntityId", "");
        builder.UseSetting("Saml2:IdentityProvider:MetadataLocation", "");
        builder.UseSetting("Saml2:IdentityProvider:MetadataXml", "");
        builder.UseEnvironment("Testing");
    }
}

internal static class ServiceCollectionTestExtensions
{
    /// <summary>
    /// Removes every <see cref="ServiceDescriptor"/> registered for <typeparamref name="TService"/>.
    /// Used by test factories to replace production registrations with InMemory test doubles.
    /// </summary>
    public static IServiceCollection RemoveAllOfType<TService>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(TService)).ToList())
        {
            services.Remove(descriptor);
        }
        return services;
    }
}
