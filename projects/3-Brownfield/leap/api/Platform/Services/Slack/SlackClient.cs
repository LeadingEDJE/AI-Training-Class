using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using SlackNet;
using SlackNet.Blocks;
using SlackNet.WebApi;

namespace LeadingEDJE.Leap.Api.Platform.Services.Slack;

/// <summary>
/// Production Slack client that sends direct messages and block kit messages via the SlackNet API.
/// </summary>
public class SlackClient(ISlackApiClient slackApiClient, ILogger<SlackClient> logger) : ISlackClient
{
    /// <summary>
    /// Opens (or reuses) a DM channel with the Slack user and posts a Block Kit message derived from
    /// <paramref name="payload"/>, including an Approve button when the payload carries a timesheet id.
    /// </summary>
    public async Task SendDirectMessageAsync(string slackUserId, NotificationPayload payload)
    {
        var channelId = await slackApiClient.Conversations.Open(new[] { slackUserId });

        var textParts = new List<string> { $"*{payload.Subject}*", payload.Body };
        if (payload.EmployeeName is not null)
        {
            textParts.Add($"Employee: {payload.EmployeeName}");
        }

        if (payload.WeekEndDate is not null)
        {
            textParts.Add($"Week ending: {payload.WeekEndDate:yyyy-MM-dd}");
        }

        var blocks = new List<Block>
        {
            new SectionBlock
            {
                Text = new Markdown(string.Join("\n", textParts))
            }
        };

        if (payload.TimesheetId is not null)
        {
            blocks.Add(new ActionsBlock
            {
                Elements =
                {
                    new Button
                    {
                        Text = new PlainText("Approve"),
                        ActionId = "approve_timesheet",
                        Value = payload.TimesheetId.ToString(),
                        Style = ButtonStyle.Primary
                    }
                }
            });
        }

        await slackApiClient.Chat.PostMessage(new Message
        {
            Channel = channelId,
            Blocks = blocks
        });

        logger.LogInformation(
            "Slack DM sent to {SlackUserId} for timesheet {TimesheetId}",
            slackUserId, payload.TimesheetId);
    }

    /// <summary>Resolves a Slack user id from an email via <c>users.lookupByEmail</c>; returns <c>null</c> on Slack errors.</summary>
    public async Task<string?> ResolveSlackUserIdAsync(string email)
    {
        try
        {
            var user = await slackApiClient.Users.LookupByEmail(email);
            return user.Id;
        }
        catch (SlackException ex)
        {
            logger.LogDebug(ex, "Could not resolve Slack user for email {Email}", email);
            return null;
        }
    }
}
