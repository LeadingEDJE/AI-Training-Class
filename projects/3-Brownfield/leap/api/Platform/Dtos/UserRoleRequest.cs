namespace LeadingEDJE.Leap.Api.Platform.Dtos;

/// <summary>
/// Request to assign or remove an authorization role for a user.
/// </summary>
public class UserRoleRequest
{
    /// <summary>EDJE identity GUID of the user whose role is being changed.</summary>
    public Guid EdjeId { get; set; }

    /// <summary>Role name to grant or revoke (e.g., "Admin", "Manager", "Payroll").</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Operator-supplied justification for the change, recorded in the audit log.</summary>
    public string Reason { get; set; } = string.Empty;
}
