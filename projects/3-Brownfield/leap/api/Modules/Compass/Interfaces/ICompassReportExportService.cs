using LeadingEDJE.Leap.Api.Modules.Compass.Services;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>Renders the Compass reports as downloadable files.</summary>
/// <remarks>
/// Every method goes through <c>ICompassReportReadService</c> — the same call the JSON route makes —
/// so an export cannot drift from what the screen was given. That is the whole design constraint: a
/// second query shaped "like" the report's would be a second definition of the report. The
/// Availability Report has no single-file form, because its three sections do not share a column set,
/// so the caller either names a section or gets all three zipped
/// (<see cref="AvailabilitySection"/>).
/// </remarks>
public interface ICompassReportExportService
{
    /// <summary>Renders one Availability Report section as CSV.</summary>
    Task<CompassExportFile> ExportAvailabilitySectionAsync(
        AvailabilitySection section, CancellationToken cancellationToken);

    /// <summary>Renders all three Availability Report sections as a zip of three CSVs.</summary>
    Task<CompassExportFile> ExportAvailabilityArchiveAsync(CancellationToken cancellationToken);

    /// <summary>Renders the Client Assignment Duration report as CSV.</summary>
    Task<CompassExportFile> ExportAssignmentDurationAsync(CancellationToken cancellationToken);

    /// <summary>Renders the Assignment Start lookup for an inclusive range as CSV.</summary>
    Task<CompassExportFile> ExportAssignmentStartAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Renders the SOW Extension Report for an inclusive range as CSV.</summary>
    Task<CompassExportFile> ExportSowExtensionAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>The three Availability Report sections, as an export discriminator.</summary>
/// <remarks>
/// An enum rather than a free string so an unknown section is a binding failure at the endpoint —
/// a 400 — instead of reaching a service that would have to invent a fallback. An empty CSV would be
/// indistinguishable from "that section has no rows", which is a different and wrong answer.
/// </remarks>
public enum AvailabilitySection
{
    /// <summary>§1 — Currently Available EDJErs (FR-010).</summary>
    CurrentlyAvailable,

    /// <summary>§2 — Confirmed Rollouts (FR-011).</summary>
    ConfirmedRollouts,

    /// <summary>§3 — Unconfirmed SOWs expiring within 90 days (FR-012, BR-5).</summary>
    UnconfirmedSows,
}
