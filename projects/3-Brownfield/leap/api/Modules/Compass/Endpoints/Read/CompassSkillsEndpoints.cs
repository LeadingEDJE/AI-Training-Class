using LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;
using LeadingEDJE.Leap.Api.Modules.Compass.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Read;

/// <summary>The active skill list — backs the Team Directory filter and EDJEr tagging pickers.</summary>
public static class CompassSkillsEndpoints
{
    /// <summary>Maps <c>/api/compass/skills</c>.</summary>
    public static RouteGroupBuilder MapCompassSkillsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/compass/skills")
            .WithTags("Compass Read")
            .RequireAuthorization();

        group.MapGet("/", GetActiveSkills);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<SkillOptionDto>>> GetActiveSkills(
        ICompassLookupService lookups,
        CancellationToken cancellationToken)
    {
        var skills = await lookups.GetSkillsAsync(activeOnly: true, cancellationToken);

        return TypedResults.Ok<IReadOnlyList<SkillOptionDto>>(
            [.. skills.Select(s => new SkillOptionDto { Id = s.Id, Name = s.Name })]);
    }
}
