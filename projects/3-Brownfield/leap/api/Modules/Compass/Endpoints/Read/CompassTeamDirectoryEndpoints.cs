using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Read;

/// <summary>The Team Directory and employee detail read surfaces — AC-5 to AC-11.</summary>
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

    private static async Task<Results<Ok<EmployeeDetailDto>, NotFound>> GetEmployeeDetail(
        int id,
        ICompassDirectoryReadService readService,
        CancellationToken cancellationToken)
    {
        EmployeeDetailDto? detail = await readService.GetEmployeeDetailAsync(id, cancellationToken);

        return detail is not null ? TypedResults.Ok(detail) : TypedResults.NotFound();
    }

    private static DirectoryStatusFilter ParseStatus(string? status) =>
        Enum.TryParse<DirectoryStatusFilter>(status, ignoreCase: true, out var parsed)
            ? parsed
            : DirectoryStatusFilter.Active;
}
