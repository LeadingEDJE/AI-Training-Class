namespace LeadingEDJE.Leap.Api.Platform.Domain;

/// <summary>
/// Key-value configuration setting stored in the database for runtime-configurable application behavior.
/// </summary>
public class SystemSetting : AuditableEntity
{
    /// <summary>Setting key — primary identifier (e.g., "payroll.auto_lock_cutoff_hours").</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Setting value, stored as string. Consumers parse to the expected type.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Operator-facing explanation of what the setting controls.</summary>
    public string Description { get; set; } = string.Empty;
}
