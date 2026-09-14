using LeadingEDJE.Leap.Api.Platform.Domain;
namespace LeadingEDJE.Leap.Api.Platform.Dtos;

/// <summary>
/// API response representation of a single audit log entry with resolved actor display name and,
/// where the target entity could be resolved, a friendly description of it.
/// </summary>
/// <param name="Id">Surrogate primary key of the audit row.</param>
/// <param name="EntityType">Short name of the audited entity type (e.g. "Timesheet").</param>
/// <param name="EntityId">Raw string form of the audited entity's key.</param>
/// <param name="EntityDescription">
/// Human-readable label for the audited entity (e.g. "Avery Quinn — week ending Jul 25, 2026").
/// Null when the entity type is unmapped or the target row no longer exists, in which case clients
/// fall back to <paramref name="EntityId"/>.
/// </param>
/// <param name="Action">Verb describing the mutation.</param>
/// <param name="Actor">Raw stored actor identifier.</param>
/// <param name="ActorName">Actor resolved to a display name, falling back to the raw identifier.</param>
/// <param name="TriggeredBy">Trigger source for the action.</param>
/// <param name="Reason">Human-readable justification, empty when not applicable.</param>
/// <param name="Changes">Serialized field-level diff.</param>
/// <param name="Timestamp">UTC timestamp the audit row was written.</param>
/// <param name="EffectiveRoles">
/// Roles the actor held at write time — the authority the change was made under, which is the only
/// thing distinguishing two users whose capability is identical. Three distinct values: null
/// means NOT CAPTURED (a Timesheet or OOTO write, or a row predating the column), an empty list means
/// captured with no roles held, and a populated list carries the roles. A client MUST NOT treat null
/// and empty as the same thing. See <see cref="AuditLog.EffectiveRoles"/>.
/// </param>
public record AuditLogResponse(
    long Id,
    string EntityType,
    string EntityId,
    string? EntityDescription,
    string Action,
    string Actor,
    string ActorName,
    string TriggeredBy,
    string Reason,
    string Changes,
    DateTime Timestamp,
    IReadOnlyList<string>? EffectiveRoles);

/// <summary>
/// Paginated wrapper for audit log query results.
/// </summary>
public record PaginatedAuditLogResponse(
    IReadOnlyList<AuditLogResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// Extension methods to map AuditLog entities to API response DTOs.
/// </summary>
public static class AuditLogMappings
{
    /// <summary>
    /// Maps an <see cref="AuditLog"/> entity to an <see cref="AuditLogResponse"/> with a pre-resolved
    /// actor display name and an optional pre-resolved entity description.
    /// </summary>
    public static AuditLogResponse ToResponse(this AuditLog auditLog, string actorName, string? entityDescription = null) =>
        new(
            auditLog.Id,
            auditLog.EntityType,
            auditLog.EntityId,
            entityDescription,
            auditLog.Action,
            auditLog.Actor,
            actorName,
            auditLog.TriggeredBy,
            auditLog.Reason,
            auditLog.Changes,
            auditLog.Timestamp,
            // Deliberately nullable all the way out to the client: null means the authority was NOT
            // CAPTURED (a Timesheet or OOTO write, or a row predating the column), which is not the
            // same as an empty array meaning the actor held no roles. Flattening either into the
            // other here would erase the distinction the column exists to carry.
            auditLog.EffectiveRoles);
}
