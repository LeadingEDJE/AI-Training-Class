using LeadingEDJE.Leap.Api.Platform.Dtos;
using LeadingEDJE.Leap.Api.Platform.Services.Slack;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Services;

public class MockSlackClientTests
{
    [Fact]
    public async Task SendDirectMessageAsync_RecordsMessage()
    {
        // Arrange
        var client = new MockSlackClient();
        var payload = new NotificationPayload("Test Subject", "Test Body");

        // Act
        await client.SendDirectMessageAsync("U12345", payload);

        // Assert
        client.SentMessages.Count.ShouldBe(1);
        client.SentMessages[0].SlackUserId.ShouldBe("U12345");
        client.SentMessages[0].Payload.ShouldBe(payload);
    }

    [Fact]
    public async Task ResolveSlackUserIdAsync_ReturnsNull()
    {
        // Arrange
        var client = new MockSlackClient();

        // Act
        var result = await client.ResolveSlackUserIdAsync("test@example.com");

        // Assert
        result.ShouldBeNull();
    }
}
