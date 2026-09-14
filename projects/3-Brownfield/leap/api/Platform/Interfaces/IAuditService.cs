
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>Records and retrieves audit trail entries for all entity changes across the system.</summary>
public interface IAuditService
{
    /// <summary>Persists a new audit entry. Append-only — existing entries are never updated.</summary>
    Task LogAsync(AuditEntry entry);

    /// <summary>Returns every audit entry for a single entity with actor GUIDs resolved to display names.</summary>
    Task<IReadOnlyList<AuditLogResponse>> GetByEntityAsync(string entityType, string entityId);

    /// <summary>
    /// Returns a paginated slice of audit entries, optionally filtered by entity type, actor, employee
    /// and date range. <paramref name="employeeId"/> resolves both stored id formats; <paramref name="actor"/> is exact.
    /// </summary>
    Task<PaginatedAuditLogResponse> BrowseAsync(string? entityType, string? actor, string? employeeId, DateTime? fromDate, DateTime? toDate, int page, int pageSize);

    /// <summary>Returns the distinct set of entity types present in the audit log (for populating filter dropdowns).</summary>
    Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync();
}
