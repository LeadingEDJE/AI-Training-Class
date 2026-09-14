using System.Text.Json;
using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Persists audit trail entries and provides paginated browsing, resolving actor ids to display names
/// via <see cref="IEmployeeDirectory"/> and entity ids via <see cref="AuditEntityDescriptionResolver"/>.
/// </summary>
public class AuditService(IAuditLogRepository repository, LeapDbContext context, IEmployeeDirectory employeeDirectory) : IAuditService
{
    private readonly AuditEntityDescriptionResolver _descriptions = new(context);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Persists an audit entry with serialized field-level changes; requires a non-empty reason.
    /// </summary>
    /// <remarks>
    /// This method calls <c>SaveChangesAsync</c> on the shared <see cref="LeapDbContext"/>. It
    /// therefore flushes whatever the calling service already had pending, which is how an audit entry
    /// ends up committed together with the change it describes rather than in a separate transaction.
    /// A caller that needs the two to succeed or fail as one must log after staging its change
    /// and must not save in between — logging first and saving second commits the audit row for a
    /// change that can still fail.
    /// </remarks>
    public async Task LogAsync(AuditEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Reason))
        {
            throw new ArgumentException("Reason is required for audit log entries.", nameof(entry));
        }

        var auditLog = new AuditLog
        {
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Action = entry.Action,
            Actor = entry.Actor,
            TriggeredBy = entry.TriggeredBy,
            Reason = entry.Reason,
            Changes = JsonSerializer.Serialize(entry.Changes, JsonOptions),
            Timestamp = DateTime.UtcNow,
            // Copying, de-duplication and sorting all live in AuditLog's setter, so a seeder or
            // migration loader that builds the entity directly gets the same guarantees this path
            // does. `null` survives as `null` — NOT CAPTURED, which is what every Timesheet and OOTO
            // write continues to record (FR-043), and is not the empty set meaning "held no roles".
            EffectiveRoles = entry.EffectiveRoles
        };

        await repository.CreateAsync(auditLog);
        await context.SaveChangesAsync();
    }

    /// <summary>Returns all audit log rows for a single entity with actor ids and entity ids both resolved.</summary>
    public async Task<IReadOnlyList<AuditLogResponse>> GetByEntityAsync(string entityType, string entityId)
    {
        var items = await repository.GetByEntityAsync(entityType, entityId);
        var actorNames = BuildActorLookup(await employeeDirectory.GetAllEmployeesAsync());
        var entityDescriptions = await _descriptions.ResolveAsync(items);

        return ToResponses(items, actorNames, entityDescriptions);
    }

    /// <summary>Returns the distinct <c>EntityType</c> values present in the audit log (for UI filter dropdowns).</summary>
    public Task<IReadOnlyList<string>> GetDistinctEntityTypesAsync()
        => repository.GetDistinctEntityTypesAsync();

    /// <summary>
    /// Queries audit logs with optional filters, resolving actor and entity ids to friendly names.
    /// <paramref name="employeeId"/> is widened to every identifier form that person's entries may use.
    /// </summary>
    public async Task<PaginatedAuditLogResponse> BrowseAsync(
        string? entityType, string? actor, string? employeeId, DateTime? fromDate, DateTime? toDate, int page, int pageSize)
    {
        // Load the directory once and use it for both identifier widening and display names.
        var employees = await employeeDirectory.GetAllEmployeesAsync();

        IReadOnlyList<string>? actorIds = ResolveActorIds(actor, employeeId, employees);
        var (items, totalCount) = await repository.BrowseAsync(entityType, actorIds, fromDate, toDate, page, pageSize);

        var actorNames = BuildActorLookup(employees);
        var entityDescriptions = await _descriptions.ResolveAsync(items);

        return new PaginatedAuditLogResponse(
            ToResponses(items, actorNames, entityDescriptions), totalCount, page, pageSize);
    }

    private static List<AuditLogResponse> ToResponses(
        IReadOnlyList<AuditLog> items,
        Dictionary<string, string> actorNames,
        IReadOnlyDictionary<string, string> entityDescriptions)
    {
        return items.Select(a =>
        {
            var actorName = actorNames.TryGetValue(a.Actor, out var name) ? name : a.Actor;
            var key = AuditEntityDescriptionResolver.Key(a.EntityType, a.EntityId);
            entityDescriptions.TryGetValue(key, out var entityDescription);
            return a.ToResponse(actorName, entityDescription);
        }).ToList();
    }

    // The employee picker sends whichever identifier the directory surfaced (the EDJE identity when set,
    // else the employee id), but audit rows record whichever identity was on the session at log time.
    // Widen the selection to every form the same person could appear under so their history isn't split.
    // An unmatched employeeId returns an empty list, not null: null means "no actor filter" and would
    // silently return every row.
    private static IReadOnlyList<string>? ResolveActorIds(
        string? actor, string? employeeId, IReadOnlyList<EmployeeDirectoryEntry> employees)
    {
        if (!string.IsNullOrWhiteSpace(employeeId))
        {
            var employee = employees.FirstOrDefault(e =>
                string.Equals(e.Id, employeeId, StringComparison.OrdinalIgnoreCase) ||
                (e.EdjeId.HasValue &&
                 string.Equals(e.EdjeId.Value.ToString(), employeeId, StringComparison.OrdinalIgnoreCase)));

            if (employee is null)
            {
                return [];
            }

            var ids = new List<string>(2);
            if (!string.IsNullOrEmpty(employee.Id))
            {
                ids.Add(employee.Id);
            }

            if (employee.EdjeId.HasValue)
            {
                var edjeId = employee.EdjeId.Value.ToString();
                if (!ids.Contains(edjeId, StringComparer.OrdinalIgnoreCase))
                {
                    ids.Add(edjeId);
                }
            }

            return ids;
        }

        if (!string.IsNullOrWhiteSpace(actor))
        {
            return [actor];
        }

        return null;
    }

    // Actors are stored as either an employee id or an EDJE identity GUID depending on which identity
    // was available at log time. Index by both so either resolves to a display name.
    private static Dictionary<string, string> BuildActorLookup(IReadOnlyList<EmployeeDirectoryEntry> employees)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in employees)
        {
            if (!string.IsNullOrEmpty(e.Id))
            {
                lookup[e.Id] = e.Name;
            }

            if (e.EdjeId.HasValue)
            {
                lookup[e.EdjeId.Value.ToString()] = e.Name;
            }
        }

        return lookup;
    }
}
