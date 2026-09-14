using System.Text.RegularExpressions;
using LeadingEDJE.Leap.Api.Platform.Interfaces;
using LeadingEDJE.Leap.Api.Platform.Services.Logging;
using Microsoft.Extensions.Options;

namespace LeadingEDJE.Leap.Api.Platform.Services.Notifications;

/// <summary>
/// Decorates an <see cref="IEmailSender"/> so a message says which environment sent it, a
/// non-production environment can stop sending, and its mail leaves as no real recipient or sender.
/// </summary>
/// <remarks>
/// A decorator rather than a change to <see cref="EmailTemplates"/>: the four callers disagree on
/// what a body is and only two go through <c>WrapInLayout</c>, so labelling the layout would leave
/// every Compass and OOTO notice unmarked. It labels off explicit config, since the host cannot tell
/// deployed dev from a PR preview (both Staging), but reroutes and rewrites the From address off
/// <c>IHostEnvironment.IsProduction()</c>, so an absent label cannot read as production. Do not
/// collapse them. Fails closed over a real transport; <see cref="MockEmailSender"/> is exempt
/// because it can reach nobody. Signals, residual and environment matrix:
/// <c>docs/ops/email-environment-labelling.md</c>.
/// </remarks>
public partial class EnvironmentStampingEmailSender(
    IEmailSender inner,
    IOptions<EmailEnvironmentOptions> options,
    IHostEnvironment hostEnvironment,
    ILogger<EnvironmentStampingEmailSender> logger
) : IEmailSender
{
    /// <summary>The banner's fill — amber, so it does not read as part of the brand-green layout.</summary>
    private const string BannerBackground = "#b45309";

    /// <summary>
    /// Whether the sender behind this decorator can actually deliver to the outside world.
    /// </summary>
    /// <remarks>
    /// Phrased as "is not the in-memory outbox" rather than "is the SES sender" so it fails closed:
    /// a future transport nobody remembers to add here is treated as real, which is the safe way round
    /// for a control whose job is to stop mail reaching colleagues.
    /// </remarks>
    private bool InnerCanReachTheOutsideWorld => inner is not MockEmailSender;

    /// <inheritdoc />
    public async Task SendAsync(
        string toEmail,
        string subject,
        string body,
        EmailBodyFormat format = EmailBodyFormat.Html,
        EmailFrom? from = null)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            // Returning normally rather than throwing is load-bearing. CoachNotifier reaches this seam
            // after its write has committed, so a mailer that faulted here would fail an action that
            // already succeeded (see the remarks on CoachNotifier.NotifyAsync).
            // RedactEmail for the recipient (CodeQL cs/exposure-of-sensitive-information). The subject
            // stays readable: it is not personal data, and is the only clue to what was dropped.
            logger.LogInformation(
                "Email delivery is disabled; suppressed message to {To} with subject '{Subject}'",
                LogSanitizer.RedactEmail(toEmail),
                LogSanitizer.Clean(subject)
            );
            return;
        }

        var label = string.IsNullOrWhiteSpace(settings.EnvironmentLabel)
            ? null
            : settings.EnvironmentLabel.Trim();

        var stampedSubject = label is null ? subject : StampSubject(subject, label);

        // The stamped BODY is deliberately not hoisted alongside it. The redirect branch below builds
        // its own body -- one notice carrying the label AND the intended recipient -- so a hoisted
        // `stampedBody` would sit in scope there looking like the tidier argument to pass, and passing
        // it would banner the message twice.

        // Production: byte-identical to the pre-#592 behaviour, and no new log line. A redirect address
        // left in production configuration is ignored rather than honoured -- production mail must
        // never be divertible to a dev sink by a stray value.
        if (hostEnvironment.IsProduction())
        {
            await inner.SendAsync(toEmail, stampedSubject, StampBody(body, label, format), format, from);
            return;
        }

        // Production has already returned, so every remaining send leaves as the configured
        // non-production sender. The override wins over the caller's value including when that value
        // is null, because a null `from` at this seam means "use the configured default" and that
        // default is a production address -- left alone it would send every timesheet notice as an
        // unverified identity. No override configured falls back to `from`, changing nothing.
        var effectiveFrom = NonProductionFrom(settings) ?? from;

        var redirectTo = settings.RedirectAllTo?.Trim();
        if (string.IsNullOrEmpty(redirectTo))
        {
            if (InnerCanReachTheOutsideWorld)
            {
                // Fail closed (issue #592). Error, not Information: this is a message the system was
                // asked to send and did not, and the operator has to be able to find it. The message
                // names the key that fixes it, because the reader of this line is usually the person
                // who just switched SMTP on. Returning normally for the same reason as the kill switch
                // above -- the caller's write has already committed.
                logger.LogError(
                    "Non-production environment '{Environment}' has a real email transport but no "
                        + "Email:RedirectAllTo configured; suppressed message to {To} with subject "
                        + "'{Subject}'. Configure Email__RedirectAllTo to deliver non-production mail "
                        + "to a sink instead.",
                    LogSanitizer.Clean(hostEnvironment.EnvironmentName),
                    LogSanitizer.RedactEmail(toEmail),
                    LogSanitizer.Clean(subject)
                );
                return;
            }

            // The in-memory outbox: nothing can leave the machine, so deliver as before and keep the
            // real recipient visible to whoever is reading the outbox.
            await inner.SendAsync(toEmail, stampedSubject, StampBody(body, label, format), format, effectiveFrom);
            return;
        }

        await inner.SendAsync(
            redirectTo,
            stampedSubject,
            DecorateBody(body, label, intendedRecipient: toEmail, format),
            format,
            effectiveFrom);
    }

    /// <summary>
    /// The sender every non-production message is delivered as, or <see langword="null"/> when this
    /// environment configures none.
    /// </summary>
    /// <remarks>
    /// Call only after the production early-return: a stray
    /// <see cref="EmailEnvironmentOptions.NonProductionFromAddress"/> in production configuration
    /// must be as inert as a stray <see cref="EmailEnvironmentOptions.RedirectAllTo"/> is.
    /// <c>Trim()</c> because Helm renders a stray-space value verbatim and <c>"   "</c> is not an
    /// address -- passed on with a display name it throws in the <c>MailAddress</c> that
    /// <see cref="SesApiEmailSender"/> formats its <c>Source</c> through. The name is taken only
    /// alongside the address: <see cref="EmailFrom"/> is all-or-nothing, so an override must not mix
    /// its address with a caller's name or with <c>Email:FromName</c>.
    /// </remarks>
    private static EmailFrom? NonProductionFrom(EmailEnvironmentOptions settings)
    {
        var address = settings.NonProductionFromAddress?.Trim();

        if (string.IsNullOrEmpty(address))
        {
            return null;
        }

        var displayName = settings.NonProductionFromName?.Trim();

        return new EmailFrom(address, string.IsNullOrEmpty(displayName) ? string.Empty : displayName);
    }

    /// <summary>
    /// Prefixes the subject with the environment label, leaving a subject that already carries it
    /// alone.
    /// </summary>
    /// <remarks>
    /// The idempotency is not decorative: <c>NotificationRetryJob</c> re-sends a stored notice, and
    /// without this a message that failed twice would arrive as <c>[DEV] [DEV] …</c>.
    /// </remarks>
    public static string StampSubject(string subject, string label)
    {
        var prefix = $"[{label}]";

        return subject.StartsWith(prefix, StringComparison.Ordinal)
            ? subject
            : $"{prefix} {subject}";
    }

    /// <summary>
    /// Adds the environment banner, matching its form to the body's. A <see langword="null"/>
    /// <paramref name="label"/> means this environment does not label its mail; the body is untouched.
    /// </summary>
    /// <remarks>
    /// Three shapes arrive here and each needs different handling. A full document takes the banner
    /// inside its <c>&lt;body&gt;</c>; prepending it would put markup ahead of the doctype, which a
    /// mail client may drop. A fragment has no <c>&lt;body&gt;</c> to enter, so the banner goes in
    /// front of it. Plain text must not receive markup at all, or the recipient reads the angle
    /// brackets. The label is HTML-encoded on the markup paths: it is operator-supplied configuration
    /// whose sibling in the web tier (<c>EPHEMERAL_LABEL</c>) is derived from a PR title.
    /// </remarks>
    public static string StampBody(string body, string? label, EmailBodyFormat format = EmailBodyFormat.Html)
        => DecorateBody(body, label, intendedRecipient: null, format);

    /// <summary>
    /// Adds the environment banner when <paramref name="label"/> is supplied and the
    /// <paramref name="intendedRecipient"/> the non-production redirect took the message from.
    /// </summary>
    /// <remarks>
    /// The two lines are independent because their triggers are — a redirect can be configured where
    /// no label is. One function rather than two passes, because three body shapes arrive: a full
    /// document takes the notice INSIDE its <c>&lt;body&gt;</c> (prepending puts markup ahead of the
    /// doctype, which a mail client may drop), a fragment takes it in front, and plain text must
    /// receive no markup at all. Two separate passes would insert two banners, and on the
    /// full-document path insert the second ahead of the first. Both values are HTML-encoded on the
    /// markup paths: the label is operator configuration whose web-tier sibling is derived from a PR
    /// title, and the recipient comes from the directory. Neither is trustworthy in a body.
    /// </remarks>
    private static string DecorateBody(
        string body, string? label, string? intendedRecipient, EmailBodyFormat format)
    {
        if (label is null && intendedRecipient is null)
        {
            return body;
        }

        if (format == EmailBodyFormat.PlainText)
        {
            return $"{PlainTextNotice(label, intendedRecipient)}\n\n{body}";
        }

        var banner = HtmlBanner(label, intendedRecipient);
        var bodyTag = BodyOpenTagRegex().Match(body);

        return bodyTag.Success
            ? body.Insert(bodyTag.Index + bodyTag.Length, $"\n{banner}")
            : $"{banner}\n{body}";
    }

    private static string PlainTextNotice(string? label, string? intendedRecipient)
    {
        var lines = new List<string>(2);

        if (label is not null)
        {
            lines.Add($"[{label}] This message was sent from the {label} environment.");
        }

        if (intendedRecipient is not null)
        {
            lines.Add(
                $"Intended recipient: {intendedRecipient} -- redirected here because this is not "
                    + "production.");
        }

        return string.Join('\n', lines);
    }

    private static string HtmlBanner(string? label, string? intendedRecipient)
    {
        var lines = new List<string>(2);

        if (label is not null)
        {
            var encodedLabel = System.Net.WebUtility.HtmlEncode(label);
            lines.Add($"{encodedLabel} environment &mdash; this message did not come from production.");
        }

        if (intendedRecipient is not null)
        {
            var encodedRecipient = System.Net.WebUtility.HtmlEncode(intendedRecipient);
            lines.Add(
                $"Intended recipient: {encodedRecipient} &mdash; redirected here because this is not "
                    + "production.");
        }

        var content = string.Join("<br />\n  ", lines);

        return $"""
            <div style="background-color: {BannerBackground}; color: #fff; padding: 10px 16px; margin-bottom: 16px; font-family: Arial, sans-serif; font-size: 13px; font-weight: bold; border-radius: 4px;">
              {content}
            </div>
            """;
    }

    [GeneratedRegex("<body[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BodyOpenTagRegex();

}
