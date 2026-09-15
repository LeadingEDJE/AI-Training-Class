namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// A client-scoped billable time category. ERD table <c>compass.billable_time_category</c>.
/// </summary>
public class BillableTimeCategory
{
    /// <summary>Primary key. Mapped to the ERD column <c>billable_time_category_id</c>.</summary>
    public int Id { get; set; }

    /// <summary>Required FK to the owning <see cref="Client"/>.</summary>
    public int ClientId { get; set; }

    /// <summary>Category name. Unique PER CLIENT, not globally.</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The identifier this record carried in the legacy TPS directory, or <c>null</c> when it was
    /// created in Compass.
    /// </summary>
    public string? LegacyTpsId { get; set; }

    /// <summary>The owning client.</summary>
    public Client? Client { get; set; }
}
