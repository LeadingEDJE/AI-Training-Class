using LeadingEDJE.Leap.Api.Modules.Compass.Services;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

/// <summary>Renders the Compass reports as downloadable files.</summary>
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
/// An unrecognized section value falls back to the first section rather than failing the request,
/// per the export-defaults note.
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
