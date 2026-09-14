using LeadingEDJE.Leap.Api.Platform.Auth;

namespace LeadingEDJE.Leap.Api.Platform.Authorization;

/// <summary>
/// Validates configured Google-group-to-role mappings against the known-role vocabulary at startup.
/// </summary>
/// <remarks>
/// Fails fast on purpose. A mapping whose role string is not a known role is a configuration typo
/// whose consequence is silent — the group grants nothing and nobody finds out until a user reports
/// missing access — and a near-miss can mangle a grant into a different module's role, so this
/// throws before the app serves traffic. The second check is the one that fails open: a group whose
/// name identifies it as a Compass group must map only to a Compass role, because mapping a Compass
/// operations group to the bare timesheet operations role grants that role in timesheet — privilege
/// escalation across module boundaries from one plausible-looking config line.
/// </remarks>
public static class GroupRoleMappingValidator
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> naming both the offending group and the
    /// offending role string when any configured mapping is invalid.
    /// </summary>
    public static void Validate(GoogleAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var mapping in options.Groups)
        {
            if (string.IsNullOrWhiteSpace(mapping.GroupName))
            {
                continue;
            }

            if (!KnownRoles.IsKnown(mapping.Role))
            {
                throw new InvalidOperationException(
                    $"Invalid GoogleAuth group mapping: group '{mapping.GroupName}' maps to role "
                    + $"'{mapping.Role}', which is not a known platform role. Known roles are: "
                    + $"{string.Join(", ", KnownRoles.All.Order(StringComparer.Ordinal))}. "
                    + "A module that adds a role must add it to KnownRoles.");
            }

            // Cross-module escalation guard. A Compass group must never carry a non-Compass role.
            var isCompassGroup = mapping.GroupName.StartsWith(
                KnownRoles.CompassGroupPrefix, StringComparison.OrdinalIgnoreCase);

            if (isCompassGroup && !KnownRoles.Compass.Contains(mapping.Role))
            {
                throw new InvalidOperationException(
                    $"Invalid GoogleAuth group mapping: Compass group '{mapping.GroupName}' maps to "
                    + $"role '{mapping.Role}', which is not a Compass role. A Compass group must "
                    + "never map to a timesheet or OOTO role — that would grant privilege in another "
                    + "module. Compass roles are: "
                    + $"{string.Join(", ", KnownRoles.Compass.Order(StringComparer.Ordinal))}.");
            }
        }
    }
}
