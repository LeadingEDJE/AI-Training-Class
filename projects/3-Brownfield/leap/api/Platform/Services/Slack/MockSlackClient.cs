using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;

namespace LeadingEDJE.Leap.Api.Platform.Services.Slack;

/// <summary>
/// In-memory Slack client that captures sent messages for test assertions.
/// </summary>
public class MockSlackClient : ISlackClient
{
    /// <summary>Captures every direct message sent, in order, for test assertions.</summary>
    public List<(string SlackUserId, NotificationPayload Payload)> SentMessages { get; } = [];

    /// <summary>Records the DM in <see cref="SentMessages"/> and returns a completed task.</summary>
    public Task SendDirectMessageAsync(string slackUserId, NotificationPayload payload)
    {
        SentMessages.Add((slackUserId, payload));
        return Task.CompletedTask;
    }

    /// <summary>Always returns <c>null</c>; the test double does not resolve Slack IDs.</summary>
    public Task<string?> ResolveSlackUserIdAsync(string email) =>
        Task.FromResult<string?>(null);
}
