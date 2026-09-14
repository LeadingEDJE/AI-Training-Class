namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// A Compass write lost a race and was rejected by a unique index.
/// </summary>
/// <remarks>
/// A module-level exception rather than the provider's own. Every uniqueness guard here is a
/// check-then-act, so a concurrent caller can commit the same name between the check and the write;
/// the database rejects the loser with SQLSTATE 23505, which unhandled surfaces as a bare HTTP 500
/// where the caller should see the 409 the sequential path produces.
///
/// The translation happens in <c>CompassUnitOfWork</c>, under <c>Data/</c> — the only place this
/// module may name provider types, because <c>CompassBoundaryTests</c> fails the build if a file under
/// <c>Services/</c> contains the token <c>DbContext</c>. SQLSTATE 23505 detection also exists in
/// <c>UserRoleService.IsDuplicateKeyViolation</c>; extract to <c>api/Platform/</c> before a third.
/// </remarks>
public sealed class CompassDuplicateKeyException : Exception
{
    /// <summary>Creates the exception with a specific message.</summary>
    /// <param name="message">What collided.</param>
    /// <param name="constraintName">The unique index that rejected the write, when known.</param>
    public CompassDuplicateKeyException(string message, string? constraintName = null)
        : base(message) => ConstraintName = constraintName;

    /// <summary>Creates the exception wrapping the provider's own failure.</summary>
    /// <param name="message">What collided.</param>
    /// <param name="innerException">The provider exception that reported the violation.</param>
    /// <param name="constraintName">The unique index that rejected the write, when known.</param>
    public CompassDuplicateKeyException(
        string message,
        Exception innerException,
        string? constraintName = null
    )
        : base(message, innerException) => ConstraintName = constraintName;

    /// <summary>The unique index that rejected the write, or null when the provider did not say.</summary>
    /// <remarks>
    /// Why the catch sites need this. Three Compass tables carry two unique indexes — a field the
    /// service pre-checks, and <c>legacy_tps_id</c>, which nothing pre-checks. Without the constraint
    /// name a handler can only guess the pre-checked field, and would tell a migration run that
    /// supplied a duplicate provenance identifier that its email was taken. Optional rather than
    /// required because a provider that reports no constraint name must still translate to a 409
    /// rather than escaping as a 500.
    /// </remarks>
    public string? ConstraintName { get; }
}
