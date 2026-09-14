using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Read;

/// <summary>The Team Directory and employee detail read surfaces — AC-5 to AC-11.</summary>
/// <remarks>
/// The Compass application read surface, not the published boundary (ADR-008) — hence no <c>/v1</c>.
/// The <c>…Endpoints.Read</c> namespace is what excludes these handlers from
/// <c>CompassTransportContractTests</c>; moving this file up a namespace would fail that gate,
/// correctly. Authentication only, no Compass role: AC-5 grants the directory to any authenticated
/// EDJEr. That is deliberately weaker than the boundary's <c>CompassAdmin</c> policy and not a
/// relaxation, because what a baseline viewer sees is scoped per field by the projection (BR-1)
/// rather than all-or-nothing by the gate.
/// </remarks>
public static class CompassTeamDirectoryEndpoints
{
    /// <summary>Maps <c>/api/compass/team-directory</c>.</summary>
    public static RouteGroupBuilder MapCompassTeamDirectoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/compass/team-directory")
            .WithTags("Compass Read")
            .RequireAuthorization();

        group.MapGet("/", GetTeamDirectory);
        group.MapGet("/{id:int}", GetEmployeeDetail);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<TeamDirectoryRowDto>>> GetTeamDirectory(
        ICompassDirectoryReadService readService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        string? search = null,
        string? employeeType = null,
        string? state = null,
        int? coachId = null,
        string? sort = null,
        bool desc = false,
        string? status = null)
    {
        var logger = loggerFactory.CreateLogger(typeof(CompassTeamDirectoryEndpoints));

        // Request-sourced values are sanitized before reaching a log template: newlines in a search
        // term would otherwise forge log lines (CodeQL cs/log-forging). coachId is a plain int, not
        // request-shaped text, so it does not need cleaning.
        logger.LogInformation(
            "Team Directory requested (search={Search}, employeeType={EmployeeType}, state={State}, "
                + "coachId={CoachId}, sort={Sort}, status={Status})",
            LogSanitizer.Clean(search),
            LogSanitizer.Clean(employeeType),
            LogSanitizer.Clean(state),
            coachId,
            LogSanitizer.Clean(sort),
            LogSanitizer.Clean(status));

        var query = new TeamDirectoryQuery(
            search,
            employeeType,
            state,
            coachId,
            sort,
            desc,
            ParseStatus(status));

        return TypedResults.Ok(await readService.GetTeamDirectoryAsync(query, cancellationToken));
    }

    /// <remarks>
    /// Not found, never forbidden — a 403 would confirm the record exists, and ids are
    /// sequential. Entitlement lives in the query, so both cases arrive here as the same null.
    /// </remarks>
    private static async Task<Results<Ok<EmployeeDetailDto>, NotFound>> GetEmployeeDetail(
        int id,
        ICompassDirectoryReadService readService,
        CancellationToken cancellationToken)
    {
        EmployeeDetailDto? detail = await readService.GetEmployeeDetailAsync(id, cancellationToken);

        return detail is not null ? TypedResults.Ok(detail) : TypedResults.NotFound();
    }

    /// <summary>
    /// Parses the Q2 status filter, defaulting to <see cref="DirectoryStatusFilter.Active"/>.
    /// </summary>
    /// <remarks>
    /// An unrecognised value falls back rather than failing — safe because this filter can only
    /// narrow: the entitlement clause is applied separately and unconditionally (FR-011a).
    /// </remarks>
    private static DirectoryStatusFilter ParseStatus(string? status) =>
        Enum.TryParse<DirectoryStatusFilter>(status, ignoreCase: true, out var parsed)
            ? parsed
            : DirectoryStatusFilter.Active;
}
