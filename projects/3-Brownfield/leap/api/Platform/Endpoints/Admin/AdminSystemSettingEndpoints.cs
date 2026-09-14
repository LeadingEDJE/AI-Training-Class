using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Platform.Endpoints.Admin;

/// <summary>
/// Admin endpoints for managing runtime-configurable system settings.
/// </summary>
public static class AdminSystemSettingEndpoints
{
    /// <summary>
    /// Maps system-setting admin CRUD endpoints under <c>/api/admin/system-settings</c>
    /// with SuperAdmin authorization.
    /// </summary>
    public static RouteGroupBuilder MapAdminSystemSettingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/system-settings")
            .WithTags("SystemSettings")
            .RequireAuthorization(RolePolicy.SuperAdmin);

        group.MapGet("/", GetAll);
        group.MapGet("/{key}", GetByKey);
        group.MapPost("/", Create);
        group.MapPut("/{key}", Update);
        group.MapDelete("/{key}", Delete);

        return group;
    }

    private static async Task<Ok<IEnumerable<SystemSettingResponse>>> GetAll(ISystemSettingService service)
    {
        var settings = await service.GetAllAsync();
        return TypedResults.Ok(settings.Select(s => new SystemSettingResponse(s.Key, s.Value, s.Description)));
    }

    private static async Task<Results<Ok<SystemSettingResponse>, NotFound>> GetByKey(
        string key,
        ISystemSettingService service)
    {
        var setting = await service.GetByKeyAsync(key);
        return setting is not null
            ? TypedResults.Ok(new SystemSettingResponse(setting.Key, setting.Value, setting.Description))
            : TypedResults.NotFound();
    }

    private static async Task<Results<Created<SystemSettingResponse>, BadRequest<object>>> Create(
        CreateSystemSettingRequest request,
        ISystemSettingService service)
    {
        var (setting, error) = await service.CreateAsync(request.Key, request.Value, request.Description);

        if (setting == null)
        {
            return TypedResults.BadRequest<object>(new { error });
        }

        return TypedResults.Created(
            $"/api/admin/system-settings/{setting.Key}",
            new SystemSettingResponse(setting.Key, setting.Value, setting.Description));
    }

    private static async Task<Results<Ok<SystemSettingResponse>, NotFound>> Update(
        string key,
        UpdateSystemSettingRequest request,
        ISystemSettingService service)
    {
        var (setting, error) = await service.UpdateAsync(key, request.Value, request.Description);
        return setting is not null
            ? TypedResults.Ok(new SystemSettingResponse(setting.Key, setting.Value, setting.Description))
            : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(string key, ISystemSettingService service)
    {
        var (success, _) = await service.DeleteAsync(key);
        return success
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
