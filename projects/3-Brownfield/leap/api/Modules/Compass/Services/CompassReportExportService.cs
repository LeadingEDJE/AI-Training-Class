using System.Globalization;
using System.IO.Compression;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Renders the three Compass reports as CSV downloads, and the Availability Report as a zip of its
/// three sections.
/// </summary>
/// <remarks>
/// The column sets are copied from the screens deliberately and literally — the requirement is the data
/// the user sees — so each header array below mirrors a <c>COLUMNS</c> constant in the corresponding
/// page component, in the same order, and <c>CompassReportExportEndpointsTests</c> asserts the header
/// rows verbatim. Date cells use <see cref="CompassDisplayDate"/>, the same <c>MM/dd/yyyy</c> the
/// screen shows; ISO was rejected because a spreadsheet cell is read by a person, and sorting is not
/// lost since Excel parses this form. This service holds no query of its own: every method calls
/// <see cref="ICompassReportReadService"/>, so an export cannot drift from the screen's definition.
/// </remarks>
public sealed class CompassReportExportService(
    ICompassReportReadService readService,
    ICompassBusinessDate businessDate) : ICompassReportExportService
{
    /// <summary>Joins the multi-client cell the same way the screens do.</summary>
    /// <remarks>
    /// A semicolon, not a comma: a comma would force the field to be quoted and read as a list inside
    /// one cell anyway, and it invites a reader to split on the wrong character.
    /// </remarks>
    private const string ClientSeparator = "; ";

    /// <inheritdoc />
    public async Task<CompassExportFile> ExportAvailabilitySectionAsync(
        AvailabilitySection section, CancellationToken cancellationToken)
    {
        var report = await readService.GetAvailabilityAsync(cancellationToken);

        // Render BEFORE naming, and via a local rather than relying on argument-evaluation order.
        // An unknown section then fails in the renderer -- which owns what a section MEANS -- rather
        // than in the filename helper it happens to be passed to first. Both guards throw the same
        // way, so this changes no behaviour; it makes each one independently reachable, and a guard
        // no test can reach is a guard nobody has checked.
        var content = RenderSection(section, report);

        return Csv(SectionFileName(section, report.AsOfDate), content);
    }

    /// <inheritdoc />
    public async Task<CompassExportFile> ExportAvailabilityArchiveAsync(
        CancellationToken cancellationToken)
    {
        var report = await readService.GetAvailabilityAsync(cancellationToken);

        using var buffer = new MemoryStream();

        // `leaveOpen` so the archive can be disposed -- which is what writes the central directory --
        // before the buffer is read. Reading before that disposal yields a truncated, unopenable zip.
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

        // Total Days is exported alongside the display string even though the screen shows only the
        // string. AssignmentDurationRowDto.TotalDays warns that lexical ordering puts "10 yrs" before
        // "3 yrs"; a spreadsheet has no other sort key, so exporting the string alone hands over a file
        // that sorts wrongly and looks right. It is derived from a displayed value, not new data.
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

        // The business date, NOT DateTime.UtcNow. Compass's day is America/New_York, so a
        // UTC-derived stamp names the file with tomorrow's date every evening after about 20:00
        // Eastern. The first version of this line did exactly that and argued a filename did not
        // count; CompassBusinessDateTests disagreed, correctly.
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

        // The range is in the filename: this report is a lookup, so two exports differing only by
        // range are the common case and indistinguishable files would be a real nuisance.
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

        // The range is in the filename, matching ExportAssignmentStartAsync: this report is a lookup,
        // so two exports differing only by range are the common case.
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

    /// <summary>The zip's entry names. Dateless — the archive filename already carries the date.</summary>
    private static string EntryName(AvailabilitySection section) =>
        $"availability-{AvailabilitySectionSlug.Of(section)}.csv";

    private static string Clients(IReadOnlyList<DashboardBreakdownClientDto> clients) =>
        string.Join(ClientSeparator, clients.Select(client => client.Name));

    /// <summary>
    /// A date cell, in the one display format. A null date renders empty, never as today.
    /// </summary>
    /// <remarks>
    /// <see cref="UnconfirmedSowRowDto.SowEndDate"/> spells out why absent must stay absent: on a
    /// triage screen an invented "expires today" is the most urgent value there is and would sort to
    /// the top. <see cref="CompassDisplayDate.Format(DateOnly?)"/> already renders null as empty.
    /// </remarks>
    private static string Cell(DateOnly? date) => CompassDisplayDate.Format(date);

    /// <summary>
    /// The date stamp in a FILENAME, which is year-first for sorting.
    /// </summary>
    /// <remarks>
    /// Not a <c>CompassDisplayDate</c> call, and not evading that rule. <c>MM/dd/yyyy</c>
    /// contains a path separator and cannot appear in a filename at all. Composed from the parts
    /// rather than through a <c>yyyy-MM-dd</c> format string so it is legible as what it is — a
    /// sortable file token — instead of reading like the displayed-date format that was removed.
    /// </remarks>
    private static string Stamp(DateOnly date) =>
        $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}";

    private static string? Number(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);


}
