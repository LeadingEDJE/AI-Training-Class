namespace LeadingEDJE.Leap.Api.Platform.Interfaces;

/// <summary>Sends transactional email notifications (submission confirmations, approval alerts, etc.).</summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends a single email to <paramref name="toEmail"/>. <paramref name="format"/> declares what
    /// <paramref name="body"/> is; a <see langword="null"/> <paramref name="from"/> uses the default sender.
    /// </summary>
    /// <remarks>
    /// <paramref name="format"/> defaults to <see cref="EmailBodyFormat.Html"/> only so that adding it
    /// changed no existing behaviour. Production call sites must still pass it explicitly, which
    /// <c>EmailBodyFormatCallSiteTests</c> enforces — a default nobody states is the sniffing this
    /// parameter replaced, one indirection further back.
    /// </remarks>
    Task SendAsync(
        string toEmail,
        string subject,
        string body,
        EmailBodyFormat format = EmailBodyFormat.Html,
        EmailFrom? from = null);
}
