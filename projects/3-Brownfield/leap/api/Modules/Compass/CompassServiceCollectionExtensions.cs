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
/// <remarks>
/// A module registers itself in one call; the recipe is <c>docs/platform/adding-a-module.md</c>.
/// Everything is registered <c>Scoped</c>, matching the database context's lifetime.
/// </remarks>
public static class CompassServiceCollectionExtensions
{
    /// <summary>Registers the Compass module's repositories and services.</summary>
    public static IServiceCollection AddCompassServices(this IServiceCollection services)
    {
        services.AddScoped<ICompassDirectoryRepository, CompassDirectoryRepository>();
        services.AddScoped<IDirectory, CompassDirectoryService>();

        // A PLATFORM-owned port this module implements (ADR-009, the feature-017 pattern): the
        // interface lives in api/Platform/Interfaces/, the implementation here, and the module
        // registers it — so Platform can resolve the caller without depending on a module.
        services.AddScoped<ICallerDirectory, CompassCallerDirectory>();

        // The persistence boundary. Compass services cannot hold the data context themselves — rule
        // two of CompassBoundaryTests forbids the token in the Services/ folder — so they save through
        // this seam. See ICompassUnitOfWork.
        services.AddScoped<ICompassUnitOfWork, CompassUnitOfWork>();

        // Lookup administration. The repository is registered as an open generic, so each lookup entity
        // closes over the one implementation rather than each getting a near-identical class.
        services.AddScoped(
            typeof(ICompassLookupRepository<>),
            typeof(CompassLookupRepository<>)
        );
        services.AddScoped<ICompassLookupService, CompassLookupService>();
        // The shared derivation and the business date it consumes. Both are stateless, but they are
        // registered Scoped like everything else here: a request that resolved "today" twice and got
        // two different dates across a midnight boundary would produce exactly the kind of
        // inconsistency the single-derivation rule exists to prevent.
        services.AddScoped<ICompassBusinessDate, CompassBusinessDate>();
        services.AddScoped<IClientStatusDerivation, ClientStatusDerivation>();

        // The application read surface (ADR-008) — distinct from the published boundary above.
        services.AddScoped<ICompassReadRepository, CompassReadRepository>();
        services.AddScoped<ICompassDirectoryReadService, CompassDirectoryReadService>();

        // The assignment/SOW write surface — Compass's first audited write path. Module-owned
        // repositories, per the new-module rule: unlike the lookup repository above, these do not
        // extend a Platform-owned base.
        services.AddScoped<ICompassAssignmentRepository, CompassAssignmentRepository>();
        services.AddScoped<ICompassSowRepository, CompassSowRepository>();
        services.AddScoped<ICompassAssignmentService, CompassAssignmentService>();
        services.AddScoped<ICompassSowService, CompassSowService>();

        // The Sales Dashboard — a dedicated repository, not an addition to ICompassReadRepository
        // above: it has no viewer tier of its own (every CompassReporting role sees the same thing)
        // and is a materially different concern from the directory reads.
        services.AddScoped<ICompassDashboardRepository, CompassDashboardRepository>();
        services.AddScoped<ICompassDashboardReadService, CompassDashboardReadService>();

        // The reports, across two repositories, and the line between them is not "reports read from
        // here". The Availability Report reads ICompassDashboardRepository above, because its three
        // sections are the dashboard's three breakdowns re-shaped: re-deriving them here is the
        // divergence BR-11 forbids, and SC-003's tile-to-section equalities then hold for free.
        // ICompassReportRepository holds only queries no tile has a counterpart for, so none can drift.
        services.AddScoped<ICompassReportRepository, CompassReportRepository>();
        services.AddScoped<ICompassReportReadService, CompassReportReadService>();

        // CSV export of those same reports. It owns no query and no repository: it calls
        // ICompassReportReadService, so a download cannot drift from what the screen was shown.
        services.AddScoped<ICompassReportExportService, CompassReportExportService>();

        // EDJEr configuration. Unlike the lookup surface above, these writes are audited — FR-017 and
        // Principle VIII, against FR-008's deliberate exemption for lookups.
        services.AddScoped<ICompassEmployeeRepository, CompassEmployeeRepository>();
        services.AddScoped<ICompassEmployeeService, CompassEmployeeService>();

        // Coach notices. Dispatch is IEmailSender and never INotificationService — that one resolves a
        // channel preference from EmployeeAttribute, a Timesheet directory type, so calling it from
        // Compass would be the consumer-side directory read Principle V forbids. The repository is
        // module-owned even though NotificationLog is a platform entity, because the query shape
        // Compass needs is one INotificationLogRepository cannot express.
        services.AddScoped<ICoachNotificationRepository, CoachNotificationRepository>();
        services.AddScoped<ICoachNotifier, CoachNotifier>();

        // The AC-19 deactivation precondition. Registered next to the EDJEr surface because that is the
        // write it guards, but deliberately a service of its own: the blockers route reads it without
        // going through the write path, and two independent derivations of "is this EDJEr deactivatable"
        // would drift the first time either changed.
        services.AddScoped<IEdjerDeactivationGuard, EdjerDeactivationGuard>();

        // Client configuration, including the client's billable time categories. Audited for the same
        // reason as the EDJEr surface, and with the categories inside the client's audited scope
        // (AC-NFR-3) rather than carrying a trail of their own.
        services.AddScoped<ICompassClientRepository, CompassClientRepository>();
        services.AddScoped<ICompassClientService, CompassClientService>();

        // Migration provenance — reading back what the TPS migration created, so a run can reconcile
        // and spot-check itself.
        services.AddScoped<
            ICompassMigrationProvenanceRepository,
            CompassMigrationProvenanceRepository
        >();

        // Contract periods, migration path only. Separate from ICompassSowService above — the Ops
        // write surface, which refuses SowType.LegacyMigrated on input — because this service guards a
        // validation bypass: LegacyMigrated is exempt from both partial database rules and is reachable
        // only by the migration principal (Principle VIII). Folding the two would put that bypass
        // inside the surface every Compass Super Admin reaches from a browser.
        services.AddScoped<ICompassSowMigrationService, CompassSowMigrationService>();

        // Developer tools — the destructive Compass clear. Registered unconditionally, while the routes
        // that reach it are mapped only when DeveloperToolsGate allows it. Keeping the environment
        // decision in exactly one place (api/Program.cs) is deliberate: a second copy here would be a
        // second thing to get wrong, and an unreachable service registration costs nothing. Nothing
        // else in the application resolves ICompassDataResetService.
        services.AddScoped<ICompassDataResetRepository, CompassDataResetRepository>();
        services.AddScoped<ICompassDataResetService, CompassDataResetService>();

        return services;
    }
}
