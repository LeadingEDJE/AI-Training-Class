namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// A client-scoped billable time category. ERD table <c>compass.billable_time_category</c>.
/// </summary>
/// <remarks>
/// Zero-to-many per client, and consumed by the timesheet system. Audited as part of the client
/// record rather than on its own.
/// <para>
/// Not the same thing as the timesheet module's <c>TimeCategory</c> (<c>public.time_categories</c>),
/// despite the similar name: that one is a global lookup, this one belongs to a single client. Whether
/// they should converge is a real question for the directory move (ADR-006) and is out of scope here.
/// </para>
/// </remarks>
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
    /// <remarks>
    /// Permanent provenance: it answers "what did this record come from?" after the migration tool
    /// and its crosswalk file are gone, and lets a lost crosswalk be rebuilt by reading it back from
    /// the target. Unique when present, and null for everything a person creates — Postgres unique
    /// indexes are NULLS DISTINCT, so that uniqueness costs a hand-created record nothing. Only a
    /// migration run may set it.
    /// </remarks>
    public string? LegacyTpsId { get; set; }

    /// <summary>The owning client.</summary>
    public Client? Client { get; set; }
}
