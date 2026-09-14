
using LeadingEDJE.Leap.Api.Platform.Domain;
namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>Data access for append-only audit log entries with entity-scoped and paginated browsing queries.</summary>
public interface IAuditLogRepository : IRepository<AuditLog>
{
    /// <summary>Returns all audit entries for a single entity (e.g. all changes to <c>Timesheet:1234</c>).</summary>
    Task<IReadOnlyList<AuditLog>> GetByEntityAsync(string entityType, string entityId);

    /// <summary>
    /// Returns a paginated slice of audit entries, optionally filtered by entity type, a set of actor
    /// identifiers (OR semantics) and a date range. Used by the admin audit browser UI.
    /// </summary>
    Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> BrowseAsync(
        string? entityType, IReadOnlyList<string>? actorIds, DateTime? fromDate, DateTime? toDate, int page, int pageSize);

    /// <summary>Returns the distinct set of entity types present in the audit log (for populating filter dropdowns).</summary>
    Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync();
}
