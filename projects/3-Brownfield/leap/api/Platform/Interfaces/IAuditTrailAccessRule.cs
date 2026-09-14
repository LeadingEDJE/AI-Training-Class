namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>
/// Decides whether a caller may read one entity's audit trail, for a single entity type.
/// </summary>
/// <remarks>
/// Platform-owned so Platform depends on no module (ADR-009); the implementation lives in whichever
/// module owns the audited data. No member here names an entity type — the registry dispatches on a
/// string it never interprets. An entity type with no registered rule is allowed; see
/// <see cref="IAuditTrailAccessPolicy"/>. Nothing is cancellable, matching
/// <see cref="IEmployeeDirectory"/>: no implementation dependency here accepts a
/// <c>CancellationToken</c>.
/// </remarks>
public interface IAuditTrailAccessRule
{
    /// <summary>
    /// The entity type this rule decides for, matching the route's <c>entityType</c> parameter.
    /// </summary>
    /// <remarks>
    /// Stable, non-empty, and unique across registered rules — compared case-insensitively, so
    /// "Timesheet" and "timesheet" collide. A duplicate throws at policy construction.
    /// </remarks>
    string EntityType { get; }

    /// <summary>
    /// Decides whether the request's caller may read the trail of the entity it names.
    /// </summary>
    /// <remarks>
    /// Return <see cref="AuditTrailAccess.Refused"/> for both "not found" and "not yours": a
    /// distinguishable answer is an existence oracle, pinned by
    /// <c>AuditLogAccessControlTests.GetEntityAuditTrail_EDJEr_MissingAndUnownedTimesheets_AnswerIdentically</c>.
    /// Do not decide admin-ness — the privilege bypass runs before any rule is consulted, so a rule
    /// is never asked about an admin. Do not throw for a negative outcome; an exception becomes a
    /// ProblemDetails 500.
    /// </remarks>
    Task<AuditTrailAccessOutcome> EvaluateAsync(AuditTrailAccessRequest request);
}

/// <summary>
/// What a rule is asked about: which entity, and who is asking.
/// </summary>
/// <remarks>
/// Carrying the caller's identity here keeps a rule's dependency graph shallow, which is what makes
/// its <c>Scoped</c> lifetime an obvious choice. The entity type is absent because the registry has
/// already dispatched on it, and admin-ness because that is decided before a rule is consulted.
/// </remarks>
/// <param name="EntityId">
/// The raw query-parameter value, unparsed. Parsing is the rule's job because the format is module
/// knowledge: <c>long</c> suits a timesheet and would be wrong for a <c>Guid</c>-keyed type.
/// </param>
/// <param name="CallerTpsEmployeeId">
/// The caller's TPS employee id, never null — <c>ICurrentUserContext.TpsEmployeeId</c> falls back to
/// the caller's EdjeId rather than yielding null. Do not guard for null: the branch is unreachable
/// through the route, and the per-changed-file coverage gate would then demand a test for it.
/// </param>
public sealed record AuditTrailAccessRequest(string EntityId, string CallerTpsEmployeeId);

/// <summary>
/// A rule's decision, plus the message that accompanies a malformed request.
/// </summary>
/// <remarks>
/// Construct through the static members, not the positional constructor: they are what keep
/// <see cref="Message"/> non-null exactly when <see cref="Result"/> is
/// <see cref="AuditTrailAccess.Invalid"/>. A record's positional constructor is public and cannot be
/// hidden, so the policy re-checks rather than relaying blindly.
/// <see cref="AuditTrailAccess.Refused"/> carries no message: the route answers a refusal with
/// <c>Forbid()</c>, and a rule-supplied message there would be a new response body.
/// </remarks>
/// <param name="Result">The decision.</param>
/// <param name="Message">
/// Module-supplied text for <see cref="AuditTrailAccess.Invalid"/>, relayed verbatim. Null otherwise.
/// </param>
public sealed record AuditTrailAccessOutcome(AuditTrailAccess Result, string? Message)
{
    /// <summary>This caller may read this entity's audit trail.</summary>
    public static AuditTrailAccessOutcome Permitted { get; } =
        new(AuditTrailAccess.Permitted, null);

    /// <summary>
    /// This caller may not — whether the entity is not theirs or does not exist. Indistinguishable
    /// on purpose.
    /// </summary>
    public static AuditTrailAccessOutcome Refused { get; } =
        new(AuditTrailAccess.Refused, null);

    /// <summary>
    /// The request is malformed for this entity type; <paramref name="message"/> reaches the caller
    /// verbatim as the response body.
    /// </summary>
    public static AuditTrailAccessOutcome Invalid(string message) =>
        new(AuditTrailAccess.Invalid, message);
}

/// <summary>
/// The three outcomes a rule can reach.
/// </summary>
/// <remarks>
/// Three, because the route has three rule-relevant responses: 200 for an owned entity, 403 for an
/// unowned or missing one, and 400 with a module-supplied message for an unparseable identifier.
/// Every other response — 401, the route policy's own 403, the missing-parameter 400, the
/// admin-bypass 200, the unregistered-type 200, an unhandled 500 — is decided before or without a
/// rule.
/// </remarks>
public enum AuditTrailAccess
{
    /// <summary>Answer the caller with the audit trail.</summary>
    Permitted,

    /// <summary>Refuse the caller.</summary>
    Refused,

    /// <summary>Reject the request as malformed, relaying the rule's message.</summary>
    Invalid,
}
