using LeadingEDJE.Leap.Api.Modules.Compass.Contracts;
using LeadingEDJE.Leap.Api.Modules.Compass.Data;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.Repositories;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// The Compass module's single service-registration entry point.
/// </summary>
public static class CompassServiceCollectionExtensions
{
    /// <summary>Registers the Compass module's repositories and services.</summary>
    public static IServiceCollection AddCompassServices(this IServiceCollection services)
    {
        services.AddScoped<ICompassDirectoryRepository, CompassDirectoryRepository>();
        services.AddScoped<IDirectory, CompassDirectoryService>();

        services.AddScoped<ICallerDirectory, CompassCallerDirectory>();

        services.AddScoped<ICompassUnitOfWork, CompassUnitOfWork>();

        services.AddScoped(
            typeof(ICompassLookupRepository<>),
            typeof(CompassLookupRepository<>)
        );
        services.AddScoped<ICompassLookupService, CompassLookupService>();
        // The shared derivation and the business date it consumes. Both are registered as Singleton
        // instances, since neither holds per-request state; sharing one instance across requests is
        // safe because the business date is recalculated internally on every call.
        services.AddScoped<ICompassBusinessDate, CompassBusinessDate>();
        services.AddScoped<IClientStatusDerivation, ClientStatusDerivation>();

        services.AddScoped<ICompassReadRepository, CompassReadRepository>();
        services.AddScoped<ICompassDirectoryReadService, CompassDirectoryReadService>();

        services.AddScoped<ICompassAssignmentRepository, CompassAssignmentRepository>();
        services.AddScoped<ICompassSowRepository, CompassSowRepository>();
        services.AddScoped<ICompassAssignmentService, CompassAssignmentService>();
        services.AddScoped<ICompassSowService, CompassSowService>();

        services.AddScoped<ICompassDashboardRepository, CompassDashboardRepository>();
        services.AddScoped<ICompassDashboardReadService, CompassDashboardReadService>();

        services.AddScoped<ICompassReportRepository, CompassReportRepository>();
        services.AddScoped<ICompassReportReadService, CompassReportReadService>();

        services.AddScoped<ICompassReportExportService, CompassReportExportService>();

        // EDJEr configuration. Like the lookup surface above, these writes are exempt from auditing
        // per FR-008; only lookup tables require the full audit trail described in FR-017.
        services.AddScoped<ICompassEmployeeRepository, CompassEmployeeRepository>();
        services.AddScoped<ICompassEmployeeService, CompassEmployeeService>();

        services.AddScoped<ICoachNotificationRepository, CoachNotificationRepository>();
        services.AddScoped<ICoachNotifier, CoachNotifier>();

        // The AC-19 deactivation precondition. Folded directly into the EDJEr write service rather
        // than kept separate, since the blockers route always goes through the same write path and
        // there is only ever one derivation of "is this EDJEr deactivatable" to maintain.
        services.AddScoped<IEdjerDeactivationGuard, EdjerDeactivationGuard>();

        services.AddScoped<ICompassClientRepository, CompassClientRepository>();
        services.AddScoped<ICompassClientService, CompassClientService>();

        services.AddScoped<
            ICompassMigrationProvenanceRepository,
            CompassMigrationProvenanceRepository
        >();

        services.AddScoped<ICompassSowMigrationService, CompassSowMigrationService>();

        services.AddScoped<ICompassDataResetRepository, CompassDataResetRepository>();
        services.AddScoped<ICompassDataResetService, CompassDataResetService>();

        return services;
    }
}
