namespace LeadingEDJE.Leap.Api.Platform.Services;

/// <summary>
/// Configuration for ADR-007's audit retention window. Bound from the <c>Retention</c> section.
/// </summary>
/// <remarks>
/// Configurable so an operator can see and set the window without a deploy, with ADR-007's value as
/// the default. Nothing sets it today, so every environment runs ADR-007's two years.
/// There is deliberately no setting for business records. "Never purged" is the absence of a policy
/// rather than a large number of years, and a knob would invite someone to put a number in it. The
/// surfaces that read the full history are specified as never truncated by retention.
/// </remarks>
public class AuditRetentionOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Retention";

    /// <summary>
    /// How many years audit entries are kept before they are purged. Defaults to ADR-007's two.
    /// </summary>
    /// <remarks>
    /// A value of zero or less is rejected at startup by <see cref="AuditRetentionOptionsValidator"/>,
    /// wired in <c>Program.cs</c> beside the role-mapping validator, rather than treated as "purge
    /// everything" — a mistyped value must not be able to empty the audit trail, which is the one
    /// failure mode here that cannot be undone. <see cref="AuditRetentionService"/> repeats the check at
    /// runtime for a service constructed outside configuration binding; that guard is belt-and-braces,
    /// not the primary gate.
    /// </remarks>
    public int AuditEntryYears { get; set; } = 2;
}
