namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// The system-generated audit reasons Compass configuration writes supply.
/// </summary>
/// <remarks>
/// The reason is generated rather than collected: <c>AuditService.LogAsync</c> throws
/// <see cref="ArgumentException"/> when an entry's reason is null or whitespace, so every audited
/// write must supply one, but none of AC-17, AC-21 or AC-23 asks an administrator to type a reason.
/// Do not add a reason field to a configuration form; that would invent a requirement the PRD does not
/// carry. Not every Compass write comes through here — lookup administration is deliberately outside
/// the audit trail (FR-008, AC-NFR-3: "Lookup tables (employee types, invoice frequency types) are not
/// audited"), and that asymmetry is intentional rather than a gap to fix.
/// </remarks>
public static class CompassAuditReason
{
    private const string Origin = "via Compass configuration";

    /// <summary>The reason recorded when a configuration record is created.</summary>
    /// <param name="subject">What was created, e.g. <c>EDJEr</c>.</param>
    /// <returns>A non-blank reason.</returns>
    public static string Created(string subject) => Compose(subject, "created");

    /// <summary>The reason recorded when a configuration record is updated.</summary>
    /// <param name="subject">What was updated, e.g. <c>Client</c>.</param>
    /// <returns>A non-blank reason.</returns>
    public static string Updated(string subject) => Compose(subject, "updated");

    /// <summary>
    /// The reason recorded when a record is permanently deleted. Why a true delete exists against
    /// Principle VIII: the remarks on <c>CompassAssignmentService.DeleteAsync</c>.
    /// </summary>
    /// <param name="subject">What was deleted, e.g. <c>Assignment</c>.</param>
    /// <returns>A non-blank reason.</returns>
    public static string Deleted(string subject) => Compose(subject, "deleted");

    /// <summary>
    /// Builds the reason, refusing a blank subject at the call site rather than letting the audit
    /// service reject it later with a message that names the audit plumbing instead of the caller.
    /// </summary>
    private static string Compose(string subject, string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        return $"{subject} {operation} {Origin}";
    }
}
