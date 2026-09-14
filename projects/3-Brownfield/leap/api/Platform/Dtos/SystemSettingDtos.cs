namespace LeadingEDJE.Leap.Api.Platform.Dtos;

/// <summary>
/// API response projection of a system configuration setting.
/// </summary>
public record SystemSettingResponse(string Key, string Value, string Description);

/// <summary>
/// Request to create a new system configuration setting.
/// </summary>
public record CreateSystemSettingRequest(string Key, string Value, string Description);

/// <summary>
/// Request to update an existing system configuration setting.
/// </summary>
public record UpdateSystemSettingRequest(string Value, string Description);
