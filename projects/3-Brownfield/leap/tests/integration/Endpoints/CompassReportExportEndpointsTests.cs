using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using LeadingEDJE.Leap.Api.IntegrationTests.Support;
using LeadingEDJE.Leap.Api.Modules.Compass.Data.SeedData;
using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.IntegrationTests.Endpoints;

/// <summary>
/// CSV export of the three Compass reports against real PostgreSQL — issue #337.
/// </summary>
/// <remarks>
/// <para>
/// Why these are integration tests and not unit tests. The property that matters is that the
/// exported file carries the SAME rows the screen was given — so each test asks the JSON route and the
/// export route the same question and compares. A unit test over a hand-built list would assert the
/// writer, which <c>CompassCsvTests</c> already does, and would say nothing about the two routes
/// agreeing.
/// </para>
/// <para>
/// The Availability Report exports as THREE files, not one. Its sections do not share a column
/// set — §1 has four columns, §2 and §3 have six each — and
/// <see cref="AvailabilityReportDto"/>'s own remarks record that forcing them into one row shape
/// invents a column no acceptance criterion covers. A merged CSV would have to drop columns or
/// fabricate them, so the section is a required discriminator and "everything" is a zip.
/// </para>
/// </remarks>
public class CompassReportExportEndpointsTests(IntegrationTestFactory factory)
    : IntegrationTestBase(factory)
{
    private const string AvailabilityRoute = "/api/compass/reports/availability";
    private const string AvailabilityExportRoute = "/api/compass/reports/availability/export";
    private const string DurationRoute = "/api/compass/reports/assignment-duration";
    private const string DurationExportRoute = "/api/compass/reports/assignment-duration/export";
    private const string StartRoute = "/api/compass/reports/assignment-start";
    private const string StartExportRoute = "/api/compass/reports/assignment-start/export";
    private const string SowExtensionRoute = "/api/compass/reports/sow-extension";
    private const string SowExtensionExportRoute = "/api/compass/reports/sow-extension/export";
    private const string WideRange = "from=2000-01-01&to=2100-01-01";

    private readonly IntegrationTestFactory _factory = factory;

    // ------------------------------------------------------------------ availability, per section

    /// <summary>
    /// Each availability section exports as CSV with the header row the SCREEN shows, in screen order.
    /// </summary>
    /// <remarks>
    /// The headers are asserted verbatim against <c>AvailabilityReportPage.tsx</c>'s <c>COLUMNS</c>.
    /// "based upon fields that are displayed" (issue #337) is the requirement, so a column the screen
    /// does not render is a defect here, not a bonus — and a renamed screen column that is not renamed
    /// here makes the file disagree with what the user was looking at.
    /// </remarks>
    [Theory]
    [InlineData("currently-available", "EDJEr,EDJE Client Assignment Start,# of Days Available,Coach")]
    [InlineData(
        "confirmed-rollouts",
        "EDJEr,Employee Type,Current Client,Assignment End Date,# Days Until Rollout,Coach")]
    [InlineData(
        "unconfirmed-sows",
        "EDJEr,Employee Type,Current Client,Current SOW End,# Days Until SOW Expiration,Coach")]
    public async Task ExportAvailabilitySection_ReturnsCsv_HeadedByTheScreensColumns(
        string section, string expectedHeader)
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync($"{AvailabilityExportRoute}?section={section}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/csv");

        var csv = await ReadCsvAsync(response);
        CsvLines(csv)[0].ShouldBe(expectedHeader);
    }

    /// <summary>Every section's CSV carries one data row per row the screen was given.</summary>
    /// <remarks>
    /// The fidelity property, and the reason this file exists: the export is only trustworthy if it is
    /// the same population. Row COUNT is the assertion that survives a seed change; asserting specific
    /// names would pin this to today's fixture.
    /// </remarks>
    [Fact]
    public async Task EveryAvailabilitySection_ExportsOneDataRowPerRowOnScreen()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();
        var onScreen = await _factory.AsCompassSales()
            .GetFromJsonAsync<AvailabilityReportDto>(
                AvailabilityRoute, TestContext.Current.CancellationToken);
        onScreen.ShouldNotBeNull();

        (string Section, int Expected)[] cases =
        [
            ("currently-available", onScreen.CurrentlyAvailable.Count),
            ("confirmed-rollouts", onScreen.ConfirmedRollouts.Count),
            ("unconfirmed-sows", onScreen.UnconfirmedSows.Count),
        ];

        foreach (var (section, expected) in cases)
        {
            // The seed must actually populate the section, or the comparison below is vacuous.
            expected.ShouldBeGreaterThan(0, $"the seed must populate {section}");

            // Act
            var csv = await GetCsvAsync($"{AvailabilityExportRoute}?section={section}");

            // Assert — minus the header row.
            (CsvLines(csv).Length - 1).ShouldBe(expected, section);
        }
    }

    /// <summary>An unknown section is a 400, never a silently empty file.</summary>
    /// <remarks>
    /// An empty CSV is indistinguishable from "that section has no rows", which is a different and
    /// wrong answer — the same reasoning the assignment-start range guard already applies.
    /// </remarks>
    [Fact]
    public async Task ExportAvailabilitySection_UnknownSection_Returns400()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync($"{AvailabilityExportRoute}?section=nonsense", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ availability, Export All

    /// <summary>No section means "all of it": a zip carrying exactly the three section CSVs.</summary>
    [Fact]
    public async Task ExportAvailability_WithoutSection_ReturnsZipOfTheThreeSectionCsvs()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync(AvailabilityExportRoute, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/zip");

        await using var stream = await response.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        archive.Entries.Count.ShouldBe(3);
        archive.Entries.Select(e => e.Name)
            .ShouldBe(
                [
                    "availability-currently-available.csv",
                    "availability-confirmed-rollouts.csv",
                    "availability-unconfirmed-sows.csv",
                ],
                ignoreOrder: true);

        // Each entry must be a real CSV, not a zero-byte placeholder.
        foreach (var entry in archive.Entries)
        {
            entry.Length.ShouldBeGreaterThan(0, $"{entry.Name} is empty");
        }
    }

    // ------------------------------------------------------------------ the two flat reports

    /// <summary>The duration report exports the screen's columns plus its numeric sort key.</summary>
    /// <remarks>
    /// Total Days is included even though the screen does not show it as a column. The screen
    /// renders "3 yrs 4 mos (1,238 days)" and sorts on the underlying number;
    /// <see cref="AssignmentDurationRowDto.TotalDays"/>'s own remarks warn that lexical ordering puts
    /// "10 yrs" before "3 yrs". A spreadsheet has no other sort key, so exporting only the display
    /// string would hand the user a file that sorts wrongly and looks right — the exact trap the DTO
    /// documents. It is derived from displayed data, not new information.
    /// </remarks>
    [Fact]
    public async Task ExportAssignmentDuration_ReturnsCsv_WithTheScreensColumnsPlusTotalDays()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();
        var onScreen = await _factory.AsCompassSales()
            .GetFromJsonAsync<List<AssignmentDurationRowDto>>(
                DurationRoute, TestContext.Current.CancellationToken);
        onScreen.ShouldNotBeNull();
        onScreen.ShouldNotBeEmpty("the seed must produce duration rows");

        // Act
        var csv = await GetCsvAsync(DurationExportRoute);

        // Assert
        var lines = CsvLines(csv);
        lines[0].ShouldBe("EDJEr,Employee Type,Client,Coach,Duration,Total Days");
        (lines.Length - 1).ShouldBe(onScreen.Count);
    }

    /// <summary>The assignment-start lookup exports the rows for the requested range.</summary>
    [Fact]
    public async Task ExportAssignmentStart_ReturnsCsv_ForTheRequestedRange()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();
        var onScreen = await _factory.AsCompassSales()
            .GetFromJsonAsync<List<AssignmentStartRowDto>>(
                $"{StartRoute}?{WideRange}", TestContext.Current.CancellationToken);
        onScreen.ShouldNotBeNull();
        onScreen.ShouldNotBeEmpty("the seed must produce assignment starts in a wide range");

        // Act
        var csv = await GetCsvAsync($"{StartExportRoute}?{WideRange}");

        // Assert
        var lines = CsvLines(csv);
        lines[0].ShouldBe("EDJEr Name,Employee Type,Client Name,Assignment Start Date");
        (lines.Length - 1).ShouldBe(onScreen.Count);
    }

    /// <summary>The export honours the same inverted-range guard as the screen's own route.</summary>
    /// <remarks>
    /// Duplicating the validation is the point: an export route that answered 200 with an empty file
    /// where the JSON route answers 400 would be a second, more permissive door onto the same query.
    /// </remarks>
    [Fact]
    public async Task ExportAssignmentStart_InvertedRange_Returns400()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSales().GetAsync(
            $"{StartExportRoute}?from=2026-12-31&to=2026-01-01", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>An export with no matching rows is a header-only CSV, not a 404 and not an error.</summary>
    /// <remarks>
    /// A user who exports an empty range has asked a valid question and should get a valid, empty
    /// answer they can open — the file itself documents the columns.
    /// </remarks>
    [Fact]
    public async Task ExportAssignmentStart_RangeWithNoRows_ReturnsHeaderOnlyCsv()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act — a range the seeder cannot have populated.
        var csv = await GetCsvAsync($"{StartExportRoute}?from=1900-01-01&to=1900-01-02");

        // Assert
        CsvLines(csv).ShouldHaveSingleItem().ShouldBe(
            "EDJEr Name,Employee Type,Client Name,Assignment Start Date");
    }

    // ------------------------------------------------------------------ SOW Extension Report (issue #534)

    /// <summary>The SOW Extension Report exports the rows for the requested range.</summary>
    [Fact]
    public async Task ExportSowExtension_ReturnsCsv_ForTheRequestedRange()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();
        var onScreen = await _factory.AsCompassSales()
            .GetFromJsonAsync<List<SowExtensionRowDto>>(
                $"{SowExtensionRoute}?{WideRange}", TestContext.Current.CancellationToken);
        onScreen.ShouldNotBeNull();
        onScreen.ShouldNotBeEmpty("the seed must produce extension SOWs in a wide range");

        // Act
        var csv = await GetCsvAsync($"{SowExtensionExportRoute}?{WideRange}");

        // Assert
        var lines = CsvLines(csv);
        lines[0].ShouldBe("EDJEr Name,Employee Type,Client Name,Extension Start Date");
        (lines.Length - 1).ShouldBe(onScreen.Count);
    }

    /// <summary>The export honours the same inverted-range guard as the screen's own route.</summary>
    [Fact]
    public async Task ExportSowExtension_InvertedRange_Returns400()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSales().GetAsync(
            $"{SowExtensionExportRoute}?from=2026-12-31&to=2026-01-01", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>An export with no matching rows is a header-only CSV, not a 404 and not an error.</summary>
    [Fact]
    public async Task ExportSowExtension_RangeWithNoRows_ReturnsHeaderOnlyCsv()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act — a range the seeder cannot have populated.
        var csv = await GetCsvAsync($"{SowExtensionExportRoute}?from=1900-01-01&to=1900-01-02");

        // Assert
        CsvLines(csv).ShouldHaveSingleItem().ShouldBe(
            "EDJEr Name,Employee Type,Client Name,Extension Start Date");
    }

    // ------------------------------------------------------------------ Excel-facing details

    /// <summary>Every CSV opens as UTF-8 in Excel: the payload starts with a BOM.</summary>
    /// <remarks>
    /// Without it Excel decodes the file in the local ANSI code page and mangles any non-ASCII name.
    /// Asserted on the raw bytes, because reading the body as a string hides the BOM.
    /// </remarks>
    [Fact]
    public async Task EveryCsvExport_StartsWithAUtf8Bom()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        string[] routes =
        [
            $"{AvailabilityExportRoute}?section=currently-available",
            DurationExportRoute,
            $"{StartExportRoute}?{WideRange}",
            $"{SowExtensionExportRoute}?{WideRange}",
        ];

        foreach (var route in routes)
        {
            // Act
            var bytes = await _factory.AsCompassSales()
                .GetByteArrayAsync(route, TestContext.Current.CancellationToken);

            // Assert
            bytes.Length.ShouldBeGreaterThan(3, route);
            bytes[..3].ShouldBe([(byte)0xEF, (byte)0xBB, (byte)0xBF], route);
        }
    }

    /// <summary>Every export names a downloadable file, so a browser saves rather than renders it.</summary>
    [Theory]
    [InlineData("/api/compass/reports/availability/export?section=currently-available", ".csv")]
    [InlineData("/api/compass/reports/availability/export", ".zip")]
    [InlineData("/api/compass/reports/assignment-duration/export", ".csv")]
    [InlineData("/api/compass/reports/assignment-start/export?from=2000-01-01&to=2100-01-01", ".csv")]
    [InlineData("/api/compass/reports/sow-extension/export?from=2000-01-01&to=2100-01-01", ".csv")]
    public async Task EveryExport_SendsAnAttachmentFilename(string route, string extension)
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await _factory.AsCompassSales()
            .GetAsync(route, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var disposition = response.Content.Headers.ContentDisposition;
        disposition.ShouldNotBeNull(route);
        disposition.DispositionType.ShouldBe("attachment", route);
        var name = disposition.FileNameStar ?? disposition.FileName;
        name.ShouldNotBeNull(route);
        // The route is visible in the theory data on failure, so it is not repeated as a message:
        // Shouldly's third parameter here is Case, not a custom message.
        name.Trim('"').ShouldEndWith(extension, Case.Sensitive);
    }

    // ------------------------------------------------------------------ authorization + audit

    /// <summary>
    /// The export routes carry the SAME authorization as the screens they export — Compass Admin is
    /// refused (FR-019), Sales / Ops / Compass Super Admin are admitted.
    /// </summary>
    /// <remarks>
    /// The exact status is asserted, never "not 200" — a stale DevBypass API makes a loose denial
    /// assertion pass. An export is the highest-value route to get
    /// this wrong on: it hands over the whole population in one request.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExportRoleRouteMatrix))]
    public async Task EveryExportRoute_AdmitsPermittedRoles_AndRefusesCompassAdmin(
        string roleKey, string route, HttpStatusCode expected)
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();

        // Act
        var response = await ClientForRole(roleKey)
            .GetAsync(route, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(expected, $"{roleKey} on {route}");
    }

    public static TheoryData<string, string, HttpStatusCode> ExportRoleRouteMatrix()
    {
        string[] routes =
        [
            AvailabilityExportRoute,
            $"{AvailabilityExportRoute}?section=currently-available",
            DurationExportRoute,
            $"{StartExportRoute}?{WideRange}",
            $"{SowExtensionExportRoute}?{WideRange}",
        ];
        (string Role, HttpStatusCode Expected)[] roles =
        [
            ("Sales", HttpStatusCode.OK),
            ("Ops", HttpStatusCode.OK),
            ("SuperAdmin", HttpStatusCode.OK),
            ("Admin", HttpStatusCode.Forbidden),
        ];

        var data = new TheoryData<string, string, HttpStatusCode>();
        foreach (var route in routes)
        {
            foreach (var (role, expected) in roles)
            {
                data.Add(role, route, expected);
            }
        }

        return data;
    }

    /// <summary>
    /// Exporting writes NO audit row, consistent with opening the screen (FR-025, Principle VIII).
    /// </summary>
    /// <remarks>
    /// A deliberate decision recorded rather than assumed (issue #337): an export is a read, and reads
    /// on these surfaces are unaudited. If that is ever revisited, this test is the one that has to
    /// change, which is what makes the decision visible.
    /// </remarks>
    [Fact]
    public async Task ExportingAReport_WritesNoAuditRow()
    {
        // Arrange
        await ResetDatabaseAsync();
        await SeedAsync();
        var before = await CountAuditLogsAsync();
        var client = _factory.AsCompassSales();

        // Act
        foreach (var route in new[]
                 {
                     AvailabilityExportRoute,
                     $"{AvailabilityExportRoute}?section=confirmed-rollouts",
                     DurationExportRoute,
                     $"{StartExportRoute}?{WideRange}",
                     $"{SowExtensionExportRoute}?{WideRange}",
                 })
        {
            (await client.GetAsync(route, TestContext.Current.CancellationToken))
                .StatusCode.ShouldBe(HttpStatusCode.OK, route);
        }

        // Assert
        var after = await CountAuditLogsAsync();
        after.ShouldBe(before, "an export is a read and writes no audit row (FR-025, Principle VIII)");
    }

    // ------------------------------------------------------------------ helpers

    private async Task<string> GetCsvAsync(string route)
    {
        var response = await _factory.AsCompassSales()
            .GetAsync(route, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, route);

        return await ReadCsvAsync(response);
    }

    /// <summary>Reads the body as text with the UTF-8 BOM stripped, so line 0 is the header row.</summary>
    private static async Task<string> ReadCsvAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        return new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');
    }

    /// <summary>
    /// Splits on CRLF, dropping the trailing blank. Deliberately naive: none of the asserted HEADER
    /// rows contain a quoted newline, and every assertion here is about the header or the row COUNT.
    /// Quoting is proven by <c>CompassCsvTests</c> over the writer itself.
    /// </summary>
    private static string[] CsvLines(string csv) =>
        csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    private HttpClient ClientForRole(string roleKey) => roleKey switch
    {
        "Sales" => _factory.AsCompassSales(),
        "Ops" => _factory.AsCompassOps(),
        "SuperAdmin" => _factory.AsCompassSuperAdmin(),
        "Admin" => _factory.AsCompassAdmin(),
        _ => throw new ArgumentOutOfRangeException(nameof(roleKey), roleKey, "unknown Compass role key"),
    };

    private async Task<int> CountAuditLogsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeapDbContext>();

        return await db.Set<AuditLog>().CountAsync(TestContext.Current.CancellationToken);
    }

    private async Task<DateOnly> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LeapDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<ICompassBusinessDate>().Today();

        await CompassDirectorySeeder.SeedAsync(context, today, TestContext.Current.CancellationToken);

        return today;
    }
}
