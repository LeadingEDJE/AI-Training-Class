namespace LeadingEDJE.Leap.Api.Platform.Domain;

/// <summary>
/// A person row in the identity system-of-record (<c>public.people</c>) — the sole surviving directory
/// table after the Timesheet/Ooto module retirement. Every authenticated session, bootstrap SuperAdmin,
/// and impersonation target resolves through this table.
/// </summary>
/// <remarks>
/// Not <see cref="AuditableEntity"/>: <c>people</c> predates that convention and carries no
/// <c>created_at</c>/<c>updated_at</c>/<c>created_by</c>/<c>updated_by</c> columns.
/// </remarks>
public class Person
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The person's EDJE identity — the primary key carried in the session's <c>EdjeId</c> claim.
    /// Nullable because a row can exist before it is minted; <see cref="Services.PersonProvisioningService"/>
    /// mints it eagerly from <see cref="Id"/> for every row it creates.
    /// </summary>
    public Guid? EdjeId { get; set; }

    /// <summary>Email address. Unique case-insensitively via the <c>ix_people_email_lower</c> functional index.</summary>
    public string? Email { get; set; }

    /// <summary>Given name.</summary>
    public string? FirstName { get; set; }

    /// <summary>Family name.</summary>
    public string? LastName { get; set; }

    /// <summary>Whether the person may sign in. Deactivation is permanent — never reversed by provisioning.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Provenance of an app-minted row (see <see cref="Services.PersonSource"/>), or <c>null</c> for an
    /// HR-authoritative row the application did not create itself.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>Job title text, carried over from the absorbed directory. Not authorization-relevant.</summary>
    public string? Title { get; set; }
}
