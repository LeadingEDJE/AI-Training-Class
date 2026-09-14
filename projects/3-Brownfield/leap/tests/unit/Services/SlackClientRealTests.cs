using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Services.Slack;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SlackNet;

namespace LeadingEDJE.Leap.Api.Tests.Services;

/// <summary>
/// Tests that exercise the real Slack API against the test workspace.
/// Skipped automatically when SLACK_BOT_TOKEN env var is not set.
///
/// To run locally:
///   export SLACK_BOT_TOKEN="xoxb-..."
///   dotnet test --project LeadingEDJE.Leap.Api.Tests --filter SlackClientRealTests
/// </summary>
public class SlackClientRealTests
{
    private const string DefaultTestUserId = "U0ALJFS4PAT";
    private const string DefaultTestEmail = "avery.quinn@example.com";

    private static string? BotToken => Environment.GetEnvironmentVariable("SLACK_BOT_TOKEN");
    private static string TestUserId =>
        Environment.GetEnvironmentVariable("SLACK_TEST_USER_ID") ?? DefaultTestUserId;
    private static string TestEmail =>
        Environment.GetEnvironmentVariable("SLACK_TEST_EMAIL") ?? DefaultTestEmail;

    private static SlackClient CreateRealClient()
    {
        var apiClient = new SlackServiceBuilder()
            .UseApiToken(BotToken!)
            .GetApiClient();
        return new SlackClient(apiClient, NullLogger<SlackClient>.Instance);
    }

    [Fact]
    public async Task ResolveSlackUserIdAsync_KnownEmail_ReturnsUserId()
    {
        if (string.IsNullOrEmpty(BotToken))
        {
            Assert.Skip("Requires SLACK_BOT_TOKEN env var");
            return;
        }

        // Arrange
        var client = CreateRealClient();

        // Act
        var userId = await client.ResolveSlackUserIdAsync(TestEmail);

        // Assert
        userId.ShouldNotBeNullOrEmpty();
        userId.ShouldBe(TestUserId);
    }

    [Fact]
    public async Task ResolveSlackUserIdAsync_UnknownEmail_ReturnsNull()
    {
        if (string.IsNullOrEmpty(BotToken))
        {
            Assert.Skip("Requires SLACK_BOT_TOKEN env var");
            return;
        }

        // Arrange
        var client = CreateRealClient();

        // Act
        var userId = await client.ResolveSlackUserIdAsync("nobody-exists-xyzzy-99@fake-domain-test.com");

        // Assert
        userId.ShouldBeNull();
    }

    [Fact]
    public async Task SendDirectMessageAsync_DeliversWithoutError()
    {
        if (string.IsNullOrEmpty(BotToken))
        {
            Assert.Skip("Requires SLACK_BOT_TOKEN env var");
            return;
        }

        // Arrange
        var client = CreateRealClient();
        var payload = new NotificationPayload(
            Subject: "[Integration Test] Direct Message",
            Body: $"Automated test at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            EmployeeName: "Test Runner",
            WeekEndDate: DateOnly.FromDateTime(DateTime.UtcNow));

        // Act & Assert — no exception means Slack accepted the message
        await Should.NotThrowAsync(() =>
            client.SendDirectMessageAsync(TestUserId, payload));
    }
}
