using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Platform.Endpoints;

/// <summary>
/// Endpoints for browsing the audit trail with filtering, pagination, and entity-specific queries.
/// </summary>
public static class AuditLogEndpoints
{
    /// <summary>
    /// Maps audit-log browsing endpoints under <c>/api/audit-logs</c>: the admin group (HR/SuperAdmin)
    /// for full-history browsing plus an EDJEr-accessible entity group for inline audit panels.
    /// </summary>
    public static RouteGroupBuilder MapAuditLogEndpoints(this WebApplication app)
    {
        // Admin endpoints (HR + SuperAdmin) -- full audit log access
        var group = app.MapGroup("/api/audit-logs")
            .WithTags("AuditLogs")
            .RequireAuthorization(RolePolicy.HROrSuperAdmin);

        group.MapGet("/", GetByEntity);
        group.MapGet("/browse", Browse);
        group.MapGet("/entity-types", GetEntityTypes);

        // EDJEr-accessible endpoint for own entity audit trail (inline audit panels)
        var entityGroup = app.MapGroup("/api/audit-logs")
            .WithTags("AuditLogs")
            .RequireAuthorization(RolePolicy.EDJEr);

        entityGroup.MapGet("/entity", GetEntityAuditTrail);

        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<AuditLogResponse>>, BadRequest<string>>> GetByEntity(
        string? entityType,
        string? entityId,
        IAuditService service)
    {
        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
        {
            return TypedResults.BadRequest("Both entityType and entityId query parameters are required.");
        }

        var logs = await service.GetByEntityAsync(entityType, entityId);
        return TypedResults.Ok(logs);
    }

    private static async Task<Ok<PaginatedAuditLogResponse>> Browse(
        string? entityType,
        string? actor,
        string? employeeId,
        DateTime? fromDate,
        DateTime? toDate,
        int page = 1,
        int pageSize = 20,
        IAuditService service = default!)
    {
        var result = await service.BrowseAsync(entityType, actor, employeeId, fromDate, toDate, page, pageSize);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<IReadOnlyList<string>>> GetEntityTypes(
        IAuditService service)
    {
        var types = await service.GetDistinctEntityTypesAsync();
        return TypedResults.Ok(types);
    }

    private static async Task<Results<Ok<IReadOnlyList<AuditLogResponse>>, BadRequest<string>, ForbidHttpResult>> GetEntityAuditTrail(
        string? entityType,
        string? entityId,
        IAuditService service,
        ICurrentUserContext currentUser,
        IAuditTrailAccessPolicy accessPolicy)
    {
        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
        {
            return TypedResults.BadRequest("Both entityType and entityId query parameters are required.");
        }

        // Admin/HR/SuperAdmin bypass ownership check
        bool isAdmin = currentUser.HasPrivilege(RolePolicy.SuperAdmin)
                    || currentUser.HasPrivilege(RolePolicy.HR)
                    || currentUser.HasPrivilege(RolePolicy.Admin);

        // HasRule before currentUser.TpsEmployeeId, because reading that property blocks on a
        // directory lookup worth two or three queries. An unregistered entity type must not pay it.
        // Two rarer paths still do -- an unparseable entityId and one matching no timesheet -- since
        // the rule owns both the parse and the lookup. That residue is accepted.
        if (!isAdmin && accessPolicy.HasRule(entityType))
        {
            var outcome = await accessPolicy.EvaluateAsync(
                entityType,
                new AuditTrailAccessRequest(entityId, currentUser.TpsEmployeeId));

            // Not a switch expression: three arms over this enum is not exhaustive, so CS8524,
            // which is an error here. A `_ => throw` arm compiles but is unreachable.
            if (outcome.Result == AuditTrailAccess.Refused)
            {
                return TypedResults.Forbid();
            }

            if (outcome.Result == AuditTrailAccess.Invalid)
            {
                // The ?? is the one place Platform composes rather than relays, guarding a
                // message the static factories make non-null. BadRequest(null) would write no body
                // and let UseStatusCodePages substitute application/problem+json, changing the
                // content type.
                return TypedResults.BadRequest(
                    outcome.Message ?? $"Invalid entityId for {entityType}.");
            }
        }

        // The original strings, never a parsed id: AuditLogRepository filters the raw entityId, so
        // `007` and `7` are different trails.
        var logs = await service.GetByEntityAsync(entityType, entityId);
        return TypedResults.Ok(logs);
    }
}
