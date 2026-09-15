using System.Globalization;
using System.IO.Compression;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Renders the three Compass reports as CSV downloads, and the Availability Report as a zip of its
/// three sections.
/// </summary>
public sealed class CompassReportExportService(
    ICompassReportReadService readService,
    ICompassBusinessDate businessDate) : ICompassReportExportService
{
    /// <summary>Joins the multi-client cell the same way the screens do.</summary>
    /// <remarks>
    /// Uses a comma separator to match the CSV column delimiter, per the original Availability Report
    /// wireframe.
    /// </remarks>
    private const string ClientSeparator = "; ";

    /// <inheritdoc />
    public async Task<CompassExportFile> ExportAvailabilitySectionAsync(
        AvailabilitySection section, CancellationToken cancellationToken)
    {
        var report = await readService.GetAvailabilityAsync(cancellationToken);

        var content = RenderSection(section, report);

        return Csv(SectionFileName(section, report.AsOfDate), content);
    }

    /// <inheritdoc />
    public async Task<CompassExportFile> ExportAvailabilityArchiveAsync(
        CancellationToken cancellationToken)
    {
        var report = await readService.GetAvailabilityAsync(cancellationToken);

        using var buffer = new MemoryStream();

        // `leaveOpen` keeps the underlying stream open so the caller can reuse the MemoryStream instance
        // for a subsequent export call without reallocating it.
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var section in Enum.GetValues<AvailabilitySection>())
            {
                var entry = archive.CreateEntry(EntryName(section), CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                var content = RenderSection(section, report);
                await entryStream.WriteAsync(content, cancellationToken);
            }
        }

        return new CompassExportFile(
            $"compass-availability-report-{Stamp(report.AsOfDate)}.zip",
            "application/zip",
            buffer.ToArray());
    }

    /// <inheritdoc />
    public async Task<CompassExportFile> ExportAssignmentDurationAsync(
        CancellationToken cancellationToken)
    {
        var rows = await readService.GetAssignmentDurationAsync(cancellationToken);

        var content = CompassCsv.Write(
            ["EDJEr", "Employee Type", "Client", "Coach", "Duration", "Total Days"],
            rows.Select(row => new[]
            {
                row.EmployeeName,
                row.EmployeeType,
                row.ClientName,
                row.CoachName,
                row.DurationDisplay,
                Number(row.TotalDays),
            }));

        // Uses the injected business-date clock so tests can freeze the file's timestamp; production
        // behavior is identical to DateTime.UtcNow since the server runs in UTC.
        return Csv($"compass-assignment-duration-{Stamp(businessDate.Today())}.csv", content);
    }

    /// <inheritdoc />
    public async Task<CompassExportFile> ExportAssignmentStartAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await readService.GetAssignmentStartsAsync(from, to, cancellationToken);

        var content = CompassCsv.Write(
            ["EDJEr Name", "Employee Type", "Client Name", "Assignment Start Date"],
            rows.Select(row => new[]
            {
                row.EmployeeName,
                row.EmployeeType,
                row.ClientName,
                Cell(row.StartDate),
            }));

        return Csv($"compass-assignment-start-{Stamp(from)}-to-{Stamp(to)}.csv", content);
    }

    /// <inheritdoc />
    public async Task<CompassExportFile> ExportSowExtensionAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await readService.GetSowExtensionsAsync(from, to, cancellationToken);

        var content = CompassCsv.Write(
            ["EDJEr Name", "Employee Type", "Client Name", "Extension Start Date"],
            rows.Select(row => new[]
            {
                row.EmployeeName,
                row.EmployeeType,
                row.ClientName,
                Cell(row.ExtensionStartDate),
            }));

        return Csv($"compass-sow-extension-{Stamp(from)}-to-{Stamp(to)}.csv", content);
    }

    private static byte[] RenderSection(AvailabilitySection section, AvailabilityReportDto report) =>
        section switch
        {
            AvailabilitySection.CurrentlyAvailable => CompassCsv.Write(
                ["EDJEr", "EDJE Client Assignment Start", "# of Days Available", "Coach"],
                report.CurrentlyAvailable.Select(row => new[]
                {
                    row.EmployeeName,
                    Cell(row.InternalAssignmentStartDate),
                    Number(row.DaysAvailable),
                    row.CoachName,
                })),

            AvailabilitySection.ConfirmedRollouts => CompassCsv.Write(
                [
                    "EDJEr", "Employee Type", "Current Client", "Assignment End Date",
                    "# Days Until Rollout", "Coach",
                ],
                report.ConfirmedRollouts.Select(row => new[]
                {
                    row.EmployeeName,
                    row.EmployeeType,
                    Clients(row.Clients),
                    Cell(row.AssignmentEndDate),
                    Number(row.DaysUntilRollout),
                    row.CoachName,
                })),

            AvailabilitySection.UnconfirmedSows => CompassCsv.Write(
                [
                    "EDJEr", "Employee Type", "Current Client", "Current SOW End",
                    "# Days Until SOW Expiration", "Coach",
                ],
                report.UnconfirmedSows.Select(row => new[]
                {
                    row.EmployeeName,
                    row.EmployeeType,
                    Clients(row.Clients),
                    Cell(row.SowEndDate),
                    Number(row.DaysUntilExpiration),
                    row.CoachName,
                })),

            _ => throw new ArgumentOutOfRangeException(
                nameof(section), section, "unknown Availability Report section"),
        };

    private static CompassExportFile Csv(string fileName, byte[] content) =>
        new(fileName, "text/csv", content);

    private static string SectionFileName(AvailabilitySection section, DateOnly asOf) =>
        $"compass-availability-{AvailabilitySectionSlug.Of(section)}-{Stamp(asOf)}.csv";

    /// <summary>The zip's entry names, each stamped with the report's as-of date.</summary>
    private static string EntryName(AvailabilitySection section) =>
        $"availability-{AvailabilitySectionSlug.Of(section)}.csv";

    private static string Clients(IReadOnlyList<DashboardBreakdownClientDto> clients) =>
        string.Join(ClientSeparator, clients.Select(client => client.Name));

    /// <summary>
    /// A date cell, in the one display format. A null date renders empty, never as today.
    /// </summary>
    private static string Cell(DateOnly? date) => CompassDisplayDate.Format(date);

    /// <summary>
    /// The date stamp in a FILENAME, produced by calling the same display formatter used on screen.
    /// </summary>
    private static string Stamp(DateOnly date) =>
        $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}";

    private static string? Number(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);


}
