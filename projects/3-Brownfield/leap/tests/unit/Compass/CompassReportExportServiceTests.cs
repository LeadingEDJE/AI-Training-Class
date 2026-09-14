using System.IO.Compression;
using System.Text;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Compass;

/// <summary>
/// The report export service's rendering rules (issue #337).
/// </summary>
/// <remarks>
/// <para>
/// These are the RULES; the integration tests are the WIRING.
/// <c>CompassReportExportEndpointsTests</c> proves the export carries the same rows the JSON route
/// returns — a property only a real database can establish. What it cannot cheaply pin down is the
/// per-cell behaviour: that a null coach renders empty rather than dropping the row, that two clients
/// join with a semicolon, that a date is <c>MM/dd/yyyy</c> and not ISO. Those are pure functions of a
/// fixed input, so they belong here, where the input can be built to contain the awkward cases.
/// </para>
/// <para>
/// Hand-written stubs, no mocking framework — the house pattern.
/// </para>
/// </remarks>
public class CompassReportExportServiceTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 25);

    // ------------------------------------------------------------------ §1 Currently Available

    [Fact]
    public async Task CurrentlyAvailable_RendersTheScreensColumns_WithDatesAsMonthDayYear()
    {
        // Arrange
        var service = Service();

        // Act
        var file = await service.ExportAvailabilitySectionAsync(
            AvailabilitySection.CurrentlyAvailable, TestContext.Current.CancellationToken);

        // Assert
        var lines = Lines(file);
        lines[0].ShouldBe("EDJEr,EDJE Client Assignment Start,# of Days Available,Coach");
        lines[1].ShouldBe("Odessa Ferrante,08/25/2021,1826,Priya Raghunathan");
    }

    [Fact]
    public async Task ACoachlessEdjer_KeepsItsRow_WithAnEmptyCoachCell()
    {
        // FR-013: a missing coach is never a reason to drop the row, and this report is the
        // compensating control for the notification Stream 3 skips — a dropped row loses the control.
        var file = await Service().ExportAvailabilitySectionAsync(
            AvailabilitySection.CurrentlyAvailable, TestContext.Current.CancellationToken);

        Lines(file)[2].ShouldBe("Marcus Bellweather,,,");
    }

    // ------------------------------------------------------------------ §2 / §3

    [Fact]
    public async Task ConfirmedRollouts_JoinsMultipleClientsWithASemicolon()
    {
        // A comma would force the field to be quoted and read as a list inside one cell anyway, and it
        // invites a reader to split on the wrong character.
        var file = await Service().ExportAvailabilitySectionAsync(
            AvailabilitySection.ConfirmedRollouts, TestContext.Current.CancellationToken);

        var lines = Lines(file);
        lines[0].ShouldBe(
            "EDJEr,Employee Type,Current Client,Assignment End Date,# Days Until Rollout,Coach");
        lines[1].ShouldBe(
            "Imogen Eastbrook,Full Time,Kestrel Aviation Services; Dunhaven Property Group,"
            + "09/30/2026,36,Devon Okafor");
    }

    [Fact]
    public async Task UnconfirmedSows_RenderAMissingDateAsEmpty_NotAsToday()
    {
        // UnconfirmedSowRowDto spells out why: on a triage screen an invented "expires today" is the
        // most urgent value there is and would sort to the top. The export must not manufacture it.
        var file = await Service().ExportAvailabilitySectionAsync(
            AvailabilitySection.UnconfirmedSows, TestContext.Current.CancellationToken);

        var lines = Lines(file);
        lines[0].ShouldBe(
            "EDJEr,Employee Type,Current Client,Current SOW End,# Days Until SOW Expiration,Coach");
        lines[1].ShouldBe("Willa Fontaine,Part Time,Ardent Manufacturing Group,,,Devon Okafor");
    }

    // ------------------------------------------------------------------ the two flat reports

    [Fact]
    public async Task AssignmentDuration_ExportsTotalDaysBesideTheDisplayString()
    {
        // The display string sorts lexically in a spreadsheet -- "10 yrs" before "3 yrs" -- and a
        // spreadsheet has no other sort key, so the number ships as its own column.
        var file = await Service().ExportAssignmentDurationAsync(TestContext.Current.CancellationToken);

        var lines = Lines(file);
        lines[0].ShouldBe("EDJEr,Employee Type,Client,Coach,Duration,Total Days");
        lines[1].ShouldBe(
            "Odessa Ferrante,Full Time,Cascade Mutual Insurance,Priya Raghunathan,"
            + "\"7 yrs (2,558 days)\",2558");
    }

    [Fact]
    public async Task AssignmentStart_ExportsTheScreensFourColumns()
    {
        var file = await Service().ExportAssignmentStartAsync(
            new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);

        var lines = Lines(file);
        lines[0].ShouldBe("EDJEr Name,Employee Type,Client Name,Assignment Start Date");
        lines[1].ShouldBe("Katarina Eastbrook,Full Time,Foxglove Analytics,03/04/2024");
    }

    // ------------------------------------------------------------------ the archive

    [Fact]
    public async Task TheArchive_CarriesTheThreeSections_EachAReadableCsv()
    {
        // Arrange / Act
        var file = await Service().ExportAvailabilityArchiveAsync(TestContext.Current.CancellationToken);

        // Assert
        file.ContentType.ShouldBe("application/zip");

        using var archive = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
        archive.Entries.Select(e => e.Name).ShouldBe(
            [
                "availability-currently-available.csv",
                "availability-confirmed-rollouts.csv",
                "availability-unconfirmed-sows.csv",
            ],
            ignoreOrder: true);

        // Each entry must be a real CSV. Reading the archive at all is the assertion that matters:
        // a zip whose central directory was written before disposal is truncated and throws here.
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open(), new UTF8Encoding(false));
            var text = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            text.TrimStart('﻿').ShouldStartWith("EDJEr", Case.Sensitive);
        }
    }

    // ------------------------------------------------------------------ filenames

    [Theory]
    [InlineData(AvailabilitySection.CurrentlyAvailable, "compass-availability-currently-available-2026-08-25.csv")]
    [InlineData(AvailabilitySection.ConfirmedRollouts, "compass-availability-confirmed-rollouts-2026-08-25.csv")]
    [InlineData(AvailabilitySection.UnconfirmedSows, "compass-availability-unconfirmed-sows-2026-08-25.csv")]
    public async Task ASectionFile_IsNamedForItsSectionAndTheReportsAsOfDate(
        AvailabilitySection section, string expected)
    {
        var file = await Service().ExportAvailabilitySectionAsync(
            section, TestContext.Current.CancellationToken);

        file.FileName.ShouldBe(expected);
        file.ContentType.ShouldBe("text/csv");
    }

    [Fact]
    public async Task TheArchive_IsNamedForTheReportsAsOfDate()
    {
        var file = await Service().ExportAvailabilityArchiveAsync(TestContext.Current.CancellationToken);

        file.FileName.ShouldBe("compass-availability-report-2026-08-25.zip");
    }

    [Fact]
    public async Task TheDurationFile_IsStampedWithTheBUSINESSDate_NotTheMachineClock()
    {
        // Compass's day is America/New_York. A UTC-derived stamp names the file with tomorrow's date
        // every evening after about 20:00 Eastern; the first version of this did exactly that. The
        // stub's date is deliberately not today's, so a regression to DateTime.UtcNow fails here.
        var file = await Service(businessDate: new DateOnly(2019, 3, 7))
            .ExportAssignmentDurationAsync(TestContext.Current.CancellationToken);

        file.FileName.ShouldBe("compass-assignment-duration-2019-03-07.csv");
    }

    [Fact]
    public async Task TheAssignmentStartFile_CarriesTheRange_SoTwoLookupsAreDistinguishable()
    {
        var file = await Service().ExportAssignmentStartAsync(
            new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);

        file.FileName.ShouldBe("compass-assignment-start-2024-01-01-to-2026-12-31.csv");
    }

    // ------------------------------------------------------------------ SOW Extension Report (issue #534)

    [Fact]
    public async Task SowExtension_ExportsTheScreensFourColumns()
    {
        var file = await Service().ExportSowExtensionAsync(
            new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);

        var lines = Lines(file);
        lines[0].ShouldBe("EDJEr Name,Employee Type,Client Name,Extension Start Date");
        lines[1].ShouldBe("Priya Okonkwo,Full Time,Larkspur Holdings,09/12/2024");
    }

    [Fact]
    public async Task TheSowExtensionFile_CarriesTheRange_SoTwoLookupsAreDistinguishable()
    {
        var file = await Service().ExportSowExtensionAsync(
            new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);

        file.FileName.ShouldBe("compass-sow-extension-2024-01-01-to-2026-12-31.csv");
    }

    // ------------------------------------------------------------------ the unreachable default

    [Fact]
    public async Task AnUndefinedSection_Throws_RatherThanRenderingAnEmptyFile()
    {
        // Not reachable through the endpoint, which parses a fixed slug table -- but the switch's
        // default is the difference between a loud failure and a silently empty export if a fourth
        // section is ever added and one switch is missed.
        var service = Service();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            service.ExportAvailabilitySectionAsync(
                (AvailabilitySection)99, TestContext.Current.CancellationToken));
    }

    // ------------------------------------------------------------------ helpers

    private static CompassReportExportService Service(DateOnly? businessDate = null) =>
        new(new StubReportReadService(), new StubBusinessDate(businessDate ?? AsOf));

    /// <summary>The payload as text, BOM stripped, split on the CRLF terminator.</summary>
    private static string[] Lines(CompassExportFile file) =>
        new UTF8Encoding(false).GetString(file.Content)
            .TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    private sealed class StubBusinessDate(DateOnly today) : ICompassBusinessDate
    {
        public DateOnly Today() => today;
    }

    /// <summary>
    /// A fixed report carrying the awkward cases: a coachless EDJEr with no dates at all, a two-client
    /// row, and a SOW with no end date.
    /// </summary>
    private sealed class StubReportReadService : ICompassReportReadService
    {
        public Task<AvailabilityReportDto> GetAvailabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AvailabilityReportDto
            {
                AsOfDate = AsOf,
                CurrentlyAvailable =
                [
                    new AvailableEdjerRowDto
                    {
                        EmployeeId = 1,
                        EmployeeName = "Odessa Ferrante",
                        InternalAssignmentStartDate = new DateOnly(2021, 8, 25),
                        DaysAvailable = 1826,
                        CoachName = "Priya Raghunathan",
                    },
                    new AvailableEdjerRowDto
                    {
                        EmployeeId = 2,
                        EmployeeName = "Marcus Bellweather",
                        InternalAssignmentStartDate = null,
                        DaysAvailable = null,
                        CoachName = null,
                    },
                ],
                ConfirmedRollouts =
                [
                    new ConfirmedRolloutRowDto
                    {
                        EmployeeId = 3,
                        EmployeeName = "Imogen Eastbrook",
                        EmployeeType = "Full Time",
                        Clients =
                        [
                            new DashboardBreakdownClientDto { Id = 10, Name = "Kestrel Aviation Services" },
                            new DashboardBreakdownClientDto { Id = 11, Name = "Dunhaven Property Group" },
                        ],
                        AssignmentEndDate = new DateOnly(2026, 9, 30),
                        DaysUntilRollout = 36,
                        CoachName = "Devon Okafor",
                    },
                ],
                UnconfirmedSows =
                [
                    new UnconfirmedSowRowDto
                    {
                        EmployeeId = 4,
                        EmployeeName = "Willa Fontaine",
                        EmployeeType = "Part Time",
                        Clients =
                        [
                            new DashboardBreakdownClientDto { Id = 12, Name = "Ardent Manufacturing Group" },
                        ],
                        SowId = 55,
                        SowEndDate = null,
                        DaysUntilExpiration = null,
                        CoachName = "Devon Okafor",
                    },
                ],
            });

        public Task<IReadOnlyList<AssignmentDurationRowDto>> GetAssignmentDurationAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignmentDurationRowDto>>(
            [
                new AssignmentDurationRowDto
                {
                    EmployeeId = 1,
                    EmployeeName = "Odessa Ferrante",
                    EmployeeType = "Full Time",
                    ClientId = 10,
                    ClientName = "Cascade Mutual Insurance",
                    CoachName = "Priya Raghunathan",
                    TotalDays = 2558,
                    // Carries a comma, so this row also proves the writer quotes the field.
                    DurationDisplay = "7 yrs (2,558 days)",
                },
            ]);

        public Task<IReadOnlyList<AssignmentStartRowDto>> GetAssignmentStartsAsync(
            DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignmentStartRowDto>>(
            [
                new AssignmentStartRowDto
                {
                    EmployeeName = "Katarina Eastbrook",
                    EmployeeType = "Full Time",
                    ClientName = "Foxglove Analytics",
                    StartDate = new DateOnly(2024, 3, 4),
                },
            ]);

        public Task<IReadOnlyList<SowExtensionRowDto>> GetSowExtensionsAsync(
            DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SowExtensionRowDto>>(
            [
                new SowExtensionRowDto
                {
                    EmployeeName = "Priya Okonkwo",
                    EmployeeType = "Full Time",
                    ClientName = "Larkspur Holdings",
                    ExtensionStartDate = new DateOnly(2024, 9, 12),
                },
            ]);
    }
}
