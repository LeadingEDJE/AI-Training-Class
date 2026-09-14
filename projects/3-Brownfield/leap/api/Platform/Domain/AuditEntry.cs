namespace LeadingEDJE.Leap.Api.Platform.Domain;

/// <summary>
/// Immutable record capturing a single auditable change before it is persisted to the audit log.
/// </summary>
/// <remarks>
/// <c>EffectiveRoles</c> defaults to <c>null</c>, meaning not captured — the correct value for the
/// Timesheet and OOTO call sites, since an empty set would instead claim the actor held no roles.
/// Supply it from the resolved session, never from current group membership. A Compass write supplies
/// <c>[.. currentUser.Privileges]</c> unfiltered: do not narrow it to <c>Compass </c>-prefixed roles,
/// because the record is platform-owned and holds the authority the actor actually carried. See
/// <see cref="AuditLog.EffectiveRoles"/> for the full contract and the query caveat it creates.
/// </remarks>
public record AuditEntry(
    string EntityType,
    string EntityId,
    string Action,
    string Actor,
    string TriggeredBy,
    string Reason,
    List<FieldChange> Changes,
    List<string>? EffectiveRoles = null);

/// <summary>
/// Represents a single field-level change within an audit entry, capturing before and after values.
/// </summary>
public record FieldChange(string Field, string? Before, string? After);
