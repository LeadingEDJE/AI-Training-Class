namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// The system-generated audit reasons Compass configuration writes supply.
/// </summary>
/// <remarks>
/// Every Compass write, including lookup table administration, is routed through this class so the
/// audit trail has full lookup-table coverage — see the lookup-audit-coverage design note.
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
    /// The reason recorded when a record is permanently deleted.
    /// </summary>
    /// <param name="subject">What was deleted, e.g. <c>Assignment</c>.</param>
    /// <returns>A non-blank reason.</returns>
    public static string Deleted(string subject) => Compose(subject, "deleted");

    /// <summary>
    /// Builds the reason string.
    /// </summary>
    private static string Compose(string subject, string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        return $"{subject} {operation} {Origin}";
    }
}
