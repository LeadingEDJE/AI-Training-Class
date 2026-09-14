namespace LeadingEDJE.Leap.Api.Platform.Services.Notifications;

/// <summary>
/// The SES configuration <see cref="SesApiEmailSender"/> sends with, as read from the <c>Email</c>
/// section by <see cref="SesApiEmailSender.ReadSettings"/>.
/// </summary>
/// <param name="Region">
/// The AWS region whose SES endpoint the send goes to, from <c>Email:SesRegion</c>. Never blank — a
/// blank region yields no settings at all, and no send.
/// </param>
/// <param name="FromAddress">The envelope sender address.</param>
/// <param name="FromName">The display name paired with <paramref name="FromAddress"/>.</param>
public sealed record SesEmailSettings(
    string Region,
    string FromAddress,
    string FromName);
