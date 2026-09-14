
using LeadingEDJE.Leap.Api.Platform.Interfaces;
namespace LeadingEDJE.Leap.Api.Platform.Services.Notifications;

/// <summary>
/// In-memory email sender that captures sent emails for test assertions.
/// </summary>
/// <remarks>
/// <c>sealed</c> is load-bearing, not tidiness. <see cref="EnvironmentStampingEmailSender"/>
/// exempts this type from the issue #592 fail-closed suppression by asking
/// <c>inner is not MockEmailSender</c> — which is <see langword="false"/> for a subclass. Unsealed, a
/// derived sender that could actually reach the network would inherit the exemption belonging to one
/// that cannot, and non-production mail would reach the real recipient.
/// </remarks>
public sealed class MockEmailSender : IEmailSender
{
    /// <summary>
    /// Captures every email that would have been sent, in order, for test assertions. <c>From</c> is the
    /// explicit sender the caller passed, or <see langword="null"/> when it relied on the configured default.
    /// </summary>
    public List<(string To, string Subject, string Body, EmailFrom? From, EmailBodyFormat Format)> SentEmails { get; } = [];

    /// <summary>Records the email in <see cref="SentEmails"/> and returns a completed task.</summary>
    public Task SendAsync(
        string toEmail,
        string subject,
        string body,
        EmailBodyFormat format = EmailBodyFormat.Html,
        EmailFrom? from = null)
    {
        SentEmails.Add((toEmail, subject, body, from, format));
        return Task.CompletedTask;
    }
}
