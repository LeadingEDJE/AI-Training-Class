namespace LeadingEDJE.Leap.Api.Platform.Dtos;

/// <summary>Response returned to Slack after processing an interactive action webhook.</summary>
/// <param name="Text">Message text displayed to the user in Slack.</param>
public record SlackActionResponse(string Text);
