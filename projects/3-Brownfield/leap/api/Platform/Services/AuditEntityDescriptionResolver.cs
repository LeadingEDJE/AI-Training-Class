using LeadingEDJE.Leap.Api.Platform.Data;
using LeadingEDJE.Leap.Api.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Turns an audit row's raw <see cref="AuditLog.EntityId"/> into a human-readable label.
/// </summary>
/// <remarks>
/// Every entity type the application audits is mapped here. An unmapped type, or a target row that has
/// since been deleted, resolves to nothing and the UI falls back to the raw id.
/// Resolution is batched: one query per entity type present on the page plus one shared query for
/// display names, regardless of row count. Names are read straight from <see cref="LeapDbContext"/>
/// rather than through a directory service scoped to active people, because an audit trail must still
/// describe someone who has left.
/// </remarks>
public class AuditEntityDescriptionResolver(LeapDbContext db)
{
    private const string EmployeeAttributeType = "EmployeeAttribute";
    private const string ImpersonateType = "Impersonate";
    private const string PersonType = "Person";
    private const string UserRoleType = "UserRole";
    private const string SystemSettingType = "SystemSetting";

    /// <summary>Composite key for the returned map — an entity is identified by type plus id.</summary>
    public static string Key(string entityType, string entityId) => string.Concat(entityType, "|", entityId);

    /// <summary>
    /// Resolves friendly descriptions for the given audit rows. Keys are produced by
    /// <see cref="Key"/>; a row whose entity cannot be described is simply absent from the map.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(IReadOnlyList<AuditLog> logs)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        if (logs.Count == 0)
        {
            return descriptions;
        }

        var idsByType = GroupIdsByType(logs);

        // Pass 1 — resolve non-person entities and note which people they point at.
        var personKeys = new HashSet<Guid>();
        var settings = await LoadSettingDescriptionsAsync(IdsFor(idsByType, SystemSettingType));

        // Types whose id IS the person key need no lookup of their own.
        CollectGuids(IdsFor(idsByType, EmployeeAttributeType), personKeys);
        CollectGuids(IdsFor(idsByType, ImpersonateType), personKeys);
        CollectGuids(IdsFor(idsByType, PersonType), personKeys);
        CollectGuids(IdsFor(idsByType, UserRoleType).Select(SplitUserRoleId).Select(p => p.EdjeId), personKeys);

        // Pass 2 — one batched name lookup for everything gathered above.
        var names = await LoadDisplayNamesAsync(personKeys);

        // Pass 3 — compose.
        foreach (var (entityType, entityIds) in idsByType)
        {
            foreach (var entityId in entityIds)
            {
                var description = Describe(entityType, entityId, names, settings);

                if (!string.IsNullOrWhiteSpace(description))
                {
                    descriptions[Key(entityType, entityId)] = description;
                }
            }
        }

        return descriptions;
    }

    private static string? Describe(
        string entityType,
        string entityId,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<string, string> settings)
    {
        switch (entityType)
        {
            case EmployeeAttributeType:
            case ImpersonateType:
            case PersonType:
                return ParseGuid(entityId) is Guid personKey ? NameOrNull(names, personKey) : null;

            case UserRoleType:
                var (edjeId, role) = SplitUserRoleId(entityId);
                return Join(edjeId is Guid roleHolder ? NameOrNull(names, roleHolder) : null, role);

            case SystemSettingType:
                return settings.TryGetValue(entityId, out var settingDescription) ? settingDescription : null;

            default:
                return null;
        }
    }

    private async Task<Dictionary<string, string>> LoadSettingDescriptionsAsync(IEnumerable<string> entityIds)
    {
        var keys = entityIds.ToArray();
        if (keys.Length == 0)
        {
            return [];
        }

        var rows = await db.SystemSettings.AsNoTracking()
            .Where(s => keys.Contains(s.Key) && s.Description != "")
            .Select(s => new { s.Key, s.Description })
            .ToListAsync();

        return rows.ToDictionary(r => r.Key, r => r.Description, StringComparer.Ordinal);
    }

    // Display names are indexed under every identifier an audit row might reference for the same
    // human: Person.Id and Person.EdjeId.
    private async Task<Dictionary<Guid, string>> LoadDisplayNamesAsync(HashSet<Guid> personKeys)
    {
        var names = new Dictionary<Guid, string>();
        if (personKeys.Count == 0)
        {
            return names;
        }

        var keys = personKeys.ToArray();
        var nullableKeys = personKeys.Select(k => (Guid?)k).ToArray();

        var people = await db.People.AsNoTracking()
            .Where(p => keys.Contains(p.Id) || nullableKeys.Contains(p.EdjeId))
            .Select(p => new { p.Id, p.EdjeId, p.FirstName, p.LastName })
            .ToListAsync();

        foreach (var person in people)
        {
            var name = FormatName(person.FirstName, person.LastName);
            if (name is null)
            {
                continue;
            }

            names[person.Id] = name;
            if (person.EdjeId.HasValue)
            {
                names[person.EdjeId.Value] = name;
            }
        }

        return names;
    }

    private static Dictionary<string, List<string>> GroupIdsByType(IReadOnlyList<AuditLog> logs)
    {
        var idsByType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var log in logs)
        {
            if (!idsByType.TryGetValue(log.EntityType, out var ids))
            {
                ids = [];
                idsByType[log.EntityType] = ids;
            }

            if (!ids.Contains(log.EntityId, StringComparer.Ordinal))
            {
                ids.Add(log.EntityId);
            }
        }

        return idsByType;
    }

    private static IEnumerable<string> IdsFor(Dictionary<string, List<string>> idsByType, string entityType)
        => idsByType.TryGetValue(entityType, out var ids) ? ids : [];

    private static (Guid? EdjeId, string Role) SplitUserRoleId(string entityId)
    {
        var separator = entityId.IndexOf(':', StringComparison.Ordinal);
        return separator < 0
            ? (null, string.Empty)
            : (ParseGuid(entityId[..separator]), entityId[(separator + 1)..]);
    }

    private static void CollectGuids(IEnumerable<string> entityIds, HashSet<Guid> personKeys)
    {
        foreach (var entityId in entityIds)
        {
            Remember(personKeys, ParseGuid(entityId));
        }
    }

    private static void CollectGuids(IEnumerable<Guid?> candidates, HashSet<Guid> personKeys)
    {
        foreach (var candidate in candidates)
        {
            Remember(personKeys, candidate);
        }
    }

    private static void Remember(HashSet<Guid> personKeys, Guid? candidate)
    {
        if (candidate is Guid value && value != Guid.Empty)
        {
            personKeys.Add(value);
        }
    }

    private static string? NameOrNull(IReadOnlyDictionary<Guid, string> names, Guid key)
        => names.TryGetValue(key, out var name) ? name : null;

    private static string? FormatName(string? firstName, string? lastName)
    {
        var name = string.Join(' ', new[] { firstName, lastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    // Renders "{left} — {right}", degrading to whichever half resolved. Both missing yields null so
    // the caller falls back to the raw entity id.
    private static string? Join(string? left, string? right) => Combine(left, right, " — ");

    private static string? Combine(string? left, string? right, string separator)
    {
        var hasLeft = !string.IsNullOrWhiteSpace(left);
        var hasRight = !string.IsNullOrWhiteSpace(right);

        if (hasLeft && hasRight)
        {
            return string.Concat(left, separator, right);
        }

        if (hasLeft)
        {
            return left;
        }

        return hasRight ? Capitalize(right!) : null;
    }

    // A lone right-hand fragment becomes the whole label, so it needs to read as a sentence
    // ("week ending Jul 25, 2026" -> "Week ending Jul 25, 2026").
    private static string Capitalize(string text)
        => char.IsLower(text[0]) ? string.Concat(char.ToUpperInvariant(text[0]), text[1..]) : text;

    private static Guid? ParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) ? parsed : null;
}
