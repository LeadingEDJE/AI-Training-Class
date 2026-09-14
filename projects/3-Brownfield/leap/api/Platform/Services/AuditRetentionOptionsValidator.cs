namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Rejects an unusable audit-retention window before the application serves traffic (ADR-007).
/// </summary>
/// <remarks>
/// Fail fast rather than log-and-continue, the same choice as <c>GroupRoleMappingValidator</c> in
/// <c>Program.cs</c>. Without this, a typo in <c>Retention:AuditEntryYears</c> leaves the application
/// running with the purge quietly disabled and audit rows accumulating forever.
/// It validates rather than clamps: coercing a bad value to the default would let the deploy succeed
/// while ignoring what the operator actually asked for.
/// The runtime guard in <see cref="AuditRetentionService"/> is not redundant. This validator covers
/// only values arriving through configuration binding at startup; the guard covers a service
/// constructed directly, such as a test passing <c>Options.Create(...)</c>.
/// </remarks>
public static class AuditRetentionOptionsValidator
{
    /// <summary>Throws when the configured window could not produce a sane cutoff.</summary>
    /// <param name="options">The bound options.</param>
    /// <exception cref="InvalidOperationException">
    /// When <see cref="AuditRetentionOptions.AuditEntryYears"/> is zero or negative.
    /// </exception>
    public static void Validate(AuditRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.AuditEntryYears <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid {AuditRetentionOptions.SectionName}:"
                + $"{nameof(AuditRetentionOptions.AuditEntryYears)} = "
                + $"{options.AuditEntryYears}. The audit retention window must be at least 1 year "
                + "(ADR-007 sets 2). A zero or negative window would compute a cutoff at or after "
                + "'now', which is a request to purge the entire audit trail — so it is refused rather "
                + "than honoured or silently corrected. Remove the setting to use the ADR's default.");
        }
    }
}
