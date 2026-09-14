using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Read;

/// <summary>The Compass report read surface — AC-38 to AC-40.</summary>
/// <remarks>
/// Part of the Compass application read surface (ADR-008), not the published boundary — no <c>/v1</c>
/// segment (FR-031). The namespace is a build constraint, not a preference:
/// <c>CompassTransportContractTests.EveryBoundaryHandlerPayload_IsAlsoAnIDirectoryPayload</c> scans the
/// exact namespace <c>…Modules.Compass.Endpoints</c> and requires every payload there to be one
/// <c>IDirectory</c> also returns, which <see cref="AvailabilityReportDto"/> is not. Every route
/// requires <see cref="RolePolicy.CompassReporting"/> — Sales, Ops, or the Compass root. Compass Admin
/// does not satisfy it (FR-019), and the refusal is enforced at the server, not by hiding the tab.
/// </remarks>
public static class CompassReportEndpoints
{
    /// <summary>Maps <c>/api/compass/reports</c>.</summary>
    public static RouteGroupBuilder MapCompassReportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/compass/reports")
            .WithTags("Compass Read")
            .RequireAuthorization(RolePolicy.CompassReporting);

        group.MapGet("/availability", GetAvailability);
        group.MapGet("/assignment-duration", GetAssignmentDuration);
        group.MapGet("/assignment-start", GetAssignmentStart);
        group.MapGet("/sow-extension", GetSowExtension);

        // The CSV export routes sit inside the same group, so they inherit
        // RolePolicy.CompassReporting rather than restating it — an export route that authorized
        // differently from the screen it exports would be a second, more permissive door onto the
        // same population.
        group.MapGet("/availability/export", ExportAvailability);
        group.MapGet("/assignment-duration/export", ExportAssignmentDuration);
        group.MapGet("/assignment-start/export", ExportAssignmentStart);
        group.MapGet("/sow-extension/export", ExportSowExtension);

        return group;
    }

    private static async Task<Ok<AvailabilityReportDto>> GetAvailability(
        ICompassReportReadService readService,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await readService.GetAvailabilityAsync(cancellationToken));

    private static async Task<Ok<IReadOnlyList<AssignmentDurationRowDto>>> GetAssignmentDuration(
        ICompassReportReadService readService,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await readService.GetAssignmentDurationAsync(cancellationToken));

    /// <summary>The Assignment Start lookup (AC-40) — every assignment starting in the inclusive range.</summary>
    /// <remarks>
    /// <c>from &gt; to</c> is a 400 with a message, never a silent swap and never an empty list: an
    /// empty list is indistinguishable from "nothing started in that range", a different and wrong
    /// answer. The guard is <c>&gt;</c>, not <c>&gt;=</c> — a single-day lookup
    /// where <c>from == to</c> is legitimate, and rejecting it is the most likely off-by-one here.
    /// Both parameters are non-nullable <see cref="DateOnly"/>, so a missing or unparseable one is a
    /// binding failure, which answers 400 only because <c>RouteHandlerOptions.ThrowOnBadRequest</c> is
    /// pinned false application-wide; on its framework default it is true under Development, turning
    /// the same request into an unhandled <c>BadHttpRequestException</c> and a 500 locally.
    /// </remarks>
    private static async Task<Results<Ok<IReadOnlyList<AssignmentStartRowDto>>, ValidationProblem>>
        GetAssignmentStart(
            DateOnly from,
            DateOnly to,
            ICompassReportReadService readService,
            CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["from"] = ["The start of the range must be on or before its end."],
            });
        }

        return TypedResults.Ok(await readService.GetAssignmentStartsAsync(from, to, cancellationToken));
    }

    /// <summary>
    /// The SOW Extension Report — every extension SOW starting in the inclusive range.
    /// </summary>
    /// <remarks>
    /// The inverted-range guard is duplicated from <see cref="GetAssignmentStart"/> rather than shared,
    /// for the same reason its export twin duplicates it: an out-of-band helper shared by four call
    /// sites is a smaller win than keeping each handler's guard visible beside its own route.
    /// </remarks>
    private static async Task<Results<Ok<IReadOnlyList<SowExtensionRowDto>>, ValidationProblem>>
        GetSowExtension(
            DateOnly from,
            DateOnly to,
            ICompassReportReadService readService,
            CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["from"] = ["The start of the range must be on or before its end."],
            });
        }

        return TypedResults.Ok(await readService.GetSowExtensionsAsync(from, to, cancellationToken));
    }


    /// <summary>
    /// Exports the Availability Report — one section as CSV, or all three zipped when no section is
    /// named.
    /// </summary>
    /// <remarks>
    /// The three sections do not share a column set, and <c>AvailabilityReportDto</c>'s remarks record
    /// that a uniform row shape invents a column no acceptance criterion covers, so a merged CSV would
    /// have to drop or fabricate columns and "everything" is a zip instead. An unknown section is a
    /// 400, never a silently empty file, which would be indistinguishable from "that section has no
    /// rows". The parameter is a string parsed here rather than an enum bound by the framework:
    /// minimal-API enum binding matches member names, so it accepts <c>ConfirmedRollouts</c> and
    /// rejects <c>confirmed-rollouts</c>. The kebab-case slug also names the downloaded file, and
    /// <see cref="AvailabilitySectionSlug"/> holds the single table both read.
    /// </remarks>
    private static async Task<IResult> ExportAvailability(
        string? section,
        ICompassReportExportService exportService,
        CancellationToken cancellationToken)
    {
        if (section is null)
        {
            return Download(await exportService.ExportAvailabilityArchiveAsync(cancellationToken));
        }

        if (!AvailabilitySectionSlug.TryParse(section, out var parsed))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["section"] =
                [
                    "Unknown report section. Expected one of: currently-available, "
                    + "confirmed-rollouts, unconfirmed-sows.",
                ],
            });
        }

        return Download(await exportService.ExportAvailabilitySectionAsync(parsed, cancellationToken));
    }

    private static async Task<IResult> ExportAssignmentDuration(
        ICompassReportExportService exportService,
        CancellationToken cancellationToken) =>
        Download(await exportService.ExportAssignmentDurationAsync(cancellationToken));

    /// <summary>Exports the Assignment Start lookup for the same inclusive range the screen uses.</summary>
    /// <remarks>
    /// The inverted-range guard is repeated rather than shared, because the two handlers return
    /// different types. Duplicating three lines is the cheaper of the two risks: an export route that
    /// answered 200 with an empty file where the JSON route answers 400 would be the more permissive
    /// door. <c>ExportAssignmentStart_InvertedRange_Returns400</c> holds it.
    /// </remarks>
    private static async Task<IResult> ExportAssignmentStart(
        DateOnly from,
        DateOnly to,
        ICompassReportExportService exportService,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["from"] = ["The start of the range must be on or before its end."],
            });
        }

        return Download(await exportService.ExportAssignmentStartAsync(from, to, cancellationToken));
    }

    /// <summary>Exports the SOW Extension Report for the same inclusive range the screen uses.</summary>
    /// <remarks>
    /// The inverted-range guard is repeated rather than shared, for the same reason
    /// <see cref="ExportAssignmentStart"/> repeats it: the two handlers return different types, and an
    /// export answering 200 with an empty file where the JSON route answers 400 would be the more
    /// permissive door.
    /// </remarks>
    private static async Task<IResult> ExportSowExtension(
        DateOnly from,
        DateOnly to,
        ICompassReportExportService exportService,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["from"] = ["The start of the range must be on or before its end."],
            });
        }

        return Download(await exportService.ExportSowExtensionAsync(from, to, cancellationToken));
    }

    /// <summary>
    /// Sends a rendered export as a download.
    /// </summary>
    /// <remarks>
    /// <c>fileDownloadName</c> is what sets <c>Content-Disposition: attachment</c>, so the browser
    /// saves the file instead of rendering the CSV as text in a tab.
    /// </remarks>
    private static IResult Download(CompassExportFile file) =>
        Results.File(file.Content, file.ContentType, file.FileName);
}
