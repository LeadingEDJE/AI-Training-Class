using System.Net.Mail;
using Amazon;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;

namespace LeadingEDJE.Leap.Api.Platform.Services.Notifications;

/// <summary>
/// Sends branded HTML email through the AWS SES API, with an automatic plain-text alternative, under
/// the credentials the pod's IRSA role provides.
/// </summary>
/// <remarks>
/// SES v1 (<c>Amazon.SimpleEmail</c>) rather than SESv2 is a constraint: the <c>ses:FromAddress</c>
/// condition applies to the v1 actions and that condition is the role's whole permission boundary,
/// so a v2 client would send under a policy written for a different action set. It replaced SMTP
/// outright because an SES SMTP password derives from a long-lived IAM user access key, which IRSA's
/// temporary web-identity credentials cannot supply. The From address is the one thing this cannot
/// get wrong quietly — both identity verification and the role condition are evaluated against it —
/// and <see cref="EnvironmentStampingEmailSender"/> is what keeps a non-production send to an address
/// this account holds. Detail: <c>docs/ops/email-environment-labelling.md</c>.
/// </remarks>
public class SesApiEmailSender(
    IAmazonSimpleEmailService ses,
    IConfiguration config,
    ILogger<SesApiEmailSender> logger) : IEmailSender
{
    /// <summary>SES defaults to 7-bit ASCII, which mangles any accented name or client.</summary>
    private const string Utf8 = "UTF-8";

    /// <summary>
    /// Sends an HTML email through the SES API. No-ops with a warning if <c>Email:SesRegion</c> is
    /// not configured.
    /// </summary>
    /// <remarks>
    /// A refusal from SES is deliberately not caught: <c>NotificationService</c> and
    /// <c>NotificationRetryJob</c> are built on the send throwing, which is how a notification is
    /// recorded as failed and picked up for retry, so swallowing an unverified-identity or
    /// <c>ses:FromAddress</c> refusal would turn a non-delivery into a silent success. An absent
    /// region returns normally instead, because <c>CoachNotifier</c>, <c>OotoEmailService</c> and the
    /// retry job all reach this seam after their write has committed — a fault on the
    /// nothing-is-configured path would fail an action that already succeeded.
    /// </remarks>
    public async Task SendAsync(
        string toEmail,
        string subject,
        string body,
        EmailBodyFormat format = EmailBodyFormat.Html,
        EmailFrom? from = null)
    {
        var settings = ReadSettings(config);
        if (settings is null)
        {
            // RedactEmail, not the raw address: CodeQL cs/exposure-of-sensitive-information, which
            // also covers the newline-stripping the log message requires.
            logger.LogWarning(
                "Email:SesRegion not configured, skipping email send to {To}",
                LogSanitizer.RedactEmail(toEmail));
            return;
        }

        var (fromAddress, fromName) = ResolveFrom(from, settings);

        var response = await ses.SendEmailAsync(
            BuildRequest(fromAddress, fromName, toEmail, subject, body, format));

        // RedactEmail for the recipient (CodeQL cs/exposure-of-sensitive-information); Clean is still
        // right for the subject, which is not personal data and is worth reading in full. The message
        // id is SES's own opaque handle, and it is what makes a delivery traceable in the SES console
        // from a log line.
        logger.LogInformation(
            "Email sent to {To} with subject '{Subject}' (SES message {MessageId})",
            LogSanitizer.RedactEmail(toEmail),
            LogSanitizer.Clean(subject),
            response.MessageId);
    }

    /// <summary>
    /// Reads the SES configuration, or <see langword="null"/> when no region is configured and
    /// nothing should be sent. Public so a test can reach the configuration read.
    /// </summary>
    /// <remarks>
    /// Split out because everything past the request — signing, the HTTPS call, credential resolution
    /// — needs AWS, so lookups left inline in a send are reachable by no test, and a key read under a
    /// name no deploy path sets is a bug nothing else notices
    /// (<c>EmailConfigurationKeyParityTests</c>). Blank means unconfigured rather than "region
    /// unset": Helm renders an unset value as <c>""</c> and a stray-space value verbatim, and neither
    /// is a region. <c>Program.cs</c> keys the transport selection on the same test, so the two agree
    /// on what "configured" means.
    /// </remarks>
    public static SesEmailSettings? ReadSettings(IConfiguration config)
    {
        var region = config["Email:SesRegion"];
        if (string.IsNullOrWhiteSpace(region))
        {
            return null;
        }

        return new SesEmailSettings(
            Region: region.Trim(),
            FromAddress: config["Email:FromAddress"] ?? "timesheet@leadingedje.com",
            FromName: config["Email:FromName"] ?? "LeadingEDJE Timesheet");
    }

    /// <summary>
    /// Resolves the From address and display name for a send.
    /// </summary>
    /// <remarks>
    /// All-or-nothing on purpose: an explicit <paramref name="from"/> supplies both the address and
    /// the display name, so a caller sending from <c>no-reply@</c> with an empty name (Compass,
    /// AC-43) gets an address-only From and never inherits the configured <c>Email:FromName</c>. A
    /// <see langword="null"/> <paramref name="from"/> falls back entirely to that configured default.
    /// </remarks>
    public static (string Address, string Name) ResolveFrom(EmailFrom? from, SesEmailSettings settings) =>
        from is null ? (settings.FromAddress, settings.FromName) : (from.Address, from.DisplayName);

    /// <summary>
    /// Builds the SES client every send goes through, pointed at the region in
    /// <paramref name="settings"/> and carrying no explicit credentials.
    /// </summary>
    /// <remarks>
    /// The region is explicit because it is not the pod's region: the container's <c>AWS_REGION</c>
    /// is what everything else in this application talks to, and letting the SDK's default chain
    /// answer would tie SES routing to it. SES identities are per-region, and a send routed to a
    /// region that holds none is refused in terms naming neither the region nor the identity. No
    /// credentials argument, deliberately — the default chain reads the <c>AWS_ROLE_ARN</c> and
    /// <c>AWS_WEB_IDENTITY_TOKEN_FILE</c> variables EKS injects and calls
    /// <c>AssumeRoleWithWebIdentity</c>; passing credentials would bypass the role. SDK v4 defers
    /// credential resolution to the first request, so a host with no identity still builds a client.
    /// </remarks>
    public static AmazonSimpleEmailServiceClient CreateClient(SesEmailSettings settings) =>
        new(RegionEndpoint.GetBySystemName(settings.Region));

    /// <summary>
    /// Builds the <see cref="SendEmailRequest"/> handed to SES, carrying the body as the
    /// <paramref name="format"/> the caller declared.
    /// </summary>
    /// <remarks>
    /// SES takes one <c>Source</c> string rather than an address and a name, so the display name is
    /// folded into it — see <see cref="FormatSource"/>. An HTML body carries a derived text
    /// alternative, for a client that renders no markup. A plain-text body carries NO html part
    /// (#598): an html part is what makes a client treat the text as markup, which turns a name
    /// containing <c>&amp;</c> into a broken entity, swallows everything after a <c>&lt;</c>, and
    /// collapses the newlines.
    /// </remarks>
    public static SendEmailRequest BuildRequest(
        string fromAddress,
        string fromName,
        string toEmail,
        string subject,
        string body,
        EmailBodyFormat format = EmailBodyFormat.Html) =>
        new()
        {
            Source = FormatSource(fromAddress, fromName),
            Destination = new Destination { ToAddresses = [toEmail] },
            Message = new Message
            {
                Subject = new Content { Charset = Utf8, Data = subject },
                Body = format == EmailBodyFormat.PlainText
                    ? new Body { Text = new Content { Charset = Utf8, Data = body } }
                    : new Body
                    {
                        Html = new Content { Charset = Utf8, Data = body },
                        Text = new Content { Charset = Utf8, Data = EmailTemplates.HtmlToPlainText(body) },
                    },
            },
        };

    /// <summary>
    /// Formats the single <c>Source</c> header value SES expects: the bare address when there is no
    /// display name, and a quoted <c>"Name" &lt;address&gt;</c> pair when there is.
    /// </summary>
    /// <remarks>
    /// <see cref="MailAddress"/> rather than string concatenation because it quotes and escapes the
    /// display name properly and returns the bare address when the name is empty — which matters:
    /// an address-only From must not acquire an empty pair of quotes. SES extracts the address from
    /// this value for both identity verification and the role's <c>ses:FromAddress</c> condition, so
    /// a malformed pair is a refusal, not a cosmetic defect.
    /// </remarks>
    private static string FormatSource(string address, string name) =>
        string.IsNullOrEmpty(name) ? address : new MailAddress(address, name).ToString();
}
