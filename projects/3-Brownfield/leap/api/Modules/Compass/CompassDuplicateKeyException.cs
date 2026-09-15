namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// A Compass write lost a race and was rejected by a unique index.
/// </summary>
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
    /// Why the catch sites need this. Every Compass table carries exactly one unique index, so a
    /// missing constraint name always means the pre-checked field collided. Required in practice: a
    /// provider that omits it should be treated as a data integrity error requiring investigation
    /// (see the constraint naming spec).
    /// </remarks>
    public string? ConstraintName { get; }
}
