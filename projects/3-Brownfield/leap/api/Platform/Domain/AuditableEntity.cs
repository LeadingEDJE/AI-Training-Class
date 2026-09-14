namespace LeadingEDJE.Leap.Api.Platform.Domain;

/// <summary>
/// Base entity with automatic audit timestamps and actor tracking for all persisted domain objects.
/// </summary>
public abstract class AuditableEntity
{
    /// <summary>UTC timestamp when the row was first inserted. Populated by the service layer on create.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC timestamp of the most recent update. Refreshed by the service layer on every mutation.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Identifier (user id or system actor) that created the row. Null when seeded or created before audit tracking.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Identifier (user id or system actor) that last updated the row. Null when the row has never been mutated after seed.</summary>
    public string? UpdatedBy { get; set; }
}
