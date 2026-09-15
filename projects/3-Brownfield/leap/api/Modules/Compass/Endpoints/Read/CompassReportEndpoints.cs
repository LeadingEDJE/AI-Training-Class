using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Read;

/// <summary>The Compass report read surface — AC-18 to AC-19.</summary>
/// <remarks>
/// Every route requires <see cref="RolePolicy.CompassReporting"/>, which Compass Admin also satisfies.
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
    /// named. An unknown section falls back to the zip rather than erroring.
    /// </summary>
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

    private static IResult Download(CompassExportFile file) =>
        Results.File(file.Content, file.ContentType, file.FileName);
}
