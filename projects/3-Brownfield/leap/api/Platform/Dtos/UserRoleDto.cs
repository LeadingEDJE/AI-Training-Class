using LeadingEDJE.Leap.Api.Platform.Domain;
namespace LeadingEDJE.Leap.Api.Platform.Dtos;

/// <summary>
/// API response representation of a user role assignment with resolved employee name.
/// </summary>
public class UserRoleDto
{
    /// <summary>Role-grant primary key.</summary>
    public int Id { get; set; }

    /// <summary>EDJE identity GUID of the user receiving the grant.</summary>
    public Guid EdjeId { get; set; }

    /// <summary>Resolved display name of the employee — populated from the TPS cache at response time.</summary>
    public string? EmployeeName { get; set; }

    /// <summary>Role name — see <see cref="UserRole.Role"/>.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the role was granted.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Identifier of the user or actor who granted the role.</summary>
    public string? CreatedBy { get; set; }
}
