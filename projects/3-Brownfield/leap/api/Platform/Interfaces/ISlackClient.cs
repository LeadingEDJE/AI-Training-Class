using LeadingEDJE.Leap.Api.Platform.Dtos;

namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>Sends Slack direct messages and resolves email-to-SlackUserId mappings.</summary>
public interface ISlackClient
{
    /// <summary>Sends a plain-text direct message to the given Slack user.</summary>
    Task SendDirectMessageAsync(string slackUserId, NotificationPayload payload);

    /// <summary>Resolves a Slack user id from an email address via Slack's <c>users.lookupByEmail</c>. Returns null if no matching user exists.</summary>
    Task<string?> ResolveSlackUserIdAsync(string email);
}
