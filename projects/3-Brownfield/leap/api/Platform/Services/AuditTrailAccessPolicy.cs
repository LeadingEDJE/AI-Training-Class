using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// The registry of <see cref="IAuditTrailAccessRule"/>s, keyed on entity type, compared
/// case-insensitively.
/// </summary>
/// <remarks>
/// Register this and every rule as <c>Scoped</c>. The rules read through scoped repositories, so a
/// singleton here captures them — and no gate catches that: <c>ServiceGraphResolvesTests</c> declines
/// <c>ValidateScopes</c>, and both unit test hosts register the timesheet repository as a singleton
/// double, so the mistake resolves cleanly under test and misbehaves only in production. This class
/// is stateless per request and has no performance argument for a longer lifetime.
/// </remarks>
public sealed class AuditTrailAccessPolicy : IAuditTrailAccessPolicy
{
    private readonly Dictionary<string, IAuditTrailAccessRule> _rulesByEntityType;

    /// <summary>Builds the registry over every registered rule.</summary>
    /// <param name="rules">Every rule the container can resolve.</param>
    /// <exception cref="InvalidOperationException">
    /// Two rules claim the same entity type, ignoring case. A duplicate is a programming error rather
    /// than a precedence question, so it fails loudly instead of letting one shadow the other. The
    /// throw happens at resolve time, which is what lets <c>ServiceGraphResolvesTests</c> catch a
    /// duplicate in CI rather than in a request.
    /// </exception>
    public AuditTrailAccessPolicy(IEnumerable<IAuditTrailAccessRule> rules)
    {
        _rulesByEntityType = new Dictionary<string, IAuditTrailAccessRule>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            if (!_rulesByEntityType.TryAdd(rule.EntityType, rule))
            {
                throw new InvalidOperationException(
                    $"More than one {nameof(IAuditTrailAccessRule)} is registered for entity type "
                        + $"'{rule.EntityType}'. Entity types are compared case-insensitively and each "
                        + "may have at most one rule.");
            }
        }
    }

    /// <inheritdoc/>
    public bool HasRule(string entityType) => _rulesByEntityType.ContainsKey(entityType);

    /// <inheritdoc/>
    public Task<AuditTrailAccessOutcome> EvaluateAsync(
        string entityType,
        AuditTrailAccessRequest request) =>
        _rulesByEntityType.TryGetValue(entityType, out var rule)
            ? rule.EvaluateAsync(request)
            : Task.FromResult(AuditTrailAccessOutcome.Permitted);
}
