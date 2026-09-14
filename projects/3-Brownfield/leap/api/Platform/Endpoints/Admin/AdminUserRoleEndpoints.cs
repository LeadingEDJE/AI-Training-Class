using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LeadingEDJE.Leap.Api.Platform.Endpoints.Admin;

/// <summary>
/// Admin endpoints for managing user authorization role assignments.
/// </summary>
public static class AdminUserRoleEndpoints
{
    /// <summary>
    /// Maps user-role admin endpoints under <c>/api/admin/user-roles</c> with SuperAdmin authorization
    /// for listing, assigning, and removing authorization roles.
    /// </summary>
    public static RouteGroupBuilder MapAdminUserRoleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/user-roles")
            .WithTags("AdminUserRoles")
            .RequireAuthorization(RolePolicy.SuperAdmin);

        group.MapGet("/", GetAllUserRoles);
        group.MapGet("/by-employee/{edjeId:guid}", GetRolesByEmployee);
        group.MapGet("/by-role/{role}", GetUsersByRole);
        group.MapPost("/", AssignRole);
        group.MapDelete("/{id:int}", RemoveRole);

        return group;
    }

    /// <summary>
    /// Builds an EdjeId → employee-name lookup from the directory for resolving
    /// <see cref="UserRoleDto.EmployeeName"/>. Employees without an EdjeId are skipped.
    /// </summary>
    private static Dictionary<string, string> BuildEmployeeNameLookup(
        IReadOnlyList<EmployeeDirectoryEntry> employees)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var employee in employees)
        {
            if (employee.EdjeId is Guid edjeId)
            {
                lookup[edjeId.ToString()] = employee.Name;
            }
        }
        return lookup;
    }

    private static async Task<Ok<IEnumerable<UserRoleDto>>> GetAllUserRoles(
        IUserRoleRepository roleRepository,
        IEmployeeDirectory employeeDirectory)
    {
        var roles = await roleRepository.GetAllAsync();
        var lookup = BuildEmployeeNameLookup(await employeeDirectory.GetAllEmployeesAsync());

        var dtos = roles.Select(r => new UserRoleDto
        {
            Id = r.Id,
            EdjeId = r.EdjeId,
            EmployeeName = lookup.TryGetValue(r.EdjeId.ToString(), out var name) ? name : null,
            Role = r.Role,
            CreatedAt = r.CreatedAt,
            CreatedBy = r.CreatedBy
        });
        return TypedResults.Ok(dtos);
    }

    private static async Task<Ok<IReadOnlyList<string>>> GetRolesByEmployee(
        Guid edjeId,
        IUserRoleService roleService)
    {
        var roles = await roleService.GetRolesAsync(edjeId);
        return TypedResults.Ok(roles);
    }

    private static async Task<Ok<IEnumerable<UserRoleDto>>> GetUsersByRole(
        string role,
        IUserRoleService roleService,
        IEmployeeDirectory employeeDirectory)
    {
        var users = await roleService.GetUsersByRoleAsync(role);
        var lookup = BuildEmployeeNameLookup(await employeeDirectory.GetAllEmployeesAsync());

        var dtos = users.Select(u => new UserRoleDto
        {
            Id = u.Id,
            EdjeId = u.EdjeId,
            EmployeeName = lookup.TryGetValue(u.EdjeId.ToString(), out var name) ? name : null,
            Role = u.Role,
            CreatedAt = u.CreatedAt,
            CreatedBy = u.CreatedBy
        });
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<Created<UserRoleRequest>, BadRequest<object>>> AssignRole(
        UserRoleRequest request,
        IUserRoleService roleService,
        ICurrentUserContext currentUser)
    {
        try
        {
            await roleService.AssignRoleAsync(
                request.EdjeId,
                request.Role,
                currentUser.EdjeId.ToString(),
                request.Reason);
            return TypedResults.Created($"/api/admin/user-roles/by-employee/{request.EdjeId}", request);
        }
        catch (InvalidOperationException ex)
        {
            // A Compass role, refused here for the same reason RemoveRole refuses self-removal:
            // this endpoint is Timesheet-owned, and Compass authority must derive from Google
            // Compass groups alone (FR-010, FR-014) -- never from a Timesheet-gated admin grant.
            return TypedResults.BadRequest<object>(new { error = ex.Message });
        }
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<object>>> RemoveRole(
        int id,
        IUserRoleRepository roleRepository,
        IUserRoleService roleService,
        ICurrentUserContext currentUser)
    {
        var allRoles = await roleRepository.GetAllAsync();
        var role = allRoles.FirstOrDefault(r => r.Id == id);
        if (role is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            await roleService.RemoveRoleAsync(
                role.EdjeId,
                role.Role,
                currentUser.EdjeId.ToString(),
                "Removed via admin endpoint");
            return TypedResults.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest<object>(new { error = ex.Message });
        }
    }
}
