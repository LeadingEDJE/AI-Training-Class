namespace LeadingEDJE.Leap.Api.Platform.Domain;

/// <summary>
/// Persisted audit trail record storing who changed what entity, when, why, and the field-level diffs.
/// </summary>
public class AuditLog
{
    /// <summary>Surrogate primary key for the audit row.</summary>
    public long Id { get; set; }

    /// <summary>Fully-qualified or short name of the entity whose mutation is being logged (e.g., "Timesheet", "TimeCategory").</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>String form of the target entity's primary key, stringified to accommodate int/Guid/composite keys uniformly.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Verb describing the mutation (e.g., "Create", "Update", "Submit", "Approve", "Reject", "Delete").</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Authenticated user who initiated the change — typically the EdjeId or display name of the person making the edit.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>Trigger source for the action — distinguishes direct UI edits from background jobs, impersonation, or TPS sync.</summary>
    public string TriggeredBy { get; set; } = string.Empty;

    /// <summary>Optional human-readable justification (e.g., rejection reason, manager comment). Empty string when not applicable.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Serialized field-level diff (typically JSON) capturing before/after values — see <see cref="FieldChange"/>.</summary>
    public string Changes { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the audit record was written.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Roles the acting user held at the moment of the write — the authority behind the change, as
    /// distinct from <see cref="Actor"/> (identity) and <see cref="TriggeredBy"/> (trigger source).
    /// </summary>
    /// <remarks>
    /// Three distinct values a consumer must honour: <c>null</c> is not captured, an empty array is
    /// captured with no roles held, a populated array is captured with those roles. Only Compass
    /// writes capture today, so never assume it is populated and never infer the producing module
    /// from <c>null</c> — read <see cref="EntityType"/>. When populated it holds every role of the
    /// session, not only Compass ones, so <c>effective_roles @&gt; ARRAY['Manager']</c> answers
    /// "Compass writes by someone who also held Manager". It is a point-in-time copy, never a
    /// read-time join to current group membership. Stored as a sorted Postgres <c>text[]</c> with no
    /// column default.
    /// </remarks>
    public List<string>? EffectiveRoles
    {
        get => _effectiveRoles;
        set => _effectiveRoles = Normalize(value);
    }

    private List<string>? _effectiveRoles;

    /// <summary>
    /// Copies, case-insensitively de-duplicates, and sorts a role set for storage. <c>null</c> is
    /// preserved as <c>null</c> — it means "not captured" and must never become an empty array.
    /// </summary>
    /// <remarks>
    /// Case-insensitive on purpose. Role strings are compared case-insensitively across the platform
    /// (see <c>KnownRoles</c>), but <c>CurrentUserContext.Privileges</c> merges the <c>user_roles</c>
    /// table with IdP privilege claims into a HashSet using the default case-sensitive comparer, so
    /// "Compass Ops" and "compass ops" can both survive for one user. An ordinal sort would store the
    /// same authority two ways and let <c>effective_roles @&gt; ARRAY['Compass Super Admin']</c> miss
    /// rows whose claim casing differed. The ordinal tiebreaker keeps the surviving representative
    /// deterministic rather than input-order dependent.
    /// </remarks>
    private static List<string>? Normalize(IEnumerable<string>? roles) =>
        roles is null
            ? null
            : [.. roles
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ThenBy(role => role, StringComparer.Ordinal)
                .Distinct(StringComparer.OrdinalIgnoreCase)];
}
