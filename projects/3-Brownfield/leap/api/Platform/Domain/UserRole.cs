namespace LeadingEDJE.Leap.Api.Platform.Domain;

/// <summary>
/// An additive authorization role assigned to a user, keyed by EDJE identity GUID.
/// </summary>
public class UserRole : AuditableEntity
{
    /// <summary>Surrogate primary key.</summary>
    public int Id { get; set; }

    /// <summary>EDJE identity GUID of the user receiving the role grant.</summary>
    public Guid EdjeId { get; set; }

    /// <summary>Role name (e.g., "SuperAdmin", "Admin", "Manager", "Payroll") — additive on top of IdP-issued roles.</summary>
    public string Role { get; set; } = string.Empty;
}
