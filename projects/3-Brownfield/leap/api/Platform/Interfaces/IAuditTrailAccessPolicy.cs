namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// The registry the audit-trail route consults: a case-insensitive lookup from an entity type to the
/// <see cref="IAuditTrailAccessRule"/> that decides for it.
/// </summary>
/// <remarks>
/// Platform-owned and entity-agnostic — it dispatches on a string and names no module type. An
/// entity type with no registered rule is allowed, which is the registry's central semantic and
/// preserves the behaviour the route had before the rule seam existed. Changing that default is a
/// behaviour change needing its own acceptance.
/// </remarks>
public interface IAuditTrailAccessPolicy
{
    /// <summary>
    /// Whether any rule is registered for <paramref name="entityType"/>, compared case-insensitively.
    /// </summary>
    /// <remarks>
    /// Callers must use this before building an <see cref="AuditTrailAccessRequest"/>, because
    /// populating that record reads <c>ICurrentUserContext.TpsEmployeeId</c> — a blocking directory
    /// lookup worth two or three queries that also logs a warning when the caller has no active
    /// directory row. Skipping it for an unregistered type is a correctness requirement, not an
    /// optimisation: see <c>AuditLogEndpoints.GetEntityAuditTrail</c>.
    /// Implementations must be side-effect free and must not touch the database, and
    /// <c>HasRule(t) == false</c> must imply <c>EvaluateAsync(t, …)</c> would permit.
    /// </remarks>
    bool HasRule(string entityType);

    /// <summary>
    /// Asks the rule registered for <paramref name="entityType"/> to decide, or permits when none is.
    /// </summary>
    /// <remarks>
    /// The rule's message is relayed unmodified — Platform must not compose, prefix, wrap or
    /// localise it.
    /// </remarks>
    Task<AuditTrailAccessOutcome> EvaluateAsync(string entityType, AuditTrailAccessRequest request);
}
